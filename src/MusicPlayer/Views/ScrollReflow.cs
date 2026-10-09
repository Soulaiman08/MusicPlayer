using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace MusicPlayer.Views;

public static class ScrollReflow
{
    public static readonly DependencyProperty DeferredWidthProperty =
        DependencyProperty.RegisterAttached(
            "DeferredWidth", typeof(double), typeof(ScrollReflow),
            new FrameworkPropertyMetadata(0.0));

    public static double GetDeferredWidth(DependencyObject obj) => (double)obj.GetValue(DeferredWidthProperty);
    public static void SetDeferredWidth(DependencyObject obj, double value) => obj.SetValue(DeferredWidthProperty, value);

    public static void Attach(ScrollViewer scroll)
    {
        DispatcherTimer? timer = null;
        double lastWidth = 0;

        void UpdateWidth(double w)
        {
            if (Math.Abs(w - lastWidth) > 200)
            {
                timer?.Stop();
                timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    SetDeferredWidth(scroll, scroll.ActualWidth);
                };
                timer.Start();
            }
            else
            {
                timer?.Stop();
                SetDeferredWidth(scroll, w);
            }
            lastWidth = w;
        }

        scroll.SizeChanged += (_, _) => UpdateWidth(scroll.ActualWidth);
        scroll.Loaded += (_, _) => { lastWidth = scroll.ActualWidth; SetDeferredWidth(scroll, scroll.ActualWidth); };
    }
}
