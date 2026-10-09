using System;
using System.Windows;
using System.Windows.Controls;

namespace MusicPlayer.Views
{
    /// <summary>
    /// Responsive wrap panel that computes a strictly proportioned 16:9 card size
    /// from its available measured width so that every row fills the width uniformly
    /// with clean, balanced spacing and adaptive column counts.
    /// Small window: 3–4 columns
    /// Medium window: 5–6 columns
    /// Large window: 6–8 columns
    /// </summary>
    public sealed class ThemeWrapPanel : Panel
    {
        public static readonly DependencyProperty MinCardWidthProperty =
            DependencyProperty.Register(nameof(MinCardWidth), typeof(double), typeof(ThemeWrapPanel),
                new FrameworkPropertyMetadata(200.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty MaxCardWidthProperty =
            DependencyProperty.Register(nameof(MaxCardWidth), typeof(double), typeof(ThemeWrapPanel),
                new FrameworkPropertyMetadata(265.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        public static readonly DependencyProperty GapProperty =
            DependencyProperty.Register(nameof(Gap), typeof(double), typeof(ThemeWrapPanel),
                new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

        private const double CardAspect = 16.0 / 9.0;

        private int _columns = 1;
        private double _cardWidth = 220.0;
        private double _cardHeight = 220.0 / CardAspect;

        public double MinCardWidth
        {
            get => (double)GetValue(MinCardWidthProperty);
            set => SetValue(MinCardWidthProperty, value);
        }

        public double MaxCardWidth
        {
            get => (double)GetValue(MaxCardWidthProperty);
            set => SetValue(MaxCardWidthProperty, value);
        }

        public double Gap
        {
            get => (double)GetValue(GapProperty);
            set => SetValue(GapProperty, value);
        }

        private void ComputeColumnsAndSizes(double availableWidth)
        {
            double w = availableWidth;
            if (double.IsNaN(w) || double.IsInfinity(w) || w <= 0)
                w = ActualWidth > 0 ? ActualWidth : 1200.0;

            double gap = Math.Max(0.0, Gap);
            double minW = Math.Max(100.0, MinCardWidth);
            double maxW = Math.Max(minW, MaxCardWidth);
            double targetW = (minW + maxW) / 2.0;

            int cols = (int)Math.Round((w + gap) / (targetW + gap));
            if (cols < 1) cols = 1;

            double cWidth = (w - gap * (cols - 1)) / cols;

            if (cWidth < minW && cols > 1)
            {
                cols--;
                cWidth = (w - gap * (cols - 1)) / cols;
            }
            else if (cWidth > maxW)
            {
                int moreCols = cols + 1;
                double candidateWidth = (w - gap * (moreCols - 1)) / moreCols;
                if (candidateWidth >= minW * 0.92)
                {
                    cols = moreCols;
                    cWidth = candidateWidth;
                }
            }

            _columns = Math.Max(1, cols);
            _cardWidth = Math.Max(minW * 0.75, (w - gap * (_columns - 1)) / _columns);
            _cardHeight = _cardWidth / CardAspect;
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            ComputeColumnsAndSizes(availableSize.Width);

            double w = availableSize.Width;
            if (double.IsNaN(w) || double.IsInfinity(w) || w <= 0)
                w = (_cardWidth * _columns) + (Gap * (_columns - 1));

            int count = Children.Count;
            int rows = count > 0 ? (count + _columns - 1) / _columns : 0;
            double totalHeight = rows > 0 ? (rows * _cardHeight + Math.Max(0, rows - 1) * Gap) : 0;

            Size childSize = new Size(_cardWidth, _cardHeight);
            for (int i = 0; i < count; i++)
            {
                Children[i].Measure(childSize);
            }

            return new Size(w, totalHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            ComputeColumnsAndSizes(finalSize.Width);

            double x = 0;
            double y = 0;
            int col = 0;
            int count = Children.Count;

            for (int i = 0; i < count; i++)
            {
                if (col >= _columns)
                {
                    col = 0;
                    x = 0;
                    y += _cardHeight + Gap;
                }

                Children[i].Arrange(new Rect(x, y, _cardWidth, _cardHeight));
                x += _cardWidth + Gap;
                col++;
            }

            return finalSize;
        }
    }
}
