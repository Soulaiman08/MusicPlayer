using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using MusicPlayer.ImageLoading;
using MusicPlayer.Models;
using MusicPlayer.Utilities;

namespace MusicPlayer.Library;

internal readonly record struct AlbumArtPerfSnapshot(
    long Calls,
    long UiCalls,
    long WorkerCalls,
    long CacheHits,
    long CacheMisses,
    long DiskHits,
    long ThumbnailHits,
    long DecodeCount,
    long DecodeTicks,
    long UiDecodeCount,
    long UiDecodeTicks,
    long CallTicks,
    long UiCallTicks);

internal enum AlbumArtUiMetric
{
    Converter,
    MemoryLookup,
    DiskPath,
    Decode
}

internal sealed class AlbumArtUiProbe
{
    private sealed class Metric
    {
        public long Count;
        public long WallTicks;
        public long CpuTicks;
        public long CpuSamples;
    }

    internal readonly struct Measurement : IDisposable
    {
        private readonly AlbumArtUiProbe? _probe;
        private readonly AlbumArtUiMetric _metric;
        private readonly long _wallStarted;
        private readonly long _cpuStarted;
        private readonly bool _hasCpuStarted;

        internal Measurement(AlbumArtUiProbe probe, AlbumArtUiMetric metric)
        {
            _probe = probe;
            _metric = metric;
            _wallStarted = Stopwatch.GetTimestamp();
            _hasCpuStarted = AlbumArtLoader.TryGetCurrentThreadCpuTicks(out _cpuStarted);
            probe.RecordMetricStarted(metric);
        }

        public void Dispose()
        {
            if (_probe == null) return;

            long wallElapsed = Stopwatch.GetTimestamp() - _wallStarted;
            bool hasCpuEnded = AlbumArtLoader.TryGetCurrentThreadCpuTicks(out long cpuEnded);
            _probe.RecordMetricCompleted(
                _metric,
                wallElapsed,
                _hasCpuStarted && hasCpuEnded ? cpuEnded - _cpuStarted : null);
        }
    }

    private readonly string _trigger;
    private readonly string _mode;
    private readonly string _state;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly bool _hasCpuStarted;
    private readonly long _cpuStarted;
    private readonly Metric _converter = new();
    private readonly Metric _memoryLookup = new();
    private readonly Metric _diskPath = new();
    private readonly Metric _decode = new();
    private long _memoryHits;
    private long _memoryMisses;
    private long _diskHits;
    private long _diskMisses;
    private long _decodeSuccesses;
    private long _scrollSkips;

    internal AlbumArtUiProbe(string trigger, string mode, string state)
    {
        _trigger = trigger;
        _mode = mode;
        _state = state;
        _hasCpuStarted = AlbumArtLoader.TryGetCurrentThreadCpuTicks(out _cpuStarted);
    }

    internal Measurement Measure(AlbumArtUiMetric metric) => new(this, metric);

    internal void RecordMemoryResult(bool hit)
    {
        if (hit) Interlocked.Increment(ref _memoryHits);
        else Interlocked.Increment(ref _memoryMisses);
    }

    internal void RecordDiskResult(bool hit)
    {
        if (hit) Interlocked.Increment(ref _diskHits);
        else Interlocked.Increment(ref _diskMisses);
    }

    internal void RecordDecodeResult(bool success)
    {
        if (success) Interlocked.Increment(ref _decodeSuccesses);
    }

    internal void RecordScrollSkip() => Interlocked.Increment(ref _scrollSkips);

    internal void RecordMetricStarted(AlbumArtUiMetric metric) =>
        Interlocked.Increment(ref GetMetric(metric).Count);

    internal void RecordMetricCompleted(AlbumArtUiMetric metric, long wallTicks, long? cpuTicks)
    {
        var target = GetMetric(metric);
        Interlocked.Add(ref target.WallTicks, wallTicks);
        if (cpuTicks is long elapsedCpuTicks && elapsedCpuTicks >= 0)
        {
            Interlocked.Add(ref target.CpuTicks, elapsedCpuTicks);
            Interlocked.Increment(ref target.CpuSamples);
        }
    }

