using System;
using System.IO.MemoryMappedFiles;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace FpsOverlay;

public partial class MainWindow : Window
{
    private MemoryMappedFile? _mmf;
    private MemoryMappedViewAccessor? _accessor;
    private readonly DispatcherTimer _pollTimer;
    private long _lastFrameCount;
    private double _smoothedFps = 60.0;

    public MainWindow()
    {
        InitializeComponent();

        _pollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
        _pollTimer.Tick += PollTelemetry;
        _pollTimer.Start();
    }

    private void PollTelemetry(object? sender, EventArgs e)
    {
        try
        {
            if (_accessor == null)
            {
                try
                {
                    _mmf = MemoryMappedFile.OpenExisting("MusicPlayer_FpsTelemetry");
                    _accessor = _mmf.CreateViewAccessor(0, 64, MemoryMappedFileAccess.Read);
                }
                catch
                {
                    FpsText.Text = "WAITING";
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                    FrameTimeText.Text = "";
                    StatsText.Text = "App not running";
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0x88));
                    return;
                }
            }

            long frameCount = _accessor.ReadInt64(0);
            double deltaMs = _accessor.ReadDouble(8);
            long timestamp = _accessor.ReadInt64(16);
            double avgFps = _accessor.ReadDouble(24);
            double minFps = _accessor.ReadDouble(32);
            int janks = _accessor.ReadInt32(40);

            long now = System.Diagnostics.Stopwatch.GetTimestamp();
            double timeSinceLastFrameMs = (now - timestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;

            if (timeSinceLastFrameMs > 150.0 || frameCount == _lastFrameCount)
            {
                // UI is resting (WPF only presents when content changes)
                FpsText.Text = "IDLE";
                FpsText.Foreground = new SolidColorBrush(Color.FromRgb(0x60, 0x80, 0xA0));
                FrameTimeText.Text = "-- ms";
                StatsText.Text = "Resting";
                StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x60, 0x80, 0xA0));
            }
            else
            {
                // Smooth the FPS reading for stable, readable display
                double targetFps = avgFps > 0 ? Math.Clamp(avgFps, 5.0, 120.0) : 60.0;
                _smoothedFps = (_smoothedFps * 0.7) + (targetFps * 0.3);

                FpsText.Text = $"{_smoothedFps:0} FPS";

                if (_smoothedFps >= 50.0)
                {
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)); // Cyan 60fps
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF));
                }
                else if (_smoothedFps >= 30.0)
                {
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(0xF5, 0xC0, 0x26)); // Yellow 30-50fps
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xF5, 0xC0, 0x26));
                }
                else
                {
                    FpsText.Foreground = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52)); // Red <30fps
                    StatusDot.Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x52, 0x52));
                }

                FrameTimeText.Text = $"{deltaMs:0.0} ms";
                StatsText.Text = janks > 0 ? $"{janks} janks" : "smooth";
            }

            _lastFrameCount = frameCount;
        }
        catch (Exception ex)
        {
            _accessor = null;
            _mmf?.Dispose();
            _mmf = null;
            StatsText.Text = ex.Message;
        }
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
