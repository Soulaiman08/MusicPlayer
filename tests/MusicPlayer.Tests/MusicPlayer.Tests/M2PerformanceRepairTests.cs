using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using MusicPlayer.Converters;
using MusicPlayer.Views;
using MusicPlayer.Views.Controls;
using Xunit;

namespace MusicPlayer.Tests;

public class M2PerformanceRepairTests
{
    private static void RunInSta(Action action)
    {
        Exception? ex = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { ex = e; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (ex != null) throw new AggregateException(ex);
    }

    [Fact]
    public void RoundedClipConverter_ReturnsFrozenRectangleGeometry_ForSidebar()
    {
        RunInSta(() =>
        {
            var converter = new RoundedClipConverter();
            
            // Open drawer: radius should be 20
            var resultOpen = converter.Convert(
                new object[] { 230.0, 600.0, true },
                typeof(Geometry),
                "sidebar",
                CultureInfo.InvariantCulture);

            Assert.NotNull(resultOpen);
            var geomOpen = Assert.IsType<RectangleGeometry>(resultOpen);
            Assert.True(geomOpen.IsFrozen);
            Assert.Equal(20, geomOpen.RadiusX);
            Assert.Equal(20, geomOpen.RadiusY);
            Assert.Equal(230.0, geomOpen.Rect.Width);
            Assert.Equal(600.0, geomOpen.Rect.Height);

            // Closed drawer: radius should be 25
            var resultClosed = converter.Convert(
                new object[] { 50.0, 600.0, false },
                typeof(Geometry),
                "sidebar",
                CultureInfo.InvariantCulture);

            Assert.NotNull(resultClosed);
            var geomClosed = Assert.IsType<RectangleGeometry>(resultClosed);
            Assert.True(geomClosed.IsFrozen);
            Assert.Equal(25, geomClosed.RadiusX);
            Assert.Equal(25, geomClosed.RadiusY);
            Assert.Equal(50.0, geomClosed.Rect.Width);
            Assert.Equal(600.0, geomClosed.Rect.Height);
        });
    }

    [Fact]
    public void RoundedClipConverter_ReturnsNull_ForInvalidDimensions()
    {
        RunInSta(() =>
        {
            var converter = new RoundedClipConverter();

            var resultZero = converter.Convert(
                new object[] { 0.0, 600.0, true },
                typeof(Geometry),
                "sidebar",
                CultureInfo.InvariantCulture);

            Assert.Null(resultZero);

            var resultNaN = converter.Convert(
                new object[] { double.NaN, 600.0, true },
                typeof(Geometry),
                "sidebar",
                CultureInfo.InvariantCulture);

            Assert.Null(resultNaN);
        });
    }

    [Fact]
    public void AudioVisualizerControl_PauseAndResume_ControlsLifecycleWithoutCrashing()
    {
        RunInSta(() =>
        {
            var viz = new AudioVisualizerControl();
            viz.PauseRendering();
            viz.ResumeRendering();
        });
    }
}