    internal string Complete(string endReason)
    {
        if (!AlbumArtLoader.EndUiImageProbe(this))
            return $"TAB-IMAGE-PROBE trigger={_trigger} mode={_mode} state={_state} end={endReason} status=not-active";

        double windowMs = (Stopwatch.GetTimestamp() - _started) * 1000.0 / Stopwatch.Frequency;
        string uiCpuMs = _hasCpuStarted && AlbumArtLoader.TryGetCurrentThreadCpuTicks(out long cpuEnded)
            ? $"{(cpuEnded - _cpuStarted) / 10000.0:0.0}"
            : "unavailable";

        return
            $"TAB-IMAGE-PROBE trigger={_trigger} mode={_mode} state={_state} end={endReason} " +
            $"windowMs={windowMs:0.0} uiCpuMs={uiCpuMs} " +
            $"converters={ReadCount(_converter)} converterWallMs={ReadWallMs(_converter):0.0} converterCpuMs={ReadCpuMs(_converter)} " +
            $"memoryLookups={ReadCount(_memoryLookup)} memoryHits={Interlocked.Read(ref _memoryHits)} memoryMisses={Interlocked.Read(ref _memoryMisses)} " +
            $"memoryWallMs={ReadWallMs(_memoryLookup):0.0} memoryCpuMs={ReadCpuMs(_memoryLookup)} " +
            $"diskHits={Interlocked.Read(ref _diskHits)} diskMisses={Interlocked.Read(ref _diskMisses)} " +
            $"scrollSkips={Interlocked.Read(ref _scrollSkips)} " +
            $"diskPaths={ReadCount(_diskPath)} diskWallMs={ReadWallMs(_diskPath):0.0} diskCpuMs={ReadCpuMs(_diskPath)} " +
            $"decodes={ReadCount(_decode)} decodeSuccesses={Interlocked.Read(ref _decodeSuccesses)} " +
            $"decodeWallMs={ReadWallMs(_decode):0.0} decodeCpuMs={ReadCpuMs(_decode)}";
    }

    private Metric GetMetric(AlbumArtUiMetric metric) => metric switch
    {
        AlbumArtUiMetric.Converter => _converter,
        AlbumArtUiMetric.MemoryLookup => _memoryLookup,
        AlbumArtUiMetric.DiskPath => _diskPath,
        AlbumArtUiMetric.Decode => _decode,
        _ => throw new ArgumentOutOfRangeException(nameof(metric))
    };

    private static long ReadCount(Metric metric) => Interlocked.Read(ref metric.Count);

    private static double ReadWallMs(Metric metric) =>
        Interlocked.Read(ref metric.WallTicks) * 1000.0 / Stopwatch.Frequency;

    private static string ReadCpuMs(Metric metric)
    {
        long count = ReadCount(metric);
        if (Interlocked.Read(ref metric.CpuSamples) != count) return "partial";

        long cpuTicks = Interlocked.Read(ref metric.CpuTicks);
        return count > 0 && cpuTicks < 1000
            ? "<0.1-below-resolution"
            : $"{cpuTicks / 10000.0:0.0}";
    }
}

public class AlbumArtLoader : IDisposable
{
    private sealed class ProgressiveArtImage
    {
        private readonly DrawingGroup _drawing;
        private readonly Dispatcher _dispatcher;

        public ProgressiveArtImage(ImageSource placeholder, Dispatcher dispatcher)
        {
            _dispatcher = dispatcher;
            _drawing = new DrawingGroup();
            _drawing.Children.Add(new ImageDrawing(placeholder, GetImageBounds(placeholder)));
            Image = new DrawingImage(_drawing);
        }

        public DrawingImage Image { get; }
        public Dispatcher Dispatcher => _dispatcher;

        public void SetSource(ImageSource source)
        {
            if (!_dispatcher.CheckAccess())
            {
                throw new InvalidOperationException("Progressive artwork must be updated on its owning dispatcher.");
            }

            _drawing.Children.Clear();
            _drawing.Children.Add(new ImageDrawing(source, GetImageBounds(source)));
        }

