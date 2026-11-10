using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;

namespace MusicPlayer.Utilities;

/// <summary>
/// Bounded, LRU, memory-budgeted decode cache for full-resolution wallpapers.
/// Thread-safe. Shared by MainWindow (selection) and MainViewModel (Themes preload).
/// </summary>
public static class WallpaperDecodeCache
{
    private sealed class Entry
    {
        public BitmapImage Image = null!;
        public long Ticks;
        public long CostBytes;
    }

    private static readonly ConcurrentDictionary<string, Entry> _cache =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly object _gate = new();
    private static readonly SemaphoreSlim _decodeSemaphore = new(2, 2);

    /// <summary>Approximate memory budget for cached decoded wallpapers.</summary>
    public const long BudgetBytes = 256L * 1024 * 1024;

    private static long _usedBytes;

    public static bool TryGet(string key, out BitmapImage? image)
    {
        image = null;
        if (string.IsNullOrEmpty(key)) return false;
        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.Image != null)
            {
                entry.Ticks = Stopwatch.GetTimestamp();
                image = entry.Image;
                return true;
            }
        }
        return false;
    }

    public static void Store(string key, BitmapImage image)
    {
        if (string.IsNullOrEmpty(key) || image == null) return;
        long cost = (long)image.PixelWidth * image.PixelHeight * 4;
        if (cost <= 0 || cost > BudgetBytes / 4) return;

        lock (_gate)
        {
            if (_cache.TryGetValue(key, out var existing))
            {
                _usedBytes -= existing.CostBytes;
                _cache.TryRemove(key, out _);
            }

            while (_usedBytes + cost > BudgetBytes && _cache.Count > 0)
            {
                string? oldestKey = null;
                long oldestTicks = long.MaxValue;
                foreach (var kv in _cache)
                {
                    if (kv.Value.Ticks < oldestTicks)
                    {
                        oldestTicks = kv.Value.Ticks;
                        oldestKey = kv.Key;
                    }
                }
                if (oldestKey == null) break;
                if (_cache.TryRemove(oldestKey, out var removed))
                    _usedBytes -= removed.CostBytes;
            }

            _cache[key] = new Entry { Image = image, Ticks = Stopwatch.GetTimestamp(), CostBytes = cost };
            _usedBytes += cost;
        }
        LogHelper.LogInfo($"[WallpaperCache] store {key}: {image.PixelWidth}x{image.PixelHeight} usedMb={_usedBytes / (1024 * 1024)}");
    }

    public static async Task PreloadAsync(IEnumerable<string> sources, int decodeWidth, CancellationToken cancellationToken = default)
    {
        if (sources == null) return;
        if (decodeWidth < 480) decodeWidth = 480;
        if (decodeWidth > 3840) decodeWidth = 3840;

        foreach (var src in sources)
        {
            if (cancellationToken.IsCancellationRequested) break;
            if (string.IsNullOrWhiteSpace(src)) continue;

            string key = src + "|" + decodeWidth;
            if (TryGet(key, out _)) continue;

            try
            {
                await _decodeSemaphore.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            try
            {
                if (cancellationToken.IsCancellationRequested) break;
                if (TryGet(key, out _)) continue;

                BitmapImage? decoded = await Task.Run(() =>
                {
                    if (cancellationToken.IsCancellationRequested) return null;
                    return Decode(src, decodeWidth);
                }, cancellationToken);

                if (decoded != null && !cancellationToken.IsCancellationRequested)
                    Store(key, decoded);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                LogHelper.LogError($"[WallpaperCache] preload failed: {src}", ex);
            }
            finally
            {
                _decodeSemaphore.Release();
            }

            // Yield and add gradual delay to avoid saturating CPU/memory during bulk preload.
            try
            {
                await Task.Delay(120, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            await Task.Yield();
        }
    }

    private static BitmapImage? Decode(string uri, int maxWidth)
    {
        long d0 = Stopwatch.GetTimestamp();
        Stream? stream = null;
        try
        {
            var bi = new BitmapImage();
            bi.BeginInit();
            bi.CacheOption = BitmapCacheOption.OnLoad;
            bi.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bi.DecodePixelWidth = maxWidth;
            if (uri.StartsWith("pack:", StringComparison.OrdinalIgnoreCase))
            {
                stream = PackResourceLoader.GetStream(uri);
                if (stream != null)
                {
                    bi.StreamSource = stream;
                    LogHelper.LogInfo($"[WallpaperCache] preload pack resource via PackResourceLoader: {uri}");
                }
                else
                {
                    bi.UriSource = new Uri(uri, UriKind.Absolute);
                    LogHelper.LogInfo($"[WallpaperCache] preload pack fallback to UriSource: {uri}");
                }
            }
            else
            {
                bi.UriSource = new Uri(uri, UriKind.Absolute);
            }
            bi.EndInit();
            bi.Freeze();
            if (bi.PixelWidth <= 0 || bi.PixelHeight <= 0) return null;
            LogHelper.LogInfo($"[WallpaperCache] preload decoded {uri}: {bi.PixelWidth}x{bi.PixelHeight} decodeMs={(Stopwatch.GetTimestamp() - d0) * 1000.0 / Stopwatch.Frequency:0}");
            return bi;
        }
        catch (Exception ex)
        {
            LogHelper.LogError($"[WallpaperCache] preload decode failed: {uri}", ex);
            return null;
        }
        finally
        {
            stream?.Dispose();
        }
    }

    public static (int Entries, long UsedBytes) Stats
    {
        get
        {
            lock (_gate) return (_cache.Count, _usedBytes);
        }
    }
}
