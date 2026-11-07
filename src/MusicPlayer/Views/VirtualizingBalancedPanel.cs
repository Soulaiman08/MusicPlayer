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
        private double _lastMeasureWidth;
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
            double maxOffset = Math.Max(0, ExtentHeight - ViewportHeight);
            offset = Math.Max(0, Math.Min(offset, maxOffset));
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
            double slot = _slot > 0 ? _slot : RowHeightEstimate;
            double rowHeight = _rowHeight > 0 ? _rowHeight : (slot + 44);
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

        protected override Size MeasureOverride(Size availableSize)
        {
            var owner = Owner;
            if (owner == null)
                return base.MeasureOverride(availableSize);

            if (_scrollOwner == null)
                _scrollOwner = FindScrollOwner();

            int prevCols = _cols;
            double prevWidth = _lastMeasureWidth;
            Solve(availableSize.Width);
            if (prevCols != _cols || Math.Abs(prevWidth - availableSize.Width) > 0.5)
                _rowHeight = 0;
            _lastMeasureWidth = availableSize.Width;

            double slot = _slot;
            double estimatedRowHeight = slot > 0 ? slot + 44 : RowHeightEstimate;
            double rowHeight = _rowHeight > 0 ? _rowHeight : estimatedRowHeight;
            double stride = rowHeight + Math.Max(0, Gap);
            int cols = _cols > 0 ? _cols : 1;
            int itemCount = owner.Items.Count;
            int totalRows = cols > 0 ? (itemCount + cols - 1) / cols : 0;

            double viewportHeight = availableSize.Height;
            if (double.IsInfinity(viewportHeight) || double.IsNaN(viewportHeight) || viewportHeight <= 0)
            {
                if (_scrollOwner != null && _scrollOwner.ViewportHeight > 0)
                    viewportHeight = _scrollOwner.ViewportHeight;
                else if (_scrollOwner != null && _scrollOwner.ActualHeight > 0)
                    viewportHeight = _scrollOwner.ActualHeight;
                else if (_viewport.Height > 0 && _viewport.Height < 10000)
                    viewportHeight = _viewport.Height;
                else
                    viewportHeight = estimatedRowHeight * 4;
            }

            double initialExtentHeight = totalRows > 0 ? (totalRows - 1) * stride + rowHeight : 0;
            double maxOffset = Math.Max(0, initialExtentHeight - viewportHeight);
            if (_offset.Y > maxOffset) _offset.Y = maxOffset;
            if (_offset.Y < 0) _offset.Y = 0;

            // visible row range with balanced cache
            int firstRow = itemCount > 0 ? (int)Math.Floor(_offset.Y / stride) - 2 : 0;
            if (firstRow < 0) firstRow = 0;
            int visibleRows = itemCount > 0 ? (int)Math.Ceiling(Math.Max(1, viewportHeight) / stride) + 4 : 0;
            int lastRow = firstRow + (visibleRows > 0 ? visibleRows : 1);
            int firstIndex = firstRow * cols;
            int lastIndex = itemCount > 0 ? Math.Min(itemCount - 1, (lastRow + 1) * cols - 1) : -1;
            if (firstIndex < 0) firstIndex = 0;
            if (lastIndex < firstIndex) lastIndex = firstIndex - 1;

            // 1. Cleanup out-of-range containers FIRST
            if (InternalChildren.Count > 0 && Owner != null)
            {
                // Clean up any individual disconnected containers (itemIndex < 0)
                for (int i = InternalChildren.Count - 1; i >= 0; i--)
                {
                    int idx = Owner.ItemContainerGenerator.IndexFromContainer(InternalChildren[i]);
                    if (idx < 0)
                    {
                        SafeRemoveOrRecycleContainer(i);
                    }
                }

                if (InternalChildren.Count > 0)
                {
                    int firstRealized = Owner.ItemContainerGenerator.IndexFromContainer(InternalChildren[0]);
                    int lastRealized = Owner.ItemContainerGenerator.IndexFromContainer(InternalChildren[InternalChildren.Count - 1]);

                    // If completely disjoint from the new range, clean all from back to front
                    if (itemCount == 0 || lastIndex < firstRealized || firstIndex > lastRealized)
                    {
                        for (int i = InternalChildren.Count - 1; i >= 0; i--)
                        {
                            if (!SafeRemoveOrRecycleContainer(i)) break;
                        }
                    }
                    else
                    {
                        // Remove trailing out-of-range containers (from the end)
                        for (int i = InternalChildren.Count - 1; i >= 0; i--)
                        {
                            int idx = Owner.ItemContainerGenerator.IndexFromContainer(InternalChildren[i]);
                            if (idx > lastIndex)
                            {
                                if (!SafeRemoveOrRecycleContainer(i)) break;
                            }
                            else
                            {
                                break;
                            }
                        }

                        // Remove leading out-of-range containers (from the start)
                        while (InternalChildren.Count > 0)
                        {
                            int idx = Owner.ItemContainerGenerator.IndexFromContainer(InternalChildren[0]);
                            if (idx >= 0 && idx < firstIndex)
                            {
                                if (!SafeRemoveOrRecycleContainer(0)) break;
                            }
                            else
                            {
                                break;
                            }
                        }
                    }
                }
            }

            // 2. Generate missing containers for [firstIndex, lastIndex]
            if (itemCount > 0 && lastIndex >= firstIndex && Owner != null)
            {
                IItemContainerGenerator generator = ItemContainerGenerator;
                if (InternalChildren.Count == 0)
                {
                    GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstIndex);
                    int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;
                    if (childIndex < 0) childIndex = 0;

                    using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
                    {
                        for (int i = firstIndex; i <= lastIndex; i++)
                        {
                            bool newlyRealized;
                            var container = generator.GenerateNext(out newlyRealized) as UIElement;
                            if (container == null) break;

                            if (newlyRealized || !InternalChildren.Contains(container))
                            {
                                if (childIndex >= InternalChildren.Count)
                                    AddInternalChild(container);
                                else
                                    InsertInternalChild(childIndex, container);
                                generator.PrepareItemContainer(container);
                            }

                            container.Measure(new Size(slot, double.PositiveInfinity));
                            if (container.DesiredSize.Height > _rowHeight)
                                _rowHeight = container.DesiredSize.Height;

                            childIndex++;
                        }
                    }
                }
                else
                {
                    int rStart = Owner.ItemContainerGenerator.IndexFromContainer(InternalChildren[0]);
                    int rEnd = Owner.ItemContainerGenerator.IndexFromContainer(InternalChildren[InternalChildren.Count - 1]);

                    // Generate any items before rStart (scrolling up)
                    if (firstIndex < rStart)
                    {
                        GeneratorPosition startPos = generator.GeneratorPositionFromIndex(firstIndex);
                        int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;
                        if (childIndex < 0) childIndex = 0;

                        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
                        {
                            for (int i = firstIndex; i < rStart; i++)
                            {
                                bool newlyRealized;
                                var container = generator.GenerateNext(out newlyRealized) as UIElement;
                                if (container == null) break;

                                if (newlyRealized || !InternalChildren.Contains(container))
                                {
                                    if (childIndex >= InternalChildren.Count)
                                        AddInternalChild(container);
                                    else
                                        InsertInternalChild(childIndex, container);
                                    generator.PrepareItemContainer(container);
                                }

                                container.Measure(new Size(slot, double.PositiveInfinity));
                                if (container.DesiredSize.Height > _rowHeight)
                                    _rowHeight = container.DesiredSize.Height;

                                childIndex++;
                            }
                        }
                    }

                    // Generate any items after rEnd (scrolling down)
                    if (lastIndex > rEnd)
                    {
                        GeneratorPosition startPos = generator.GeneratorPositionFromIndex(rEnd + 1);
                        int childIndex = startPos.Offset == 0 ? startPos.Index : startPos.Index + 1;
                        if (childIndex < 0 || childIndex > InternalChildren.Count) childIndex = InternalChildren.Count;

                        using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
                        {
                            for (int i = rEnd + 1; i <= lastIndex; i++)
                            {
                                bool newlyRealized;
                                var container = generator.GenerateNext(out newlyRealized) as UIElement;
                                if (container == null) break;

                                if (newlyRealized || !InternalChildren.Contains(container))
                                {
                                    if (childIndex >= InternalChildren.Count)
                                        AddInternalChild(container);
                                    else
                                        InsertInternalChild(childIndex, container);
                                    generator.PrepareItemContainer(container);
                                }

                                container.Measure(new Size(slot, double.PositiveInfinity));
                                if (container.DesiredSize.Height > _rowHeight)
                                    _rowHeight = container.DesiredSize.Height;

                                childIndex++;
                            }
                        }
                    }
                }
            }

            // 3. Only measure all children if row height is not yet established
            if (_rowHeight <= 0)
            {
                foreach (UIElement child in InternalChildren)
                {
                    child.Measure(new Size(slot, double.PositiveInfinity));
                    var h = child.DesiredSize.Height;
                    if (h > _rowHeight && h > 0)
                    {
                        _rowHeight = h;
                    }
                }
            }

            // Recompute extent with updated row height
            rowHeight = _rowHeight > 0 ? _rowHeight : estimatedRowHeight;
            stride = rowHeight + Math.Max(0, Gap);
            double finalExtentHeight = totalRows > 0 ? (totalRows - 1) * stride + rowHeight : 0;
            _extent = new Size(availableSize.Width, finalExtentHeight);
            _viewport = new Size(availableSize.Width, viewportHeight);

            double finalMaxOffset = Math.Max(0, finalExtentHeight - viewportHeight);
            if (_offset.Y > finalMaxOffset)
                _offset.Y = finalMaxOffset;

            _scrollOwner?.InvalidateScrollInfo();

            double resultHeight = double.IsInfinity(availableSize.Height) ? _extent.Height : Math.Min(availableSize.Height, _extent.Height);
            return new Size(availableSize.Width, resultHeight);
        }

        private bool SafeRemoveOrRecycleContainer(int childIndex)
        {
            if (childIndex < 0 || childIndex >= InternalChildren.Count)
                return false;

            UIElement child = InternalChildren[childIndex];
            IItemContainerGenerator generator = ItemContainerGenerator;
            if (generator == null) return false;

            int itemIndex = (Owner != null) ? Owner.ItemContainerGenerator.IndexFromContainer(child) : -1;
            GeneratorPosition pos;
            if (itemIndex >= 0)
            {
                pos = generator.GeneratorPositionFromIndex(itemIndex);
            }
            else
            {
                pos = new GeneratorPosition(childIndex, 0);
            }

            if (pos.Index < 0 || pos.Offset != 0)
            {
                pos = new GeneratorPosition(childIndex, 0);
            }

            bool recycling = Owner != null &&
                VirtualizingPanel.GetVirtualizationMode(Owner) == VirtualizationMode.Recycling &&
                generator is IRecyclingItemContainerGenerator;

            try
            {
                if (recycling && generator is IRecyclingItemContainerGenerator recGen)
                {
                    recGen.Recycle(pos, 1);
                }
                else
                {
                    generator.Remove(pos, 1);
                }

                // Only remove visually if generator operation succeeded
                RemoveInternalChildRange(childIndex, 1);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"SafeRemoveOrRecycleContainer failed for childIndex {childIndex}, itemIndex {itemIndex}: {ex.Message}");
                return false;
            }
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
            double rowHeight = _rowHeight > 0 ? _rowHeight : (slot > 0 ? slot + 44 : RowHeightEstimate);
            int cols = _cols > 0 ? _cols : 1;
            double stride = rowHeight + Math.Max(0, Gap);

            foreach (UIElement child in InternalChildren)
            {
                int index = (Owner != null ? Owner.ItemContainerGenerator.IndexFromContainer(child) : -1);
                if (index < 0) continue;
                int row = index / cols;
                int col = index % cols;
                double x = col * (_slot + _gapH);
                double y = row * stride - VerticalOffset;
                child.Arrange(new Rect(x, y, slot, rowHeight));
            }

            double vpHeight = (_scrollOwner != null && _scrollOwner.ViewportHeight > 0)
                ? _scrollOwner.ViewportHeight
                : (_viewport.Height > 0 && _viewport.Height < _extent.Height ? _viewport.Height : finalSize.Height);
            _viewport = new Size(finalSize.Width, vpHeight);
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
