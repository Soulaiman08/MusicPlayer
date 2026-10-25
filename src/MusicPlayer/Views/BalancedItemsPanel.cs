using System;
using System.Windows;
using System.Windows.Controls;

namespace MusicPlayer.Views
{
    public class BalancedItemsPanel : Panel
    {
        public static readonly DependencyProperty MinItemWidthProperty =
            DependencyProperty.Register("MinItemWidth", typeof(double), typeof(BalancedItemsPanel),
                new FrameworkPropertyMetadata(140d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty MaxItemWidthProperty =
            DependencyProperty.Register("MaxItemWidth", typeof(double), typeof(BalancedItemsPanel),
                new FrameworkPropertyMetadata(180d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty GapProperty =
            DependencyProperty.Register("Gap", typeof(double), typeof(BalancedItemsPanel),
                new FrameworkPropertyMetadata(8d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        private static readonly DependencyPropertyKey ItemWidthPropertyKey =
            DependencyProperty.RegisterReadOnly("ItemWidth", typeof(double), typeof(BalancedItemsPanel),
                new FrameworkPropertyMetadata(150d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsArrange));

        public static readonly DependencyProperty ItemWidthProperty = ItemWidthPropertyKey.DependencyProperty;

        public double MinItemWidth
        {
            get => (double)GetValue(MinItemWidthProperty);
            set => SetValue(MinItemWidthProperty, value);
        }

        public double MaxItemWidth
        {
            get => (double)GetValue(MaxItemWidthProperty);
            set => SetValue(MaxItemWidthProperty, value);
        }

        public double Gap
        {
            get => (double)GetValue(GapProperty);
            set => SetValue(GapProperty, value);
        }

        public double ItemWidth => (double)GetValue(ItemWidthProperty);

        private double _slot;
        private double _gapH;
        private double _rowHeight;

        private void Solve(double width)
        {
            var w = width > 0 && !double.IsNaN(width) && !double.IsInfinity(width) ? width : Math.Max(1, MinItemWidth);
            var gap = Math.Max(0, Gap);
            var min = Math.Clamp(MinItemWidth, 1d, w);
            var max = Math.Max(min, MaxItemWidth);
            var cols = (int)Math.Floor((w + gap) / (min + gap));
            var slot = Math.Min(max, (w - gap * (cols - 1)) / cols);
            var used = slot * cols + gap * (cols - 1);
            _slot = Math.Max(1, slot);
            _gapH = gap + (cols > 1 ? Math.Max(0, w - used) / (cols - 1) : 0);
            _cols = Math.Max(1, cols);
        }

        private int _cols;

        protected override Size MeasureOverride(Size availableSize)
        {
            Solve(availableSize.Width);
            if (Math.Abs(ItemWidth - _slot) > 0.5)
                SetValue(ItemWidthPropertyKey, _slot);
            _rowHeight = 0;
            var childConstraint = new Size(_slot, double.PositiveInfinity);
            foreach (UIElement child in Children)
            {
                child.Measure(childConstraint);
                var h = child.DesiredSize.Height;
                if (double.IsInfinity(h) || double.IsNaN(h)) h = 0;
                if (h > _rowHeight) _rowHeight = h;
            }
            return new Size(availableSize.Width, ComputeExtent());
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            Solve(finalSize.Width);
            if (Math.Abs(ItemWidth - _slot) > 0.5)
                SetValue(ItemWidthPropertyKey, _slot);

            // Recompute _rowHeight if not yet measured (Arrange can be called before Measure)
            if (_rowHeight <= 0 && Children.Count > 0)
            {
                var childConstraint = new Size(_slot, double.PositiveInfinity);
                foreach (UIElement child in Children)
                {
                    child.Measure(childConstraint);
                    var h = child.DesiredSize.Height;
                    if (double.IsInfinity(h) || double.IsNaN(h)) h = 0;
                    if (h > _rowHeight) _rowHeight = h;
                }
            }

            var rowHeight = Math.Max(0, _rowHeight);
            var y = 0d;
            var count = Children.Count;
            for (var i = 0; i < count; i++)
            {
                var child = Children[i];
                var col = i % _cols;
                var x = col * (_slot + _gapH);
                child.Arrange(new Rect(x, y, _slot, rowHeight > 0 ? rowHeight : Math.Max(0, child.DesiredSize.Height)));
                if (col == _cols - 1) y += rowHeight + Gap;
            }
            return new Size(finalSize.Width, ComputeExtent());
        }

        private double ComputeExtent()
        {
            var rows = _cols > 0 ? (Children.Count + _cols - 1) / _cols : 0;
            return rows > 0 ? (rows - 1) * (_rowHeight + Math.Max(0, Gap)) + _rowHeight : 0;
        }
    }
}