        private static Rect GetImageBounds(ImageSource source)
        {
            if (source is BitmapSource bitmap)
            {
                return new Rect(0, 0, Math.Max(1, bitmap.PixelWidth), Math.Max(1, bitmap.PixelHeight));
            }

            if (source is DrawingImage drawingImage && drawingImage.Drawing is { } drawing &&
                !drawing.Bounds.IsEmpty)
            {
                return drawing.Bounds;
            }

            return new Rect(0, 0, 64, 64);
        }
    }

    private readonly ILogger<AlbumArtLoader>? _logger;
    private readonly AlbumArtAssetService _assetService = new();
    private readonly ImageLoadingCoordinator _coordinator;
    private readonly bool _ownsCoordinator;

    // Direct in-memory cache mirrors for test inspection compatibility.
    // They are deliberately bounded by byte budget: the coordinator's LRU only evicts its own
    // map, so an unbounded mirror here pinned every decoded bitmap for the
    // whole session (one of the main sources of the 550-650 MB private set).
    private const long ThumbnailCacheBudgetBytes = 16L * 1024 * 1024; // 16 MB
    private const long MediumCacheBudgetBytes = 32L * 1024 * 1024;   // 32 MB
    private const long FullCacheBudgetBytes = 64L * 1024 * 1024;     // 64 MB

    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new();
    private readonly ConcurrentDictionary<string, ImageSource?> _mediumCache = new();
    private readonly ConcurrentDictionary<string, ImageSource?> _fullQualityCache = new();

    // Track estimated byte sizes for budget enforcement
    private readonly ConcurrentDictionary<string, long> _cacheSizes = new();
    private readonly ConcurrentDictionary<string, long> _mediumCacheSizes = new();
    private readonly ConcurrentDictionary<string, long> _fullCacheSizes = new();
    private long _currentCacheBytes;
    private long _currentMediumCacheBytes;
    private long _currentFullCacheBytes;

