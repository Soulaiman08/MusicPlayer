using System;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MusicPlayer.Utilities;

/// <summary>
/// Fast image blur engine. Three-pass box blur (sliding-sum, O(w*h))
/// approximating Gaussian. Intermediate values stored in Q8 fixed-point
/// (ushort) to halve memory vs float while avoiding 8-bit truncation between passes.
/// Base render cache uses LRU + per-key single-flight.
/// Must be called on an STA thread (RenderTargetBitmap affinity).
/// </summary>
public static class WallpaperBlurEngine
{
    public const int AlgorithmVersion = 4;

    private sealed class BaseEntry
    {
        public BitmapSource Image = null!;
        public long Ticks;
        public long CostBytes;
    }

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, BaseEntry> _baseCache =
        new(System.StringComparer.Ordinal);
    private static readonly object _baseGate = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, object> _baseLocks =
        new(System.StringComparer.Ordinal);
    private const long BaseBudgetBytes = 64L * 1024 * 1024;
    private static long _baseUsedBytes;

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

        string baseKey = source.GetHashCode().ToString() + "|" + w + "x" + h + "|" +
                         dpiX.ToString("0.##") + "x" + dpiY.ToString("0.##");

        // Single-flight per key.
        object keyLock = _baseLocks.GetOrAdd(baseKey, _ => new object());
        BitmapSource? baseBitmap = null;
        bool cacheHit = false;
        long tRender0 = System.Diagnostics.Stopwatch.GetTimestamp();
        lock (keyLock)
        {
            // Recheck cache under lock.
            lock (_baseGate)
            {
                if (_baseCache.TryGetValue(baseKey, out var e) && e.Image != null)
                {
                    e.Ticks = System.Diagnostics.Stopwatch.GetTimestamp();
                    baseBitmap = e.Image;
                    cacheHit = true;
                }
            }

            if (baseBitmap == null)
            {
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

                long cost = (long)w * h * 4;
                lock (_baseGate)
                {
                    // LRU eviction.
                    while (_baseUsedBytes + cost > BaseBudgetBytes && _baseCache.Count > 0)
                    {
                        string? oldestKey = null; long oldestTicks = long.MaxValue;
                        foreach (var kv in _baseCache)
                            if (kv.Value.Ticks < oldestTicks) { oldestTicks = kv.Value.Ticks; oldestKey = kv.Key; }
                        if (oldestKey == null) break;
                        if (_baseCache.TryRemove(oldestKey, out var removed))
                            _baseUsedBytes -= removed.CostBytes;
                    }
                    _baseCache[baseKey] = new BaseEntry { Image = baseBitmap, Ticks = System.Diagnostics.Stopwatch.GetTimestamp(), CostBytes = cost };
                    _baseUsedBytes += cost;
                }
            }
        }
        _baseLocks.TryRemove(baseKey, out _);

        long tRender1 = System.Diagnostics.Stopwatch.GetTimestamp();
        LogHelper.LogInfo($"[WallpaperPerf] SourceRender={(tRender1 - tRender0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms size={w}x{h} cacheHit={cacheHit}");

        cancellationToken.ThrowIfCancellationRequested();

        if (radius < 0.5)
            return baseBitmap;

        int stride = w * 4;
        var pixels = new byte[h * stride];

        long tCopy0 = System.Diagnostics.Stopwatch.GetTimestamp();
        baseBitmap.CopyPixels(pixels, stride, 0);
        long tCopy1 = System.Diagnostics.Stopwatch.GetTimestamp();
        LogHelper.LogInfo($"[WallpaperPerf] CopyPixels={(tCopy1 - tCopy0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms");

        int n = w * h * 4;
        // ArrayPool buffers.
        var pool = System.Buffers.ArrayPool<ushort>.Shared;
        var cur = pool.Rent(n);
        var tmp = pool.Rent(n);
        try
        {
            for (int i = 0; i < n; i++) cur[i] = (ushort)(pixels[i] << 8);

        int half = Math.Max(1, (int)Math.Round(radius / 3.0));
        if (half < 1) half = 1;

        long hAccum = 0, vAccum = 0;
        for (int pass = 0; pass < 3; pass++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long hs = System.Diagnostics.Stopwatch.GetTimestamp();
            HorizontalBoxQ8(cur, tmp, w, h, half, cancellationToken);
            hAccum += System.Diagnostics.Stopwatch.GetTimestamp() - hs;

            cancellationToken.ThrowIfCancellationRequested();
            long vs = System.Diagnostics.Stopwatch.GetTimestamp();
            // Transpose-based vertical pass for better locality.
            TransposeQ8(tmp, cur, w, h);
            HorizontalBoxQ8(cur, tmp, h, w, half, cancellationToken);
            TransposeQ8(tmp, cur, h, w);
            vAccum += System.Diagnostics.Stopwatch.GetTimestamp() - vs;
        }
        LogHelper.LogInfo($"[WallpaperPerf] HorizontalPasses={hAccum * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms VerticalPasses={vAccum * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms");

        long tBc0 = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < n; i += 4)
        {
            ushort aQ = cur[i + 3];
            byte a = (byte)((aQ + 128) >> 8);
            byte b = (byte)Math.Min((aQ + 128) >> 8, (cur[i + 0] + 128) >> 8);
            byte g = (byte)Math.Min((aQ + 128) >> 8, (cur[i + 1] + 128) >> 8);
            byte r = (byte)Math.Min((aQ + 128) >> 8, (cur[i + 2] + 128) >> 8);
            pixels[i + 0] = b;
            pixels[i + 1] = g;
            pixels[i + 2] = r;
            pixels[i + 3] = a;
        }
        var result = BitmapSource.Create(w, h, dpiX, dpiY, PixelFormats.Pbgra32, null, pixels, stride);
        result.Freeze();
        long tBc1 = System.Diagnostics.Stopwatch.GetTimestamp();
        LogHelper.LogInfo($"[WallpaperPerf] BitmapCreateFreeze={(tBc1 - tBc0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency:0.0}ms");
        return result;
        }
        finally
        {
            pool.Return(cur);
            pool.Return(tmp);
        }
    }

    private static void HorizontalBoxQ8(ushort[] src, ushort[] dst, int w, int h, int half, CancellationToken token)
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
                    dst[row + x * 4 + c] = (ushort)(sum / window);
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

    private static void TransposeQ8(ushort[] src, ushort[] dst, int w, int h)
    {
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                for (int c = 0; c < 4; c++)
                    dst[(x * h + y) * 4 + c] = src[(y * w + x) * 4 + c];
    }

    private static void VerticalBoxQ8(ushort[] src, ushort[] dst, int w, int h, int half, CancellationToken token)
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
                    dst[(y * w + x) * 4 + c] = (ushort)(sum / window);
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
