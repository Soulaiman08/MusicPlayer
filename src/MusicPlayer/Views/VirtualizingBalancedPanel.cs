using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace MusicPlayer.Views
{
    // Real virtualizing grid panel: only materializes containers for items whose
    // rows intersect the viewport (plus a small cache). Reuses containers on scroll.
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

        private static readonly DependencyPropertyKey ItemWidthPropertyKey =
            DependencyProperty.RegisterReadOnly("ItemWidth", typeof(double), typeof(VirtualizingBalancedPanel),
                new FrameworkPropertyMetadata(150d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty ItemWidthProperty = ItemWidthPropertyKey.DependencyProperty;

        public double MinItemWidth { get => (double)GetValue(MinItemWidthProperty); set => SetValue(MinItemWidthProperty, value); }
        public double MaxItemWidth { get => (double)GetValue(MaxItemWidthProperty); set => SetValue(MaxItemWidthProperty, value); }
        public double Gap { get => (double)GetValue(GapProperty); set => SetValue(GapProperty, value); }
        public double ItemWidth => (double)GetValue(ItemWidthProperty);

        private double _slot;
        private double _gapH;
        private int _cols;
        private double _rowHeight = 0;
        private const double RowHeightEstimate = 200;

        private Size _extent = new Size(0, 0);
        private Size _viewport = new Size(0, 0);
        private Point _offset = new Point(0, 0);
        private ScrollViewer? _scrollOwner;

        public bool CanHorizontallyScroll { get; set; } = false;
        public bool CanVerticallyScroll { get; set; } = true;
        public double ExtentWidth => _extent.Width;
        public double ExtentHeight => _extent.Height;
        public double ViewportWidth => _viewport.Width;
        public double ViewportHeight => _viewport.Height;
        public double HorizontalOffset => _offset.X;
        public double VerticalOffset => _offset.Y;

        public ScrollViewer? ScrollOwner
        {
            get => _scrollOwner;
            set => _scrollOwner = value;
        }

        private ItemsControl? Owner => ItemsControl.GetItemsOwner(this);

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
                SetValue(ItemWidthPropertyKey, _slot);
        }

        public void SetVerticalOffset(double offset)
        {
            offset = Math.Max(0, Math.Min(offset, Math.Max(0, ExtentHeight - ViewportHeight)));
            if (Math.Abs(offset - _offset.Y) > 0.001)
            {
                _offset.Y = offset;
                InvalidateMeasure();
                _scrollOwner?.InvalidateScrollInfo();
            }
        }

        public void SetHorizontalOffset(double offset)
        {
            // no horizontal scrolling
        }

        public void LineUp() => SetVerticalOffset(VerticalOffset - 16);
        public void LineDown() => SetVerticalOffset(VerticalOffset + 16);
        public void LineLeft() { }
        public void LineRight() { }
        public void PageUp() => SetVerticalOffset(VerticalOffset - ViewportHeight);
        public void PageDown() => SetVerticalOffset(VerticalOffset + ViewportHeight);
        public void PageLeft() { }
        public void PageRight() { }
        public void MouseWheelUp() => SetVerticalOffset(VerticalOffset - 48);
        public void MouseWheelDown() => SetVerticalOffset(VerticalOffset + 48);
        public void MouseWheelLeft() { }
        public void MouseWheelRight() { }

        public Rect MakeVisible(Visual visual, Rect rectangle)
        {
            if (Owner == null) return Rect.Empty;
            var container = visual as FrameworkElement;
            int index = (Owner != null ? Owner.ItemContainerGenerator.IndexFromContainer(container) : -1);
            if (index < 0) return Rect.Empty;
            int cols = _cols > 0 ? _cols : 1;
            int row = index / cols;
            double stride = (_rowHeight > 0 ? _rowHeight : RowHeightEstimate) + Math.Max(0, Gap);
            double itemTop = row * stride;
            double itemBottom = itemTop + (_rowHeight > 0 ? _rowHeight : RowHeightEstimate);
            if (itemTop < VerticalOffset)
                SetVerticalOffset(itemTop);
            else if (itemBottom > VerticalOffset + ViewportHeight)
                SetVerticalOffset(itemBottom - ViewportHeight);
            return new Rect(0, itemTop - VerticalOffset, ViewportWidth, itemBottom - itemTop);
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            var owner = Owner;
            if (owner == null)
                return base.MeasureOverride(availableSize);

            Solve(availableSize.Width);
            double slot = _slot;
            double rowHeight = _rowHeight > 0 ? _rowHeight : RowHeightEstimate;
            double stride = rowHeight + Math.Max(0, Gap);
            int cols = _cols;
            int itemCount = owner.Items.Count;

            // visible row range with cache
            int firstRow = itemCount > 0 ? (int)Math.Floor(VerticalOffset / stride) - 2 : 0;
            if (firstRow < 0) firstRow = 0;
            int visibleRows = itemCount > 0 ? (int)Math.Ceiling(Math.Max(1, ViewportHeight) / stride) + 4 : 0;
            int lastRow = firstRow + (visibleRows > 0 ? visibleRows : 1);
            int firstIndex = firstRow * cols;
            int lastIndex = itemCount > 0 ? Math.Min(itemCount - 1, (lastRow + 1) * cols - 1) : -1;
            if (firstIndex < 0) firstIndex = 0;
            if (lastIndex < firstIndex) lastIndex = firstIndex - 1;

            IItemContainerGenerator generator = ItemContainerGenerator;

            // Generate containers for visible range
            if (itemCount > 0 && lastIndex >= firstIndex)
            {
                GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstIndex);
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
                        if (h > _rowHeight && h > 0) _rowHeight = h;
                        childIndex++;
                    }
                }
            }

            // Cleanup containers outside range
            for (int i = InternalChildren.Count - 1; i >= 0; i--)
            {
                var container = InternalChildren[i];
                int index = (Owner != null ? Owner.ItemContainerGenerator.IndexFromContainer(container) : -1);
                if (index < firstIndex || index > lastIndex)
                {
                    try
                    {
                        var genPos = generator.GeneratorPositionFromIndex(index);
                        bool recycling = Owner != null &&
                            VirtualizingPanel.GetVirtualizationMode(Owner) == VirtualizationMode.Recycling &&
                            generator is IRecyclingItemContainerGenerator;
                        if (recycling)
                        {
                            ((IRecyclingItemContainerGenerator)generator).Recycle(genPos, 1);
                        }
                        else
                        {
                            generator.Remove(genPos, 1);
                        }
                        RemoveInternalChildRange(i, 1);
                    }
                    catch { }
                }
            }

            // Recompute extent with updated row height
            Solve(availableSize.Width);
            rowHeight = _rowHeight > 0 ? _rowHeight : RowHeightEstimate;
            stride = rowHeight + Math.Max(0, Gap);
            int totalRows = cols > 0 ? (itemCount + cols - 1) / cols : 0;
            _extent = new Size(availableSize.Width,
                totalRows > 0 ? (totalRows - 1) * stride + rowHeight : 0);
            _viewport = availableSize;
            if (_offset.Y > Math.Max(0, ExtentHeight - ViewportHeight))
                _offset.Y = Math.Max(0, ExtentHeight - ViewportHeight);

            _scrollOwner?.InvalidateScrollInfo();
            if (ScrollOwner == null) _scrollOwner = FindScrollOwner();
            _scrollOwner?.InvalidateScrollInfo();

            return new Size(availableSize.Width, _extent.Height);
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
            Solve(finalSize.Width);
            var owner = Owner;
            if (owner == null) return finalSize;

            double slot = _slot;
            double rowHeight = _rowHeight > 0 ? _rowHeight : RowHeightEstimate;
            int cols = _cols;
            IItemContainerGenerator generator = ItemContainerGenerator;

            foreach (UIElement child in InternalChildren)
            {
                int index = (Owner != null ? Owner.ItemContainerGenerator.IndexFromContainer(child) : -1);
                if (index < 0) continue;
                int row = index / cols;
                int col = index % cols;
                double x = col * (_slot + _gapH);
                double y = row * (rowHeight + Math.Max(0, Gap)) - VerticalOffset;
                child.Arrange(new Rect(x, y, slot, rowHeight));
            }

            _viewport = finalSize;
            return finalSize;
        }

        protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
        {
            base.OnItemsChanged(sender, args);
            _rowHeight = 0;
            InvalidateMeasure();
        }
    }
}
