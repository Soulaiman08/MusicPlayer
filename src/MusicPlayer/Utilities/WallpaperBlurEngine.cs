using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicPlayer.Utilities;

/// <summary>
/// Fast image blur engine. Three-pass box blur (sliding-sum, O(w*h))
/// approximating Gaussian. Intermediate values kept in float to avoid
/// repeated 8-bit rounding. Honours CancellationToken.
/// Must be called on an STA thread (RenderTargetBitmap affinity).
/// </summary>
public static class WallpaperBlurEngine
{
    public const int AlgorithmVersion = 3;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapSource> _baseCache =
        new(System.StringComparer.Ordinal);
    private static readonly object _baseCacheGate = new();

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

        string baseKey = source.GetHashCode().ToString() + "|" + w + "x" + h + "|" + dpiX.ToString("0.##") + "x" + dpiY.ToString("0.##");

        BitmapSource? baseBitmap = null;
        lock (_baseCacheGate)
        {
            _baseCache.TryGetValue(baseKey, out baseBitmap);
        }

        bool baseCacheHit = baseBitmap != null;

        if (baseBitmap == null)
        {
            long tRender0 = System.Diagnostics.Stopwatch.GetTimestamp();
            var target = new RenderTargetBitmap(w, h, dpiX, dpiY, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (var ctx = visual.RenderOpen())
            {
                var brush = new ImageBrush(source) { Stretch = Stretch.UniformToFill };
                ctx.DrawRectangle(brush, null, new Rect(0, 0, renderWidth, renderHeight));
            }
            target.Render(visual);
            target.Freeze();
            baseBitmap = target;
            long tRender1 = System.Diagnostics.Stopwatch.GetTimestamp();
            LogHelper.LogInfo($"[WallpaperPerf] SourceRender={(tRender1 - tRender0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms size={w}x{h} cacheHit=False");

            lock (_baseCacheGate)
            {
                if (_baseCache.Count >= 8) _baseCache.Clear();
                _baseCache[baseKey] = baseBitmap;
            }
        }
        else
        {
            LogHelper.LogInfo($"[WallpaperPerf] SourceRender=0.0ms size={w}x{h} cacheHit=True");
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (radius < 0.5)
            return baseBitmap;

        int stride = w * 4;
        var pixels = new byte[h * stride];

        // SourceRender timing already logged during base render above.
        long tCopy0 = System.Diagnostics.Stopwatch.GetTimestamp();
        baseBitmap.CopyPixels(pixels, stride, 0);
        long tCopy1 = System.Diagnostics.Stopwatch.GetTimestamp();
        LogHelper.LogInfo($"[WallpaperPerf] CopyPixels={(tCopy1 - tCopy0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms baseCacheHit={baseCacheHit}");

        int n = w * h * 4;
        var cur = new float[n];
        for (int i = 0; i < n; i++) cur[i] = pixels[i];
        var tmp = new float[n];

        int half = Math.Max(1, (int)Math.Round(radius / 3.0));
        if (half < 1) half = 1;

        long hAccum = 0, vAccum = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long hs = System.Diagnostics.Stopwatch.GetTimestamp();
            HorizontalBoxFloat(cur, tmp, w, h, half, cancellationToken);
            hAccum += System.Diagnostics.Stopwatch.GetTimestamp() - hs;

            cancellationToken.ThrowIfCancellationRequested();
            long vs = System.Diagnostics.Stopwatch.GetTimestamp();
            VerticalBoxFloat(tmp, cur, w, h, half, cancellationToken);
            vAccum += System.Diagnostics.Stopwatch.GetTimestamp() - vs;
        }
        LogHelper.LogInfo($"[WallpaperPerf] HorizontalPasses={hAccum * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms VerticalPasses={vAccum * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms");

        // Convert float -> byte once, clamp colors to alpha (premul safety).
        for (int i = 0; i < n; i += 4)
        {
            float a = Clamp(cur[i + 3], 0f, 255f);
            float b = Clamp(cur[i + 0], 0f, a);
            float g = Clamp(cur[i + 1], 0f, a);
            float r = Clamp(cur[i + 2], 0f, a);
            pixels[i + 0] = (byte)(b + 0.5f);
            pixels[i + 1] = (byte)(g + 0.5f);
            pixels[i + 2] = (byte)(r + 0.5f);
            pixels[i + 3] = (byte)(a + 0.5f);
        }

        long tBc0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = BitmapSource.Create(w, h, dpiX, dpiY, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze();
        long tBc1 = System.Diagnostics.Stopwatch.GetTimestamp();
        LogHelper.LogInfo($"[WallpaperPerf] BitmapCreateFreeze={(tBc1 - tBc0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms");
        return result;
    }

    private static float Clamp(float v, float lo, float hi)
    {
        if (v < lo) return lo;
        if (v > hi) return hi;
        return v;
    }

    private static void HorizontalBoxFloat(float[] src, float[] dst, int w, int h, int half, CancellationToken token)
    {
        int window = half * 2 + 1;
        float invWindow = 1f / window;
        for (int y = 0; y < h; y++)
        {
            token.ThrowIfCancellationRequested();
            int row = y * w * 4;
            for (int c = 0; c < 4; c++)
            {
                float sum = 0f;
                for (int k = -half; k <= half; k++)
                {
                    int sx = k;
                    if (sx < 0) sx = 0;
                    if (sx >= w) sx = w - 1;
                    sum += src[row + sx * 4 + c];
                }
                for (int x = 0; x < w; x++)
                {
                    dst[row + x * 4 + c] = sum * invWindow;
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

    private static void VerticalBoxFloat(float[] src, float[] dst, int w, int h, int half, CancellationToken token)
    {
        int window = half * 2 + 1;
        float invWindow = 1f / window;
        for (int x = 0; x < w; x++)
        {
            token.ThrowIfCancellationRequested();
            for (int c = 0; c < 4; c++)
            {
                float sum = 0f;
                for (int k = -half; k <= half; k++)
                {
                    int sy = k;
                    if (sy < 0) sy = 0;
                    if (sy >= h) sy = h - 1;
                    sum += src[(sy * w + x) * 4 + c];
                }
                for (int y = 0; y < h; y++)
                {
                    dst[(y * w + x) * 4 + c] = sum * invWindow;
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
