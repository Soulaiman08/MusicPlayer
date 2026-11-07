using System;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MusicPlayer.Views
{
    /// <summary>
    /// High-performance virtualizing balanced grid panel: only materializes containers
    /// for items whose rows intersect the viewport (+ buffer rows).
    /// Observes parent ScrollViewer scroll offsets, provides exact template-measured row heights
    /// (eliminating gap accumulation), and preserves complete DataContext/bindings across scrolling.
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
            if (Math.Abs(e.VerticalChange) > 0.01 || Math.Abs(e.ViewportHeightChange) > 0.01)
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

            // 1. If row height is not established, probe item 0 to measure exact template DesiredSize
            if (_rowHeight <= 0 && itemCount > 0)
            {
                GeneratorPosition p0 = generator.GeneratorPositionFromIndex(0);
                using (generator.StartAt(p0, GeneratorDirection.Forward, true))
                {
                    var probe = generator.GenerateNext(out bool _) as UIElement;
                    if (probe != null)
                    {
                        generator.PrepareItemContainer(probe);
                        probe.Measure(new Size(_slot, double.PositiveInfinity));
                        if (probe.DesiredSize.Height > 0)
                        {
                            _rowHeight = probe.DesiredSize.Height;
                        }
                    }
                }
                ResetAllContainers();
            }

            double effectiveRowHeight = _rowHeight > 0 ? _rowHeight : (_slot > 0 ? _slot + 44 : 200);
            double stride = effectiveRowHeight + Math.Max(0, Gap);
            int cols = _cols > 0 ? _cols : 1;
            int totalRows = (itemCount + cols - 1) / cols;

            // 2. Resolve scroll offset and viewport height from parent ScrollViewer
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

            // 3. Compute active row range with buffer rows (1 row above, 2 rows below)
            int topRow = (int)Math.Floor(offsetY / stride);
            int firstRow = Math.Max(0, topRow - 1);
            int visibleRowCount = (int)Math.Ceiling(Math.Max(1, viewportH) / stride);
            int lastRow = Math.Min(totalRows - 1, topRow + visibleRowCount + 2);

            int reqFirst = firstRow * cols;
            int reqLast = Math.Min(itemCount - 1, (lastRow + 1) * cols - 1);
            if (reqFirst < 0) reqFirst = 0;
            if (reqLast < reqFirst) reqLast = reqFirst - 1;

            int reqCount = reqLast >= reqFirst ? (reqLast - reqFirst + 1) : 0;

            // 4. Clean range materialization: ensures zero broken bindings or missing containers
            if (_firstRealizedIndex != reqFirst || _lastRealizedIndex != reqLast || InternalChildren.Count != reqCount)
            {
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

                            container.Measure(new Size(_slot, double.PositiveInfinity));
                            if (container.DesiredSize.Height > _rowHeight)
                            {
                                _rowHeight = container.DesiredSize.Height;
                            }
                        }
                    }
                    _firstRealizedIndex = reqFirst;
                    _lastRealizedIndex = reqLast;
                }
            }
            else
            {
                // Measure existing realized containers
                for (int i = 0; i < InternalChildren.Count; i++)
                {
                    UIElement child = InternalChildren[i];
                    child.Measure(new Size(_slot, double.PositiveInfinity));
                    if (child.DesiredSize.Height > _rowHeight)
                    {
                        _rowHeight = child.DesiredSize.Height;
                    }
                }
            }

            // 5. Total extent height: communicates full library height to parent ScrollViewer
            double finalRowHeight = _rowHeight > 0 ? _rowHeight : effectiveRowHeight;
            double finalStride = finalRowHeight + Math.Max(0, Gap);
            double totalExtentHeight = totalRows > 0 ? (totalRows - 1) * finalStride + finalRowHeight : 0;

            return new Size(availableSize.Width, totalExtentHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Solve(finalSize.Width);
            var owner = Owner;
            if (owner == null) return finalSize;

            double slot = _slot;
            double rowHeight = _rowHeight > 0 ? _rowHeight : (slot > 0 ? slot + 44 : 200);
            int cols = _cols > 0 ? _cols : 1;
            double stride = rowHeight + Math.Max(0, Gap);
            double gapH = _gapH;

            int count = InternalChildren.Count;
            int baseIndex = _firstRealizedIndex >= 0 ? _firstRealizedIndex : 0;

            for (int i = 0; i < count; i++)
            {
                UIElement child = InternalChildren[i];
                int itemIndex = baseIndex + i;
                int row = itemIndex / cols;
                int col = itemIndex % cols;
                double x = col * (slot + gapH);
                double y = row * stride;

                child.Arrange(new Rect(x, y, slot, rowHeight));
            }

            return finalSize;
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