    private static void StoreBounded(
        ConcurrentDictionary<string, ImageSource?> cache,
        ConcurrentDictionary<string, long> cacheSizes,
        ref long currentBytes,
        long budgetBytes,
        string key,
        ImageSource? value)
    {
        if (string.IsNullOrWhiteSpace(key) || value == null) return;

        long sizeBytes = EstimateSizeBytes(value);
        cache[key] = value;
        cacheSizes[key] = sizeBytes;
        Interlocked.Add(ref currentBytes, sizeBytes);

        // Evict LRU if over budget (simple approximation: remove oldest entries)
        while (currentBytes > budgetBytes && cacheSizes.Count > 0)
        {
            // Find and remove an entry (not the one we just added)
            foreach (var pair in cacheSizes)
            {
                if (string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase)) continue;
                if (cache.TryRemove(pair.Key, out _) && cacheSizes.TryRemove(pair.Key, out long removedSize))
                {
                    Interlocked.Add(ref currentBytes, -removedSize);
                    break;
                }
            }
        }
    }

    private static long EstimateSizeBytes(ImageSource image)
    {
        if (image is BitmapSource bitmap)
        {
            int w = Math.Max(1, bitmap.PixelWidth);
            int h = Math.Max(1, bitmap.PixelHeight);
            int bpp = bitmap.Format.BitsPerPixel > 0 ? bitmap.Format.BitsPerPixel : 32;
            return ((long)w * h * bpp) / 8 + 128;
        }

        return 4096; // Approximate baseline for vector/drawing images
    }

    private readonly ConcurrentDictionary<string, ProgressiveArtImage> _pendingThumbnailImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProgressiveArtImage> _pendingMediumImages = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ProgressiveArtImage> _pendingFullImages = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _testPendingMediumParses = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, Lazy<Task<ImageSource?>>> _testPendingFullParses = new(StringComparer.OrdinalIgnoreCase);

    private readonly object _lifetimeGate = new();
    private readonly HashSet<Task> _ownedTasks = new();
    private readonly CancellationTokenSource _lifetimeCts = new();
    private ImageSource? _defaultArt;
    private volatile bool _disposed;

    private static volatile bool _scrollingActive;
    private static long _perfMediumCalls;
    private static long _perfMediumUiCalls;
    private static long _perfMediumWorkerCalls;
    private static long _perfMediumCacheHits;
    private static long _perfMediumCacheMisses;
    private static long _perfMediumDiskHits;
    private static long _perfMediumThumbnailHits;
    private static long _perfMediumDecodeCount;
    private static long _perfMediumDecodeTicks;
    private static long _perfMediumUiDecodeCount;
    private static long _perfMediumUiDecodeTicks;
    private static long _perfMediumCallTicks;
    private static long _perfMediumUiCallTicks;
    private static AlbumArtUiProbe? _activeUiImageProbe;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentThread();

    [DllImport("kernel32.dll", SetLastError = false)]
    private static extern bool GetThreadTimes(
        IntPtr thread,
        out long creationTime,
        out long exitTime,
        out long kernelTime,
        out long userTime);

    internal static AlbumArtUiProbe? BeginUiImageProbe(string trigger, string mode, string state)
    {
        if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") != "1")
            return null;

        var probe = new AlbumArtUiProbe(trigger, mode, state);
        return Interlocked.CompareExchange(ref _activeUiImageProbe, probe, null) == null
            ? probe
            : null;
    }

    internal static bool EndUiImageProbe(AlbumArtUiProbe probe) =>
        ReferenceEquals(Interlocked.CompareExchange(ref _activeUiImageProbe, null, probe), probe);

    internal static AlbumArtUiProbe.Measurement MeasureUiImage(AlbumArtUiMetric metric)
    {
        var probe = Volatile.Read(ref _activeUiImageProbe);
        return probe != null && Application.Current?.Dispatcher?.CheckAccess() == true
            ? probe.Measure(metric)
            : default;
    }

    private static AlbumArtUiProbe? GetActiveUiImageProbe()
    {
        var probe = Volatile.Read(ref _activeUiImageProbe);
        return Application.Current?.Dispatcher?.CheckAccess() == true ? probe : null;
    }

    internal static bool TryGetCurrentThreadCpuTicks(out long ticks)
    {
        if (GetThreadTimes(GetCurrentThread(), out _, out _, out long kernelTime, out long userTime))
        {
            ticks = kernelTime + userTime;
            return true;
        }

        ticks = 0;
        return false;
    }

    internal static AlbumArtPerfSnapshot GetPerfSnapshot() => new(
        Interlocked.Read(ref _perfMediumCalls),
        Interlocked.Read(ref _perfMediumUiCalls),
        Interlocked.Read(ref _perfMediumWorkerCalls),
        Interlocked.Read(ref _perfMediumCacheHits),
        Interlocked.Read(ref _perfMediumCacheMisses),
        Interlocked.Read(ref _perfMediumDiskHits),
        Interlocked.Read(ref _perfMediumThumbnailHits),
        Interlocked.Read(ref _perfMediumDecodeCount),
        Interlocked.Read(ref _perfMediumDecodeTicks),
        Interlocked.Read(ref _perfMediumUiDecodeCount),
        Interlocked.Read(ref _perfMediumUiDecodeTicks),
        Interlocked.Read(ref _perfMediumCallTicks),
        Interlocked.Read(ref _perfMediumUiCallTicks));

    public static bool ScrollingActive => _scrollingActive;

    public static void SetScrollingActive(bool active)
    {
        _scrollingActive = active;
        Instance?._coordinator.SetScrollingActive(active);
    }

    public static AlbumArtLoader Instance { get; internal set; } = null!;

    public AlbumArtLoader(ILogger<AlbumArtLoader>? logger = null)
        : this(logger, initializeDiskCache: true)
    {
    }

    internal AlbumArtLoader(ILogger<AlbumArtLoader>? logger, bool initializeDiskCache)
    {
        _logger = logger;
        if (ImageLoadingCoordinator.Instance != null)
        {
            _coordinator = ImageLoadingCoordinator.Instance;
            _ownsCoordinator = false;
        }
        else
        {
            _coordinator = new ImageLoadingCoordinator(null, logger);
            _ownsCoordinator = true;
        }

        if (initializeDiskCache)
        {
            AppPaths.EnsureDirectories();
        }
    }

    public ImageLoadingCoordinator Coordinator => _coordinator;

    public void Dispose()
    {
        lock (_lifetimeGate)
        {
            if (_disposed) return;
            _disposed = true;
            _pendingThumbnailImages.Clear();
            _pendingMediumImages.Clear();
            _pendingFullImages.Clear();
        }

        if (ReferenceEquals(Instance, this))
        {
            Instance = null!;
        }

        try { _lifetimeCts.Cancel(); } catch { }

        if (_ownsCoordinator)
        {
            _coordinator.Dispose();
        }

        _lifetimeCts.Dispose();
    }

    public ImageSource? LoadAlbumArt(string? filePath)
        => LoadAlbumArtInternal(filePath, null);

    public ImageSource? LoadAlbumArt(MusicTrack? track)
    {
        if (track == null) return GetDefaultAlbumArt();

        string? resolvedPath = ResolveTrackArtPath(track);
        if (string.IsNullOrWhiteSpace(resolvedPath)) return GetDefaultAlbumArt();

        return LoadAlbumArtInternal(resolvedPath, track);
    }

    private ImageSource? LoadAlbumArtInternal(string? filePath, MusicTrack? track)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return GetDefaultAlbumArt();
        if (_disposed) return GetDefaultAlbumArt();

        // 1. In-memory check
        if (_cache.TryGetValue(filePath, out var direct) && direct != null) return direct;
        var mem = _coordinator.TryGetAlbumArt(track, ImageQuality.Thumbnail)
               ?? _coordinator.TryGetAlbumArt(filePath, ImageQuality.Thumbnail);
        if (mem != null)
        {
            StoreBounded(_cache, _cacheSizes, ref _currentCacheBytes, ThumbnailCacheBudgetBytes, filePath, mem);
            return mem;
        }

        // 2. Return progressive DrawingImage immediately and load asynchronously off UI thread
        var progressive = GetOrCreateProgressiveImage(_pendingThumbnailImages, filePath, GetDefaultAlbumArt());

        EnqueueBackgroundArtLoad(filePath, track, ImageQuality.Thumbnail, _pendingThumbnailImages, progressive);

        return progressive.Image;
    }

    public ImageSource? LoadMediumAlbumArt(MusicTrack? track)
    {
        if (track == null) return GetDefaultAlbumArt();
        string? path = ResolveTrackArtPath(track);
        if (string.IsNullOrWhiteSpace(path)) return GetDefaultAlbumArt();
        return LoadMediumAlbumArtInternal(path, track);
    }

    public ImageSource? LoadMediumAlbumArt(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return GetDefaultAlbumArt();
        return LoadMediumAlbumArtInternal(filePath, null);
    }

    private ImageSource? LoadMediumAlbumArtInternal(string path, MusicTrack? track)
    {
        if (_disposed) return GetDefaultAlbumArt();

        if (_mediumCache.TryGetValue(path, out var direct) && direct != null) return direct;
        var mem = _coordinator.TryGetAlbumArt(track, ImageQuality.Medium)
               ?? _coordinator.TryGetAlbumArt(path, ImageQuality.Medium);
        if (mem != null)
        {
            StoreBounded(_mediumCache, _mediumCacheSizes, ref _currentMediumCacheBytes, MediumCacheBudgetBytes, path, mem);
            return mem;
        }

        // Use thumbnail as placeholder if available
        ImageSource placeholder = _cache.TryGetValue(path, out var thumb) && thumb != null
            ? thumb
            : GetDefaultAlbumArt();

        var progressive = GetOrCreateProgressiveImage(_pendingMediumImages, path, placeholder);

        EnqueueBackgroundArtLoad(path, track, ImageQuality.Medium, _pendingMediumImages, progressive);

        return progressive.Image;
    }

    public ImageSource? LoadFullAlbumArt(MusicTrack? track)
    {
        if (track == null) return GetDefaultAlbumArt();
        string? path = ResolveTrackArtPath(track);
        if (string.IsNullOrWhiteSpace(path)) return GetDefaultAlbumArt();
        return LoadFullAlbumArtInternal(path, track);
    }

    public ImageSource? LoadFullAlbumArt(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return GetDefaultAlbumArt();
        return LoadFullAlbumArtInternal(filePath, null);
    }

    private ImageSource? LoadFullAlbumArtInternal(string path, MusicTrack? track)
    {
        if (_disposed) return GetDefaultAlbumArt();

        if (_fullQualityCache.TryGetValue(path, out var direct) && direct != null) return direct;
        var mem = _coordinator.TryGetAlbumArt(track, ImageQuality.Full)
               ?? _coordinator.TryGetAlbumArt(path, ImageQuality.Full);
        if (mem != null)
        {
            StoreBounded(_fullQualityCache, _fullCacheSizes, ref _currentFullCacheBytes, FullCacheBudgetBytes, path, mem);
            return mem;
        }

        ImageSource placeholder = _mediumCache.TryGetValue(path, out var med) && med != null
            ? med
            : (_cache.TryGetValue(path, out var thumb) && thumb != null ? thumb : GetDefaultAlbumArt());

        var progressive = GetOrCreateProgressiveImage(_pendingFullImages, path, placeholder);

        EnqueueBackgroundArtLoad(path, track, ImageQuality.Full, _pendingFullImages, progressive);

        return progressive.Image;
    }

    private void EnqueueBackgroundArtLoad(
        string path,
        MusicTrack? track,
        ImageQuality quality,
        ConcurrentDictionary<string, ProgressiveArtImage> pendingImages,
        ProgressiveArtImage progressive)
    {
        lock (_lifetimeGate)
        {
            if (_disposed) return;

            Task task = Task.Run(async () =>
            {
                ImageSource? result = null;
                try
                {
                    var priority = _scrollingActive ? ImagePriority.NearScroll : ImagePriority.Visible;
                    result = await _coordinator.LoadAlbumArtAsync(track, path, quality, priority, _lifetimeCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Background album art load failed for {Path}", path);
                }
                finally
                {
                    // ALWAYS clean up pending entry regardless of success/failure/cancellation
                    if (pendingImages.TryRemove(path, out var p) && p != null)
                    {
                        Dispatcher d = p.Dispatcher;
                        if (!d.HasShutdownStarted && !d.HasShutdownFinished)
                        {
                            await d.BeginInvoke(new Action(() =>
                            {
                                try
                                {
                                    if (result != null)
                                        p.SetSource(result);
                                }
                                catch { }
                            }), DispatcherPriority.Background);
                        }
                    }
                }

                if (result != null)
                {
                    if (quality == ImageQuality.Thumbnail)
                        StoreBounded(_cache, _cacheSizes, ref _currentCacheBytes, ThumbnailCacheBudgetBytes, path, result);
                    else if (quality == ImageQuality.Medium)
                        StoreBounded(_mediumCache, _mediumCacheSizes, ref _currentMediumCacheBytes, MediumCacheBudgetBytes, path, result);
                    else if (quality == ImageQuality.Full)
                        StoreBounded(_fullQualityCache, _fullCacheSizes, ref _currentFullCacheBytes, FullCacheBudgetBytes, path, result);

                    // Store derived asset for beneficiaries without re-extracting from audio file
                    if (track != null)
                    {
                        var asset = _assetService.Resolve(track);
                        if (asset != null)
                        {
                            var artQ = quality switch
                            {
                                ImageQuality.Thumbnail => AlbumArtQuality.Thumbnail,
                                ImageQuality.Medium => AlbumArtQuality.Medium,
                                _ => AlbumArtQuality.Full
                            };
                            _assetService.StoreDerived(asset, artQ, result);
                        }
                    }
                }
            });

            _ownedTasks.Add(task);
            _ = task.ContinueWith(t =>
            {
                lock (_lifetimeGate) { _ownedTasks.Remove(t); }
            }, TaskScheduler.Default);
        }
    }

    public ImageSource? TryGetCachedAlbumArt(MusicTrack? track)
    {
        if (track == null) return null;
        string? path = ResolveTrackArtPath(track);
        if (string.IsNullOrWhiteSpace(path)) return null;

        if (_fullQualityCache.TryGetValue(path, out var full) && full != null) return full;
        if (_mediumCache.TryGetValue(path, out var med) && med != null) return med;
        if (_cache.TryGetValue(path, out var thumb) && thumb != null && thumb != _defaultArt) return thumb;

        return _coordinator.TryGetAlbumArt(track, ImageQuality.Full)
            ?? _coordinator.TryGetAlbumArt(track, ImageQuality.Medium)
            ?? _coordinator.TryGetAlbumArt(track, ImageQuality.Thumbnail);
    }

    public string? GetCachedArtFilePath(MusicTrack? track)
    {
        if (track == null) return null;
        // This is called from UI converters - we must avoid blocking.
        // Return cached DB record path if available, otherwise trigger async load.
        string? path = ResolveTrackArtPath(track);
        if (string.IsNullOrWhiteSpace(path)) return null;

        string normPath = SourceIdentity.NormalizePath(path);
        
        // Try to get from DB record without blocking
        var record = _coordinator.DiskCache.GetCacheFilePath(normPath, ImageQuality.Medium);
        if (File.Exists(record)) return record;

        // Trigger async load but return null for now (placeholder will show)
        _ = _coordinator.GetCachedArtFilePathAsync(track, path, _lifetimeCts.Token);
        return null;
    }

    public string? GetCachedArtFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        string normPath = SourceIdentity.NormalizePath(filePath);
        
        var record = _coordinator.DiskCache.GetCacheFilePath(normPath, ImageQuality.Medium);
        if (File.Exists(record)) return record;

        _ = _coordinator.GetCachedArtFilePathAsync(null, filePath, _lifetimeCts.Token);
        return null;
    }

    public Task PreloadTrackArtwork(
        IEnumerable<MusicTrack> tracks,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            var list = tracks.Where(t => t != null && !string.IsNullOrEmpty(t.FilePath)).ToList();
            if (list.Count == 0) return;

            Parallel.ForEach(list, new ParallelOptions
            {
                MaxDegreeOfParallelism = 4,
                CancellationToken = cancellationToken
            }, track =>
            {
                try
                {
                    var art = _coordinator.LoadAlbumArtAsync(track, track.FilePath, ImageQuality.Thumbnail, ImagePriority.Preload, cancellationToken).GetAwaiter().GetResult();
                    if (art != null) StoreBounded(_cache, _cacheSizes, ref _currentCacheBytes, ThumbnailCacheBudgetBytes, track.FilePath, art);
                }
                catch { }
            });
        }, cancellationToken);
    }

    public Task WarmUpArtCacheAsync(
        IEnumerable<MusicTrack> tracks,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var list = tracks.Where(t => t != null && !string.IsNullOrEmpty(t.FilePath)).ToList();
            if (list.Count == 0) return;

            // PreloadTrackArtwork already loads and caches the exact same
            // Thumbnail quality for every track. The old second sequential pass
            // re-requested each one (cache hits only) and doubled the awaits per
            // batch while keeping the warmup task alive longer.
            await PreloadTrackArtwork(list, cancellationToken).ConfigureAwait(false);
        }, cancellationToken);
    }

    public void PreloadArtwork(IEnumerable<string> filePaths)
    {
        ArgumentNullException.ThrowIfNull(filePaths);
        Task.Run(async () =>
        {
            foreach (var path in filePaths)
            {
                if (_lifetimeCts.IsCancellationRequested) break;
                try
                {
                    await _coordinator.LoadAlbumArtAsync(null, path, ImageQuality.Thumbnail, ImagePriority.Preload, _lifetimeCts.Token).ConfigureAwait(false);
                }
                catch { }
            }
        });
    }

    public ImageSource GetDefaultAlbumArt()
    {
        if (_defaultArt != null) return _defaultArt;
        _defaultArt = ImageLoadingCoordinator.CreateDefaultArt();
        return _defaultArt;
    }

    public static ImageSource CreateProgrammedDefaultArt()
        => ImageLoadingCoordinator.CreateDefaultArt();

    public void ClearCache()
    {
        _cache.Clear();
        _mediumCache.Clear();
        _fullQualityCache.Clear();
        _coordinator.MemoryCache.Clear();
    }

    // ── Test harness methods for ImageRequestTests compatibility ───────────────
    internal Task<ImageSource?> EnqueueFullArtParse(string path, Func<Task<ImageSource?>> decodeAsync)
        => EnqueueFullArtParse(path, null, _ => decodeAsync());

    internal Task<ImageSource?> EnqueueFullArtParse(
        string path,
        Func<CancellationToken, Task<ImageSource?>> decodeAsync)
        => EnqueueFullArtParse(path, null, decodeAsync);

    internal Task<ImageSource?> EnqueueFullArtParse(
        string path,
        MusicTrack? track,
        Func<CancellationToken, Task<ImageSource?>> decodeAsync)
    {
        lock (_lifetimeGate)
        {
            if (_disposed) return Task.FromResult<ImageSource?>(null);

            Lazy<Task<ImageSource?>>? pending = null;
            pending = new Lazy<Task<ImageSource?>>(async () =>
            {
                try
                {
                    var result = await decodeAsync(_lifetimeCts.Token).ConfigureAwait(false);
                    if (result != null)
                    {
                        StoreBounded(_fullQualityCache, _fullCacheSizes, ref _currentFullCacheBytes, FullCacheBudgetBytes, path, result);
                        if (track != null)
                        {
                            var asset = _assetService.Resolve(track);
                            if (asset != null)
                            {
                                _assetService.StoreDerived(asset, AlbumArtQuality.Full, result);
                            }
                        }
                    }
                    return result;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Test full art parse failed");
                    return null;
                }
                finally
                {
                    ((ICollection<KeyValuePair<string, Lazy<Task<ImageSource?>>>>)_testPendingFullParses)
                        .Remove(new KeyValuePair<string, Lazy<Task<ImageSource?>>>(path, pending!));
                }
            }, LazyThreadSafetyMode.ExecutionAndPublication);

            return _testPendingFullParses.GetOrAdd(path, pending).Value;
        }
    }

    internal Task<ImageSource?> EnqueueMediumArtParseForTest(
        string path,
        Func<CancellationToken, Task<ImageSource?>> decodeAsync)
    {
        lock (_lifetimeGate)
        {
            if (_disposed) return Task.FromResult<ImageSource?>(null);

            Lazy<Task<ImageSource?>>? pending = null;
            pending = new Lazy<Task<ImageSource?>>(async () =>
            {
                try
                {
                    var result = await decodeAsync(_lifetimeCts.Token).ConfigureAwait(false);
                    if (result != null)
                    {
                        StoreBounded(_mediumCache, _mediumCacheSizes, ref _currentMediumCacheBytes, MediumCacheBudgetBytes, path, result);
                    }
                    return result;
                }
                catch (OperationCanceledException)
                {
                    return null;
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "Test medium art parse failed");
                    return null;
                }
                finally
                {
                    ((ICollection<KeyValuePair<string, Lazy<Task<ImageSource?>>>>)_testPendingMediumParses)
                        .Remove(new KeyValuePair<string, Lazy<Task<ImageSource?>>>(path, pending!));
                }
            }, LazyThreadSafetyMode.ExecutionAndPublication);

            return _testPendingMediumParses.GetOrAdd(path, pending).Value;
        }
    }

    private static string? ResolveTrackArtPath(MusicTrack? track)
        => AlbumArtProvider.ResolveSourcePath(track, null);

    private static ProgressiveArtImage GetOrCreateProgressiveImage(
        ConcurrentDictionary<string, ProgressiveArtImage> presentations,
        string path,
        ImageSource placeholder)
    {
        Dispatcher dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        return presentations.GetOrAdd(path, _ => new ProgressiveArtImage(placeholder, dispatcher));
    }
}
