using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MusicPlayer.Views
{
    /// <summary>
    /// Lightweight, thread-safe diagnostics metrics for VirtualizingBalancedPanel
    /// providing real-time instrumentation of layout duration and container reuse.
    /// </summary>
    public static class PanelMetrics
    {
        public static long MeasureCount;
        public static long MeasureTotalMs;
        public static long ArrangeCount;
        public static long ArrangeTotalMs;
        public static long FullResetCount;
        public static long ContainersCreated;
        public static long ContainersReused;
        public static long ContainersRemoved;
        public static int LastVisibleItemCount;
        public static int LastBufferItemCount;
        public static double LastMeasureDurationMs;
        public static double LastArrangeDurationMs;

        public static string GetSummary()
        {
            long mCount = Interlocked.Read(ref MeasureCount);
            long mMs = Interlocked.Read(ref MeasureTotalMs);
            long aCount = Interlocked.Read(ref ArrangeCount);
            long aMs = Interlocked.Read(ref ArrangeTotalMs);
            long resets = Interlocked.Read(ref FullResetCount);
            long created = Interlocked.Read(ref ContainersCreated);
            long reused = Interlocked.Read(ref ContainersReused);
            long removed = Interlocked.Read(ref ContainersRemoved);

            double avgMeasure = mCount > 0 ? (double)mMs / mCount : 0;
            double avgArrange = aCount > 0 ? (double)aMs / aCount : 0;

            return $"MeasureCalls={mCount} (avg {avgMeasure:F2}ms), " +
                   $"ArrangeCalls={aCount} (avg {avgArrange:F2}ms), " +
                   $"FullResets={resets}, Created={created}, Reused={reused}, Removed={removed}, " +
                   $"LastVisible={LastVisibleItemCount}, LastBuffer={LastBufferItemCount}";
        }
    }

    /// <summary>
    /// High-performance virtualizing balanced grid panel:
    /// Maintains stable container realization with incremental entry/exit management.
    /// Preserves existing containers across scrolling (preventing DataContext detachment and image churn),
    /// throttles redundant InvalidateMeasure passes on sub-row scroll deltas,
    /// and provides pixel-accurate template-measured extent heights.
    /// </summary>
    public class VirtualizingBalancedPanel : VirtualizingPanel, IScrollInfo
    {
        internal readonly record struct VisibleRange(int VisibleRows, int FirstIndex, int LastIndex, double Offset);

        public static readonly DependencyProperty MinItemWidthProperty =
            DependencyProperty.Register("MinItemWidth", typeof(double), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(140d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty MaxItemWidthProperty =
            DependencyProperty.Register("MaxItemWidth", typeof(double), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(180d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty GapProperty =
            DependencyProperty.Register("Gap", typeof(double), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty CacheRowsProperty =
            DependencyProperty.Register("CacheRows", typeof(int), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(2, FrameworkPropertyMetadataOptions.AffectsMeasure),
                value => value is int rows && rows >= 0);

        private static readonly DependencyPropertyKey ItemWidthPropertyKey =
            DependencyProperty.RegisterReadOnly("ItemWidth", typeof(double), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(150d));

        public static readonly DependencyProperty ItemWidthProperty = ItemWidthPropertyKey.DependencyProperty;

        public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
        public double MaxItemWidth { get => (double)GetValue(MaxItemWidthProperty); set => SetValue(MaxItemWidthProperty, value); }
        public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
        public int CacheRows { get => (int)GetValue(CacheRowsProperty); set => SetValue(CacheRowsProperty, value); }
        public double ItemWidth => (double)GetValue(ItemWidthProperty);

        private double _slot = 150;
        private double _gapH = 8;
        private int _cols = 4;
        private double _rowHeight = 0;
        private double _lastMeasureWidth = 0;
        private int _firstRealizedIndex = -1;
        private int _lastRealizedIndex = -1;
        private int _currentFirstRow = -1;
        private int _currentLastRow = -1;

        // Solve() only depends on the available width and the three sizing DPs.
        // It used to run twice per measure and once per arrange even when nothing
        // changed, and then InvalidateScrollInfo() fired unconditionally.
        private double _solvedWidth = double.NaN;
        private double _solvedMin = double.NaN;
        private double _solvedMax = double.NaN;
        private double _solvedGap = double.NaN;
        private double _lastInfoOffset = double.NaN;

        private Size _extent = new Size(0, 0);
        private Size _viewport = new Size(0, 0);
        private Point _offset = new Point(0, 0);
        private ScrollViewer? _scrollOwner;
        private ScrollViewer? _fallbackScrollViewer;
        private bool _generatorMissingLogged;
        private bool _measureDeferred;

        public VirtualizingBalancedPanel()
        {
            Loaded += (_, _) => InvalidateMeasure();
        }

        public bool CanHorizontallyScroll { get; set; } = false;
        public bool CanVerticallyScroll { get; set; } = true;
        public double ExtentWidth => _extent.Width;
        public double ExtentHeight => _extent.Height;
        public double ViewportWidth => _viewport.Width;
        public double ViewportHeight => _viewport.Height;
        public double HorizontalOffset => _offset.X;
        public double VerticalOffset => _scrollOwner == null && _fallbackScrollViewer != null
            ? _fallbackScrollViewer.VerticalOffset
            : _offset.Y;

        public ScrollViewer? ScrollOwner
        {
            get => _scrollOwner;
            set
            {
                if (ReferenceEquals(_scrollOwner, value)) return;
                _scrollOwner = value;
                EnsureFallbackScrollViewer();
            }
        }

        private ItemsControl? Owner => ItemsControl.GetItemsOwner(this);

        private static bool IsFinitePositive(double value) => double.IsFinite(value) && value > 0;

        private static bool ValuesClose(double a, double b) =>
            Math.Abs(a - b) < 0.001 || (double.IsNaN(a) && double.IsNaN(b));

        private static bool SizesClose(Size a, Size b) =>
            ValuesClose(a.Width, b.Width) && ValuesClose(a.Height, b.Height);

        private static double GetSafeGap(double gap) =>
            double.IsFinite(gap) && gap > 0 ? gap : 0;

        private double ResolveWidth(double width)
        {
            if (IsFinitePositive(width)) return width;
            if (_scrollOwner != null && IsFinitePositive(_scrollOwner.ViewportWidth)) return _scrollOwner.ViewportWidth;
            return IsFinitePositive(MinItemWidth) ? Math.Max(1, MinItemWidth) : 140;
        }

        internal static double ResolveViewportHeight(double availableHeight, double scrollViewerHeight, double verticalPadding)
        {
            if (IsFinitePositive(availableHeight)) return availableHeight;

            double actualViewportHeight = scrollViewerHeight - Math.Max(0, verticalPadding);
            if (IsFinitePositive(actualViewportHeight)) return actualViewportHeight;

            return RowHeightEstimate;
        }

        internal static double ResolveGenerationOffset(bool usesScrollInfo, double panelOffset, double scrollViewerOffset) =>
            usesScrollInfo ? panelOffset : scrollViewerOffset;

        private static double GetExtentHeight(int itemCount, int cols, double rowHeight, double gap)
        {
            if (itemCount <= 0 || cols <= 0) return 0;

            rowHeight = IsFinitePositive(rowHeight) ? rowHeight : RowHeightEstimate;
            gap = GetSafeGap(gap);
            int totalRows = (itemCount - 1) / cols + 1;
            double stride = rowHeight + gap;
            if (!double.IsFinite(stride)) stride = double.MaxValue;

            double extentHeight = (totalRows - 1d) * stride + rowHeight;
            return double.IsFinite(extentHeight) ? extentHeight : double.MaxValue;
        }

        private static double ClampOffset(double offset, double extentHeight, double viewportHeight)
        {
            if (!double.IsFinite(offset)) offset = 0;
            if (!double.IsFinite(extentHeight) || extentHeight < 0) extentHeight = 0;
            if (!IsFinitePositive(viewportHeight)) viewportHeight = 0;
            return Math.Clamp(offset, 0, Math.Max(0, extentHeight - viewportHeight));
        }

        internal static VisibleRange CalculateVisibleRange(
            int itemCount,
            int cols,
            double offset,
            double viewportHeight,
            double rowHeight,
            double gap,
            int cacheRows = 2)
        {
            if (itemCount <= 0 || cols <= 0)
                return new VisibleRange(0, 0, -1, 0);

            rowHeight = IsFinitePositive(rowHeight) ? rowHeight : RowHeightEstimate;
            gap = GetSafeGap(gap);
            viewportHeight = IsFinitePositive(viewportHeight) ? viewportHeight : RowHeightEstimate;
            double stride = rowHeight + gap;
            if (!double.IsFinite(stride)) stride = double.MaxValue;

            int totalRows = (itemCount - 1) / cols + 1;
            double extentHeight = GetExtentHeight(itemCount, cols, rowHeight, gap);
            offset = ClampOffset(offset, extentHeight, viewportHeight);

            double topRowValue = Math.Floor(offset / stride);
            int topRow = !double.IsFinite(topRowValue)
                ? totalRows - 1
                : (int)Math.Min(totalRows - 1d, Math.Max(0, topRowValue));

            double visibleRowsValue = Math.Ceiling(viewportHeight / stride);
            int visibleRows = !double.IsFinite(visibleRowsValue) || visibleRowsValue >= totalRows
                ? totalRows
                : Math.Max(1, (int)visibleRowsValue);

            if (cacheRows < 0) cacheRows = 0;
            int firstRow = Math.Max(0, topRow - cacheRows);
            long lastVisibleRow = Math.Min(totalRows - 1L, (long)topRow + visibleRows - 1);
            int lastRow = (int)Math.Min(totalRows - 1L, lastVisibleRow + cacheRows);
            int firstIndex = (int)Math.Min(itemCount - 1L, (long)firstRow * cols);
            int lastIndex = (int)Math.Min(itemCount - 1L, ((long)lastRow + 1) * cols - 1);

            return new VisibleRange(visibleRows, firstIndex, lastIndex, offset);
        }

        private void Solve(double width)
        {
            var w = Math.Max(1, IsFinitePositive(width) ? width : IsFinitePositive(MinItemWidth) ? MinItemWidth : 140);
            double configuredMin = IsFinitePositive(MinItemWidth) ? MinItemWidth : 140;
            double configuredMax = IsFinitePositive(MaxItemWidth) ? MaxItemWidth : configuredMin;
            double configuredGap = GetSafeGap(Gap);
            if (Math.Abs(w - _solvedWidth) < 0.01 &&
                Math.Abs(configuredMin - _solvedMin) < 0.01 &&
                Math.Abs(configuredMax - _solvedMax) < 0.01 &&
                Math.Abs(configuredGap - _solvedGap) < 0.01)
            {
                return;
            }
            _solvedWidth = w;
            _solvedMin = configuredMin;
            _solvedMax = configuredMax;
            _solvedGap = configuredGap;
            var gap = Math.Min(GetSafeGap(Gap), w);
            var minWidth = IsFinitePositive(MinItemWidth) ? MinItemWidth : 140;
            var maxWidth = IsFinitePositive(MaxItemWidth) ? MaxItemWidth : minWidth;
            var min = Math.Clamp(minWidth, 1d, w);
            var max = Math.Max(min, maxWidth);
            var denominator = min + gap;
            var numerator = w + gap;
            var ratio = double.IsFinite(denominator) && denominator > 0
                ? numerator / denominator
                : 1;
            var cols = !double.IsFinite(ratio) || ratio >= int.MaxValue
                ? int.MaxValue
                : (int)Math.Floor(ratio);
            if (cols < 1) cols = 1;
            var slot = Math.Min(max, (w - gap * (cols - 1)) / cols);
            var used = slot * cols + gap * (cols - 1);
            _slot = Math.Max(1, slot);
            _gapH = gap + (cols > 1 ? Math.Max(0, w - used) / (cols - 1) : 0);
            _cols = Math.Max(1, cols);

        public void SetVerticalOffset(double offset)
        {
            offset = ClampOffset(offset, ExtentHeight, ViewportHeight);
            if (_scrollOwner == null && _fallbackScrollViewer != null)
            {
                _fallbackScrollViewer.ScrollToVerticalOffset(offset);
                return;
            }

            if (Math.Abs(offset - _offset.Y) > 0.001)
            {
                SetValue(ItemWidthPropertyKey, _slot);
            }
        }

        private double ComputeTotalExtent()
        {
            int itemCount = Owner?.Items.Count ?? 0;
            if (itemCount <= 0 || _cols <= 0) return 0;
            double rowH = _rowHeight > 0 ? _rowHeight : (_slot + 44);
            double stride = rowH + Math.Max(0, Gap);
            int totalRows = (itemCount + _cols - 1) / _cols;
            return totalRows > 0 ? (totalRows - 1) * stride + rowH : 0;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                var owner = Owner;
                if (owner == null) return base.MeasureOverride(availableSize);

                HookScrollViewer();

                int prevCols = _cols;
                double prevWidth = _lastMeasureWidth;
                Solve(availableSize.Width);
                if (prevCols != _cols || Math.Abs(prevWidth - availableSize.Width) > 0.5)
                {
                    _rowHeight = 0;
                }
                _lastMeasureWidth = availableSize.Width;

                int itemCount = owner.Items.Count;
                if (itemCount == 0)
                {
                    ResetAllContainers();
                    return new Size(availableSize.Width, 0);
                }

                IItemContainerGenerator generator = ItemContainerGenerator;
                if (generator == null)
                {
                    return new Size(availableSize.Width, 0);
                }

                double effectiveRowHeight = _rowHeight > 0 ? _rowHeight : (_slot > 0 ? _slot + 44 : 200);
                double stride = effectiveRowHeight + Math.Max(0, Gap);
                int cols = _cols > 0 ? _cols : 1;
                int totalRows = (itemCount + cols - 1) / cols;

                // Resolve scroll offset and viewport height from parent ScrollViewer
                double offsetY = 0;
                double viewportH = 800;

                if (_parentScrollViewer != null)
                {
                    offsetY = Math.Max(0, _parentScrollViewer.VerticalOffset);
                    viewportH = _parentScrollViewer.ViewportHeight > 0
                        ? _parentScrollViewer.ViewportHeight
                        : (_parentScrollViewer.ActualHeight > 0 ? _parentScrollViewer.ActualHeight : 800);
                }
                else if (!double.IsInfinity(availableSize.Height) && availableSize.Height > 0)
                {
                    viewportH = availableSize.Height;
                }

                // Compute active row range with buffer rows (1 row above, 2 rows below)
                int topRow = (int)Math.Floor(offsetY / stride);
                int firstRow = Math.Max(0, topRow - 1);
                int visibleRowCount = (int)Math.Ceiling(Math.Max(1, viewportH) / stride);
                int lastRow = Math.Min(totalRows - 1, topRow + visibleRowCount + 2);

                _currentFirstRow = firstRow;
                _currentLastRow = lastRow;

                int reqFirst = firstRow * cols;
                int reqLast = Math.Min(itemCount - 1, (lastRow + 1) * cols - 1);
                if (reqFirst < 0) reqFirst = 0;
                if (reqLast < reqFirst) reqLast = reqFirst - 1;

                int reqCount = reqLast >= reqFirst ? (reqLast - reqFirst + 1) : 0;
                int visibleCount = Math.Min(itemCount, (topRow + visibleRowCount) * cols) - (topRow * cols);
                PanelMetrics.LastVisibleItemCount = Math.Max(0, visibleCount);
                PanelMetrics.LastBufferItemCount = Math.Max(0, reqCount - visibleCount);

                Size childConstraint = new Size(_slot, double.PositiveInfinity);

                // Incremental Container Management:
                // If previous realized range overlaps with requested range, preserve all overlapping containers!
                // Only generate newly needed rows and release rows that scrolled out of view.
                bool canIncrement = _firstRealizedIndex >= 0 &&
                                    _lastRealizedIndex >= _firstRealizedIndex &&
                                    InternalChildren.Count == (_lastRealizedIndex - _firstRealizedIndex + 1) &&
                                    reqLast >= _firstRealizedIndex &&
                                    reqFirst <= _lastRealizedIndex;

                if (canIncrement && reqCount > 0)
                {
                    try
                    {
                        // 1. Remove out-of-range containers from the top (scrolling DOWN)
                        if (_firstRealizedIndex < reqFirst)
                        {
                            int countToRemove = reqFirst - _firstRealizedIndex;
                            generator.Remove(new GeneratorPosition(0, 0), countToRemove);
                            RemoveInternalChildRange(0, countToRemove);
                            _firstRealizedIndex = reqFirst;
                            Interlocked.Add(ref PanelMetrics.ContainersRemoved, countToRemove);
                        }

                        // 2. Remove out-of-range containers from the bottom (scrolling UP)
                        if (_lastRealizedIndex > reqLast)
                        {
                            int countToRemove = _lastRealizedIndex - reqLast;
                            int childStartIndex = InternalChildren.Count - countToRemove;
                            generator.Remove(new GeneratorPosition(childStartIndex, 0), countToRemove);
                            RemoveInternalChildRange(childStartIndex, countToRemove);
                            _lastRealizedIndex = reqLast;
                            Interlocked.Add(ref PanelMetrics.ContainersRemoved, countToRemove);
                        }

                        // 3. Generate newly needed containers at the top (scrolling UP)
                        if (reqFirst < _firstRealizedIndex)
                        {
                            int prevFirst = _firstRealizedIndex;
                            GeneratorPosition startPos = generator.GeneratorPositionFromIndex(reqFirst);
                            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
                            {
                                int insertIndex = 0;
                                for (int i = reqFirst; i < prevFirst; i++)
                                {
                                    var container = generator.GenerateNext(out bool newlyRealized) as UIElement;
                                    if (container == null) break;
                                    InsertInternalChild(insertIndex++, container);
                                    generator.PrepareItemContainer(container);
                                    container.Measure(childConstraint);
                                    if (_rowHeight <= 0 && container.DesiredSize.Height > 0)
                                    {
                                        _rowHeight = container.DesiredSize.Height;
                                    }
                                    Interlocked.Increment(ref PanelMetrics.ContainersCreated);
                                }
                            }
                            _firstRealizedIndex = reqFirst;
                        }

                        // 4. Generate newly needed containers at the bottom (scrolling DOWN)
                        if (reqLast > _lastRealizedIndex)
                        {
                            int prevLast = _lastRealizedIndex;
                            GeneratorPosition startPos = generator.GeneratorPositionFromIndex(prevLast + 1);
                            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
                            {
                                for (int i = prevLast + 1; i <= reqLast; i++)
                                {
                                    var container = generator.GenerateNext(out bool newlyRealized) as UIElement;
                                    if (container == null) break;
                                    AddInternalChild(container);
                                    generator.PrepareItemContainer(container);
                                    container.Measure(childConstraint);
                                    if (_rowHeight <= 0 && container.DesiredSize.Height > 0)
                                    {
                                        _rowHeight = container.DesiredSize.Height;
                                    }
                                    Interlocked.Increment(ref PanelMetrics.ContainersCreated);
                                }
                            }
                            _lastRealizedIndex = reqLast;
                        }

                        // 5. Measure surviving overlapping containers (no-op if unchanged)
                        int currentCount = InternalChildren.Count;
                        Interlocked.Add(ref PanelMetrics.ContainersReused, currentCount);
                        for (int i = 0; i < currentCount; i++)
                        {
                            UIElement child = InternalChildren[i];
                            child.Measure(childConstraint);
                            if (_rowHeight <= 0 && child.DesiredSize.Height > 0)
                            {
                                _rowHeight = child.DesiredSize.Height;
                            }
                        }
                    }
                    catch
                    {
                        // Resilient fallback on any generator drift
                        canIncrement = false;
                    }
                }

                if (!canIncrement)
                {
                    // Clean realization for non-overlapping jumps, tab switches, and resets
                    ResetAllContainers();

                    if (reqCount > 0)
                    {
                        GeneratorPosition startPos = generator.GeneratorPositionFromIndex(reqFirst);
                        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
                        {
                            for (int i = reqFirst; i <= reqLast; i++)
                            {
                                var container = generator.GenerateNext(out bool newlyRealized) as UIElement;
                                if (container == null) break;

                                AddInternalChild(container);
                                generator.PrepareItemContainer(container);

                                container.Measure(childConstraint);
                                if (_rowHeight <= 0 && container.DesiredSize.Height > 0)
                                {
                                    _rowHeight = container.DesiredSize.Height;
                                }
                                Interlocked.Increment(ref PanelMetrics.ContainersCreated);
                            }
                        }
                        _firstRealizedIndex = reqFirst;
                        _lastRealizedIndex = reqLast;
                    }
                }

                // Total extent height: communicates full library height to parent ScrollViewer
                double finalRowHeight = _rowHeight > 0 ? _rowHeight : effectiveRowHeight;
                double finalStride = finalRowHeight + Math.Max(0, Gap);
                double totalExtentHeight = totalRows > 0 ? (totalRows - 1) * finalStride + finalRowHeight : 0;

                return new Size(availableSize.Width, totalExtentHeight);
            }
            finally
            {
                long elapsed = Stopwatch.GetTimestamp() - t0;
                double ms = elapsed * 1000.0 / Stopwatch.Frequency;
                PanelMetrics.LastMeasureDurationMs = ms;
                Interlocked.Increment(ref PanelMetrics.MeasureCount);
                Interlocked.Add(ref PanelMetrics.MeasureTotalMs, (long)ms);
            }
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            long t0 = Stopwatch.GetTimestamp();
            try
            {
                Solve(finalSize.Width);
                var owner = Owner;
                if (owner == null) return finalSize;

                double slot = _slot;
                double rowHeight = _rowHeight > 0 ? _rowHeight : (_slot > 0 ? _slot + 44 : 200);
                int cols = _cols > 0 ? _cols : 1;
                double stride = rowHeight + Math.Max(0, Gap);
                double gapH = _gapH;

                int count = InternalChildren.Count;
                int baseIndex = _firstRealizedIndex >= 0 ? _firstRealizedIndex : 0;

                for (int i = 0; i < count; i++)
                {
                    UIElement child = InternalChildren[i];
                    int itemIndex = baseIndex + i;

                    int realIndex = owner.ItemContainerGenerator.IndexFromContainer(child);
                    if (realIndex >= 0) itemIndex = realIndex;

                    int row = itemIndex / cols;
                    int col = itemIndex % cols;
                    double x = col * (slot + gapH);
                    double y = row * stride;

                    child.Arrange(new Rect(x, y, slot, rowHeight));
                }

                return finalSize;
            }
            finally
            {
                long elapsed = Stopwatch.GetTimestamp() - t0;
                double ms = elapsed * 1000.0 / Stopwatch.Frequency;
                PanelMetrics.LastArrangeDurationMs = ms;
                Interlocked.Increment(ref PanelMetrics.ArrangeCount);
                Interlocked.Add(ref PanelMetrics.ArrangeTotalMs, (long)ms);
            }
        }

        private void ResetAllContainers()
        {
            try
            {
                IItemContainerGenerator generator = ItemContainerGenerator;
                if (generator != null && InternalChildren.Count > 0)
                {
                    generator.RemoveAll();
                }
            }
            catch { }
            RemoveInternalChildRange(0, InternalChildren.Count);
            _firstRealizedIndex = -1;
            _lastRealizedIndex = -1;
            _currentFirstRow = -1;
            _currentLastRow = -1;
            Interlocked.Increment(ref PanelMetrics.FullResetCount);
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            base.OnItemsChanged(sender, args);
            _rowHeight = 0;
            ResetAllContainers();
            InvalidateMeasure();
        }

        // ── IScrollInfo Implementation ──────────────────────────────────────────
        public void SetVerticalOffset(double offset)
        {
            if (_parentScrollViewer != null)
                _parentScrollViewer.ScrollToVerticalOffset(offset);
        }

        public void SetHorizontalOffset(double offset) { }
        public void LineUp() => SetVerticalOffset(VerticalOffset - 32);
        public void LineDown() => SetVerticalOffset(VerticalOffset + 32);
        public void LineLeft() { }
        public void LineRight() { }
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 96);
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 96);
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            if (visual == null) return Rect.Empty;
            var container = FindContainerForElement(visual);
            int index = (container != null && Owner != null ? Owner.ItemContainerGenerator.IndexFromContainer(container) : -1);
            if (index < 0) return Rect.Empty;
            int cols = _cols > 0 ? _cols : 1;
            int row = index / cols;
            double rowHeight = IsFinitePositive(_rowHeight) ? _rowHeight : RowHeightEstimate;
            double stride = rowHeight + GetSafeGap(Gap);
            double itemTop = row * stride;
            double itemBottom = itemTop + rowHeight;
            if (itemTop < VerticalOffset)
                SetVerticalOffset(itemTop);
            else if (itemBottom > VerticalOffset + ViewportHeight)
                SetVerticalOffset(itemBottom - ViewportHeight);
            return new Rect(0, itemTop - VerticalOffset, ViewportWidth, itemBottom - itemTop);
        }

        private FrameworkElement? FindContainerForElement(Visual visual)
        {
            var owner = Owner;
            if (owner == null)
                return base.MeasureOverride(availableSize);

            EnsureFallbackScrollViewer();
            double width = ResolveWidth(availableSize.Width);
            double viewportHeight = ResolveViewportHeight(
                availableSize.Height,
                (_scrollOwner ?? _fallbackScrollViewer)?.ActualHeight ?? 0,
                (_scrollOwner ?? _fallbackScrollViewer) is ScrollViewer scrollViewer
                    ? scrollViewer.Padding.Top + scrollViewer.Padding.Bottom
                    : 0);
            Solve(width);
            double slot = _slot;
            double rowHeight = IsFinitePositive(_rowHeight) ? _rowHeight : RowHeightEstimate;
            int cols = _cols;
            int itemCount = owner.Items.Count;

            double generationOffset = ResolveGenerationOffset(
                _scrollOwner != null,
                _offset.Y,
                _fallbackScrollViewer?.VerticalOffset ?? 0);
            VisibleRange visibleRange = CalculateVisibleRange(
                itemCount, cols, generationOffset, viewportHeight, rowHeight, Gap, CacheRows);
            if (_scrollOwner != null)
                _offset.Y = visibleRange.Offset;
            int firstIndex = visibleRange.FirstIndex;
            int lastIndex = visibleRange.LastIndex;

            IItemContainerGenerator? generator = ItemContainerGenerator;
            if (generator == null)
            {
                // The panel can be measured once while its ItemsPresenter is being
                // re-attached (for example when the songs grid is switched back on
                // after being collapsed). There is no generator to drive yet, so
                // defer one layout pass instead of dereferencing null.
                if (!_generatorMissingLogged && Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1")
                {
                    _generatorMissingLogged = true;
                    MusicPlayer.Utilities.LogHelper.LogInfo("VirtualizingBalancedPanel: generator not attached yet; deferring measure");
                }
                if (!_measureDeferred)
                {
                    _measureDeferred = true;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,
                        new Action(() =>
                        {
                            _measureDeferred = false;
                            InvalidateMeasure();
                        }));
                }
                return new Size(width, Math.Max(0, _extent.Height));
            }

            // Generate containers for visible range
            if (itemCount > 0 && lastIndex >= firstIndex)
            {
                GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstIndex);
                // WPF GeneratorPosition semantics (mirrors VirtualizingStackPanel.IndexToGeneratorPositionForStart):
                // Offset == 0 means item firstIndex is already realized at realized-ordinal startPos.Index.
                // Offset != 0 means firstIndex is not realized; Index is the realized item (or fictitious
                // start = -1) preceding it, so its child ordinal is Index + 1 (0 when Index == -1).
                int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;
                using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
                {
                    for (int i = firstIndex; i <= lastIndex; i++)
                    {
                        bool newlyRealized;
                        var container = generator.GenerateNext(out newlyRealized) as UIElement;
                        if (container == null) continue;
                        if (newlyRealized)
                        {
                            if (childIndex >= InternalChildren.Count)
                                AddInternalChild(container);
                            else
                                InsertInternalChild(childIndex, container);
                            generator.PrepareItemContainer(container);
                        }
                        container.Measure(new Size(slot, double.PositiveInfinity));
                        var h = container.DesiredSize.Height;
                        if (IsFinitePositive(h) && h > _rowHeight) _rowHeight = h;
                        childIndex++;
                    }
                }
            }
            DependencyObject? current = visual;
            while (current != null && !ReferenceEquals(current, this))
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (ReferenceEquals(parent, this) && current is FrameworkElement fe)
                    return fe;
                current = parent;
            }

            // Recompute extent with updated row height. Solve() is width/keyed, so
            // row height changes never alter the column layout and must not rerun it.
            rowHeight = IsFinitePositive(_rowHeight) ? _rowHeight : RowHeightEstimate;
            var newExtent = new Size(width, GetExtentHeight(itemCount, cols, rowHeight, Gap));
            var newViewport = new Size(width, viewportHeight);
            if (_scrollOwner != null)
            {
                double clampedOffset = ClampOffset(_offset.Y, newExtent.Height, newViewport.Height);
                if (Math.Abs(clampedOffset - _offset.Y) > 0.001)
                {
                    _offset.Y = clampedOffset;
                    InvalidateMeasure();
                }
            }

            bool scrollInfoChanged = !SizesClose(_extent, newExtent)
                || !SizesClose(_viewport, newViewport)
                || !ValuesClose(_offset.Y, _lastInfoOffset);
            _extent = newExtent;
            _viewport = newViewport;
            if (scrollInfoChanged)
            {
                _lastInfoOffset = _offset.Y;
                _scrollOwner?.InvalidateScrollInfo();
            }

            return new Size(width, _extent.Height);
        }

        private void EnsureFallbackScrollViewer()
        {
            ScrollViewer? fallback = _scrollOwner == null ? FindScrollOwner() : null;
            if (ReferenceEquals(fallback, _fallbackScrollViewer)) return;

            if (_fallbackScrollViewer != null)
                _fallbackScrollViewer.ScrollChanged -= OnFallbackScrollChanged;

            _fallbackScrollViewer = fallback;
            if (_fallbackScrollViewer != null)
                _fallbackScrollViewer.ScrollChanged += OnFallbackScrollChanged;
        }

        private void OnFallbackScrollChanged(object sender, ScrollChangedEventArgs args)
        {
            if (_scrollOwner == null && Math.Abs(args.VerticalChange) > 0.001)
                InvalidateMeasure();
        }

        private ScrollViewer? FindScrollOwner()
        {
            DependencyObject current = this;
            while (current != null)
            {
                if (current is ScrollViewer sv) return sv;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            EnsureFallbackScrollViewer();
            Solve(finalSize.Width);
            var owner = Owner;
            if (owner == null) return finalSize;

            double slot = _slot;
            double rowHeight = IsFinitePositive(_rowHeight) ? _rowHeight : RowHeightEstimate;
            int cols = _cols;
            IItemContainerGenerator generator = ItemContainerGenerator;

            foreach (UIElement child in InternalChildren)
            {
                int index = (Owner != null ? Owner.ItemContainerGenerator.IndexFromContainer(child) : -1);
                if (index < 0) continue;
                int row = index / cols;
                int col = index % cols;
                double x = col * (_slot + _gapH);
                double offset = _scrollOwner == null ? 0 : _offset.Y;
                double y = row * (rowHeight + GetSafeGap(Gap)) - offset;
                child.Arrange(new Rect(x, y, slot, rowHeight));
            }

            double viewportWidth = ResolveWidth(finalSize.Width);
            double viewportHeight = ResolveViewportHeight(
                finalSize.Height,
                (_scrollOwner ?? _fallbackScrollViewer)?.ActualHeight ?? 0,
                (_scrollOwner ?? _fallbackScrollViewer) is ScrollViewer scrollViewer
                    ? scrollViewer.Padding.Top + scrollViewer.Padding.Bottom
                    : 0);
            _viewport = new Size(viewportWidth, viewportHeight);
            return finalSize;
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            base.OnItemsChanged(sender, args);
            if (args.Action is NotifyCollectionChangedAction.Remove
                or NotifyCollectionChangedAction.Replace
                or NotifyCollectionChangedAction.Reset)
                _rowHeight = 0;
            InvalidateMeasure();
        }
    }
}
