using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicPlayer.Utilities;

/// <summary>
/// Fast image blur engine. Uses three-pass box blur (sliding-sum, O(w*h))
/// which approximates a Gaussian. Honours CancellationToken for real
/// cancellation. Must be called on an STA thread (RenderTargetBitmap affinity).
/// </summary>
public static class WallpaperBlurEngine
{
    public const int AlgorithmVersion = 2;

    public static BitmapSource? RenderAndBlur(
        BitmapSource source,
        int pixelWidth, int pixelHeight,
        double dpiX, double dpiY,
        double radius,
        CancellationToken cancellationToken = default)
    {
        if (source == null || pixelWidth < 1 || pixelHeight < 1) return null;
        if (dpiX <= 0 || double.IsNaN(dpiX)) dpiX = 96.0;
        if (dpiY <= 0 || double.IsNaN(dpiY)) dpiY = 96.0;

        int w = Math.Max(1, pixelWidth);
        int h = Math.Max(1, pixelHeight);
        double renderWidth = w * 96.0 / dpiX;
        double renderHeight = h * 96.0 / dpiY;

        var target = new RenderTargetBitmap(w, h, dpiX, dpiY, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var ctx = visual.RenderOpen())
        {
            var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
            ctx.DrawRectangle(brush, null, new Rect(0, 0, renderWidth, renderHeight));
        }
        target.Render(visual);
        target.Freeze();

        cancellationToken.ThrowIfCancellationRequested();

        if (radius < 0.5)
            return target;

        int stride = w * 4;
        var pixels = new byte[h * stride];
        target.CopyPixels(pixels, stride, 0);

        BoxBlur(pixels, w, h, radius, cancellationToken);

        var result = BitmapSource.Create(w, h, dpiX, dpiY, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    private static void BoxBlur(byte[] src, int w, int h, double radius, CancellationToken token)
    {
        int half = Math.Max(1, (int)Math.Round(radius / 3.0));
        if (half < 1) half = 1;

        var a = src;
        var b = new byte[src.Length];

        for (int pass = 0; pass < 3; pass++)
        {
            token.ThrowIfCancellationRequested();
            HorizontalBox(a, b, w, h, half, token);
            token.ThrowIfCancellationRequested();
            VerticalBox(b, a, w, h, half, token);
        }
        if (!ReferenceEquals(a, src))
        {
            Buffer.BlockCopy(a, 0, src, 0, src.Length);
        }
    }

    private static void HorizontalBox(byte[] src, byte[] dst, int w, int h, int half, CancellationToken token)
    {
        int window = half * 2 + 1;
        for (int y = 0; y < h; y++)
        {
            token.ThrowIfCancellationRequested();
            int row = y * w * 4;
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int k = -half; k <= half; k++)
                {
                    int sx = k;
                    if (sx < 0) sx = 0;
                    if (sx >= w) sx = w - 1;
                    sum += src[row + sx * 4 + c];
                }
                for (int x = 0; x < w; x++)
                {
                    dst[row + x * 4 + c] = (byte)(sum / window);
                    int leftIdx = x - half;
                    if (leftIdx < 0) leftIdx = 0;
                    int rightIdx = x + half + 1;
                    if (rightIdx >= w) rightIdx = w - 1;
                    sum -= src[row + leftIdx * 4 + c];
                    sum += src[row + rightIdx * 4 + c];
                }
            }
        }
    }

    private static void VerticalBox(byte[] src, byte[] dst, int w, int h, int half, CancellationToken token)
    {
        int window = half * 2 + 1;
        for (int x = 0; x < w; x++)
        {
            token.ThrowIfCancellationRequested();
            for (int c = 0; c < 4; c++)
            {
                int sum = 0;
                for (int k = -half; k <= half; k++)
                {
                    int sy = k;
                    if (sy < 0) sy = 0;
                    if (sy >= h) sy = h - 1;
                    sum += src[(sy * w + x) * 4 + c];
                }
                for (int y = 0; y < h; y++)
                {
                    dst[(y * w + x) * 4 + c] = (byte)(sum / window);
                    int topIdx = y - half;
                    if (topIdx < 0) topIdx = 0;
                    int botIdx = y + half + 1;
                    if (botIdx >= h) botIdx = h - 1;
                    sum -= src[(topIdx * w + x) * 4 + c];
                    sum += src[(botIdx * w + x) * 4 + c];
                }
            }
        }
    }
}
