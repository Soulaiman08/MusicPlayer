using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Threading;
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
        public static readonly DependencyProperty MinItemWidthProperty =
            DependencyProperty.Register("MinItemWidth", typeof(double), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(140d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty MaxItemWidthProperty =
            DependencyProperty.Register("MaxItemWidth", typeof(double), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(180d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty GapProperty =
            DependencyProperty.Register("Gap", typeof(double), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        // Intentionally WITHOUT AffectsMeasure/AffectsArrange to prevent recursive layout cycles during MeasureOverride/ArrangeOverride
        private static readonly DependencyPropertyKey ItemWidthPropertyKey =
            DependencyProperty.RegisterReadOnly("ItemWidth", typeof(double), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(150d));

        public static readonly DependencyProperty ItemWidthProperty = ItemWidthPropertyKey.DependencyProperty;

        public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
        public double MaxItemWidth { get => (double)GetValue(MaxItemWidthProperty); set => SetValue(MaxItemWidthProperty, value); }
        public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
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

        private ScrollViewer? _parentScrollViewer;
        private ScrollViewer? _scrollOwner;

        public bool CanHorizontallyScroll { get; set; } = false;
        public bool CanVerticallyScroll { get; set; } = true;
        public double ExtentWidth => _lastMeasureWidth;
        public double ExtentHeight => ComputeTotalExtent();
        public double ViewportWidth => _lastMeasureWidth;
        public double ViewportHeight => _parentScrollViewer?.ViewportHeight ?? 800;
        public double HorizontalOffset => 0;
        public double VerticalOffset => _parentScrollViewer?.VerticalOffset ?? 0;

        public ScrollViewer? ScrollOwner
        {
            get => _scrollOwner;
            set => _scrollOwner = value;
        }

        private ItemsControl? Owner => ItemsControl.GetItemsOwner(this);

        public VirtualizingBalancedPanel()
        {
            Loaded += (_, _) => HookScrollViewer();
            Unloaded += (_, _) => UnhookScrollViewer();
        }

        protected override void OnVisualParentChanged(DependencyObject oldParent)
        {
            base.OnVisualParentChanged(oldParent);
            HookScrollViewer();
        }

        private void HookScrollViewer()
        {
            var sv = FindAncestorScrollViewer(this);
            if (ReferenceEquals(_parentScrollViewer, sv)) return;

            UnhookScrollViewer();
            _parentScrollViewer = sv;
            if (_parentScrollViewer != null)
            {
                _parentScrollViewer.ScrollChanged += OnParentScrollChanged;
            }
        }

        private void UnhookScrollViewer()
        {
            if (_parentScrollViewer != null)
            {
                _parentScrollViewer.ScrollChanged -= OnParentScrollChanged;
                _parentScrollViewer = null;
            }
        }

        private void OnParentScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (_parentScrollViewer == null) return;

            // Only invalidate measure when the visible/buffer row window actually changes.
            // When scrolling within the buffer row boundary, sub-pixel compositor scrolling handles
            // translation smoothly at native monitor refresh rates with zero UI thread work.
            double effectiveRowHeight = _rowHeight > 0 ? _rowHeight : (_slot > 0 ? _slot + 44 : 200);
            double stride = effectiveRowHeight + Math.Max(0, Gap);
            if (stride <= 0) stride = 200;

            double offsetY = Math.Max(0, _parentScrollViewer.VerticalOffset);
            double viewportH = _parentScrollViewer.ViewportHeight > 0
                ? _parentScrollViewer.ViewportHeight
                : (_parentScrollViewer.ActualHeight > 0 ? _parentScrollViewer.ActualHeight : 800);

            int topRow = (int)Math.Floor(offsetY / stride);
            int visibleRowCount = (int)Math.Ceiling(Math.Max(1, viewportH) / stride);
            int firstRow = Math.Max(0, topRow - 1);
            int lastRow = topRow + visibleRowCount + 2;

            if (firstRow != _currentFirstRow || lastRow != _currentLastRow || Math.Abs(e.ViewportHeightChange) > 1.0)
            {
                InvalidateMeasure();
            }
        }

        private static ScrollViewer? FindAncestorScrollViewer(DependencyObject? current)
        {
            while (current != null)
            {
                if (current is ScrollViewer sv) return sv;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void Solve(double width)
        {
            var w = width > 0 && !double.IsNaN(width) && !double.IsInfinity(width) ? width : Math.Max(1, MinItemWidth);
            var gap = Math.Max(0, Gap);
            var min = Math.Clamp(MinItemWidth, 1d, w);
            var max = Math.Max(min, MaxItemWidth);
            var cols = (int)Math.Floor((w + gap) / (min + gap));
            if (cols < 1) cols = 1;
            var slot = Math.Min(max, (w - gap * (cols - 1)) / cols);
            var used = slot * cols + gap * (cols - 1);
            _slot = Math.Max(1, slot);
            _gapH = gap + (cols > 1 ? Math.Max(0, w - used) / (cols - 1) : 0);
            _cols = Math.Max(1, cols);

            if (Math.Abs(ItemWidth - _slot) > 0.5)
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
            double rowHeight = _rowHeight > 0 ? _rowHeight : (_slot + 44);
            double stride = rowHeight + Math.Max(0, Gap);
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
            if (visual == null) return null;
            if (Owner != null)
            {
                var c = Owner.ContainerFromElement(visual) as FrameworkElement;
                if (c != null) return c;
            }
            DependencyObject? current = visual;
            while (current != null && !ReferenceEquals(current, this))
            {
                var parent = VisualTreeHelper.GetParent(current);
                if (ReferenceEquals(parent, this) && current is FrameworkElement fe)
                    return fe;
                current = parent;
            }
            return visual as FrameworkElement;
        }
    }
}
