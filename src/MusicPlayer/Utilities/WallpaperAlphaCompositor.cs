using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicPlayer.Utilities;

internal static class WallpaperAlphaCompositor
{
    public static BitmapSource Apply(BitmapSource source, double blackOverlayOpacity)
    {
        ArgumentNullException.ThrowIfNull(source);
        double opacity = Math.Clamp(blackOverlayOpacity, 0.0, 1.0);
        if (opacity == 0.0) return source;

        var converted = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
        converted.Freeze();

        int stride = checked(source.PixelWidth * 4);
        byte[] pixels = new byte[checked(stride * source.PixelHeight)];
        converted.CopyPixels(pixels, stride, 0);
        ApplyBlackOverlay(pixels, opacity);

        var result = BitmapSource.Create(
            source.PixelWidth,
            source.PixelHeight,
            source.DpiX,
            source.DpiY,
            PixelFormats.Pbgra32,
            null,
            pixels,
            stride);
        result.Freeze();
        return result;
    }

    private static void ApplyBlackOverlay(byte[] premultipliedBgra, double opacity)
    {
        double retain = 1.0 - opacity;
        for (int i = 0; i < premultipliedBgra.Length; i += 4)
        {
            premultipliedBgra[i] = Scale(premultipliedBgra[i], retain);
            premultipliedBgra[i + 1] = Scale(premultipliedBgra[i + 1], retain);
            premultipliedBgra[i + 2] = Scale(premultipliedBgra[i + 2], retain);
            premultipliedBgra[i + 3] = (byte)Math.Clamp(
                (int)Math.Round(premultipliedBgra[i + 3] * retain + 255.0 * opacity),
                0,
                255);
        }
    }

    private static byte Scale(byte value, double factor)
        => (byte)Math.Clamp((int)Math.Round(value * factor), 0, 255);
}
