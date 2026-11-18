using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.Utilities;

namespace MusicPlayer.Tests;

public class WallpaperAlphaCompositorTests
{
    [Fact]
    public void Apply_BakesBlackOverlayWithoutChangingResolution()
    {
        byte[] sourcePixels =
        {
            100, 150, 200, 255,
            0, 0, 0, 0
        };
        var source = BitmapSource.Create(
            2,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            sourcePixels,
            8);
        source.Freeze();

        var result = WallpaperAlphaCompositor.Apply(source, 0.5);
        byte[] resultPixels = new byte[8];
        result.CopyPixels(resultPixels, 8, 0);

        Assert.Equal(2, result.PixelWidth);
        Assert.Equal(1, result.PixelHeight);
        Assert.Equal(new byte[] { 50, 75, 100, 255, 0, 0, 0, 128 }, resultPixels);
    }

    [Fact]
    public void Apply_ZeroOpacityReturnsOriginalBitmap()
    {
        var source = BitmapSource.Create(
            1,
            1,
            96,
            96,
            PixelFormats.Bgra32,
            null,
            new byte[] { 10, 20, 30, 255 },
            4);
        source.Freeze();

        Assert.Same(source, WallpaperAlphaCompositor.Apply(source, 0));
    }
}
