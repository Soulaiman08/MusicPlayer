using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicPlayer.Utilities;

/// <summary>
/// Real image-processing blur engine used by both the main wallpaper path
/// and the Theme Editor preview. Produces a frozen Pbgra32 BitmapSource.
/// Uses separable Gaussian blur with edge clamping to avoid halos.
/// Must be called on an STA thread (RenderTargetBitmap affinity).
/// </summary>
public static class WallpaperBlurEngine
{
    public const int AlgorithmVersion = 1;

    /// <summary>
    /// Render <paramref name="source"/> into a <paramref name="pixelWidth"/> x
    /// <paramref name="pixelHeight"/> bitmap (UniformToFill) and apply a
    /// separable Gaussian blur of <paramref name="radius"/> output pixels.
    /// When radius &lt; 0.5 the bitmap is returned unblurred.
    /// </summary>
    public static BitmapSource? RenderAndBlur(
        BitmapSource source,
        int pixelWidth, int pixelHeight,
        double dpiX, double dpiY,
        double radius)
    {
        if (source == null || pixelWidth < 1 || pixelHeight < 1) return null;
        if (dpiX <= 0 || double.IsNaN(dpiX)) dpiX = 96.0;
        if (dpiY <= 0 || double.IsNaN(dpiY)) dpiY = 96.0;

        int w = Math.Max(1, pixelWidth);
        int h = Math.Max(1, pixelHeight);
        double renderWidth = w * 96.0 / dpiX;
        double renderHeight = h * 96.0 / dpiY;

        // 1) Render source stretched/cropped into target (on this STA thread).
        var target = new RenderTargetBitmap(w, h, dpiX, dpiY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
            ctx.DrawRectangle(brush, null, new Rect(0, 0, renderWidth, renderHeight));
        }
        target.Render(visual);
        target.Freeze();

        if (radius < 0.5)
            return target;

        // 2) Extract pixels.
        int stride = w * 4;
        var pixels = new byte[h * stride];
        target.CopyPixels(pixels, stride, 0);

        // 3) Separable Gaussian blur with edge clamping.
        var blurred = GaussianBlur(pixels, w, h, radius);

        // 4) Build frozen result.
        var result = BitmapSource.Create(w, h, dpiX, dpiY, PixelFormats.Pbgra32, null, blurred, stride);
        result.Freeze();
        return result;
    }

    private static byte[] GaussianBlur(byte[] src, int w, int h, double radius)
    {
        // sigma: treat radius as the effective spread. Kernel half = 3*sigma.
        double sigma = Math.Max(0.1, radius);
        int half = (int)Math.Ceiling(3.0 * sigma);
        if (half < 1) half = 1;
        if (half > 128) half = 128;

        // Build normalized kernel.
        int ksize = half * 2 + 1;
        var kernel = new double[ksize];
        double sum = 0;
        for (int i = 0; i < ksize; i++)
        {
            int x = i - half;
            kernel[i] = Math.Exp(-(x * x) / (2.0 * sigma * sigma));
            sum += kernel[i];
        }
        for (int i = 0; i < ksize; i++) kernel[i] /= sum;

        // Horizontal pass.
        var tmp = new byte[src.Length];
        for (int y = 0; y < h; y++)
        {
            int row = y * w * 4;
            for (int x = 0; x < w; x++)
            {
                double b = 0, g = 0, r = 0, a = 0;
                for (int k = -half; k <= half; k++)
                {
                    int sx = x + k;
                    if (sx < 0) sx = 0;
                    if (sx >= w) sx = w - 1;
                    int idx = row + sx * 4;
                    double weight = kernel[k + half];
                    b += src[idx + 0] * weight;
                    g += src[idx + 1] * weight;
                    r += src[idx + 2] * weight;
                    a += src[idx + 3] * weight;
                }
                int o = row + x * 4;
                tmp[o + 0] = ClampByte(b);
                tmp[o + 1] = ClampByte(g);
                tmp[o + 2] = ClampByte(r);
                tmp[o + 3] = ClampByte(a);
            }
        }

        // Vertical pass.
        var dst = new byte[src.Length];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                double b = 0, g = 0, r = 0, a = 0;
                for (int k = -half; k <= half; k++)
                {
                    int sy = y + k;
                    if (sy < 0) sy = 0;
                    if (sy >= h) sy = h - 1;
                    int idx = (sy * w + x) * 4;
                    double weight = kernel[k + half];
                    b += tmp[idx + 0] * weight;
                    g += tmp[idx + 1] * weight;
                    r += tmp[idx + 2] * weight;
                    a += tmp[idx + 3] * weight;
                }
                int o = (y * w + x) * 4;
                dst[o + 0] = ClampByte(b);
                dst[o + 1] = ClampByte(g);
                dst[o + 2] = ClampByte(r);
                dst[o + 3] = ClampByte(a);
            }
        }
        return dst;
    }

    private static byte ClampByte(double v)
    {
        if (v < 0) return 0;
        if (v > 255) return 255;
        return (byte)Math.Round(v);
    }
}
