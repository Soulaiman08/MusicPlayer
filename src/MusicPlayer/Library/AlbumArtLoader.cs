using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Database;
using MusicPlayer.Models;
using MusicPlayer.Utilities;
using Microsoft.Extensions.Logging;

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

public class AlbumArtLoader
{
    private readonly DatabaseService _db;
    private readonly ILogger<AlbumArtLoader>? _logger;
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new();
    private readonly ConcurrentDictionary<string, ImageSource?> _mediumCache = new(); // mini-player quality
    private readonly ConcurrentDictionary<string, ImageSource?> _fullQualityCache = new();
    private readonly ConcurrentDictionary<string, byte> _pendingParses = new();
    private readonly ConcurrentDictionary<string, byte> _pendingMediumParses = new();
    public event Action<string>? MediumArtReady;
    private readonly ConcurrentQueue<string> _preloadQueue = new();
    private ImageSource? _defaultArt;
    private volatile bool _preloading;

    // Scroll-layout gate (set by the smooth-scroll engine): while frantic mouse
    // wheel activity is animating the list rows, any cache-miss returns the
    // frozen fallback instantly — no file IO, no TagLib tag iteration, no cache
    // growth/trim on the UI thread. The warm-ups and background parses simply
    // defer until scrolling settles.
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
        return probe != null && System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true
            ? probe.Measure(metric)
            : default;
    }

    private static AlbumArtUiProbe? GetActiveUiImageProbe()
    {
        var probe = Volatile.Read(ref _activeUiImageProbe);
        return System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true ? probe : null;
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

    private readonly struct PerfMeasurement : IDisposable
    {
        private readonly bool _enabled;
        private readonly bool _isDecode;
        private readonly bool _isUiThread;
        private readonly long _started;

        public PerfMeasurement(bool isDecode)
        {
            _enabled = Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1";
            _isDecode = isDecode;
            _isUiThread = _enabled && System.Windows.Application.Current?.Dispatcher?.CheckAccess() == true;
            _started = _enabled ? System.Diagnostics.Stopwatch.GetTimestamp() : 0L;

            if (!_enabled) return;
            if (isDecode)
            {
                Interlocked.Increment(ref _perfMediumDecodeCount);
                if (_isUiThread) Interlocked.Increment(ref _perfMediumUiDecodeCount);
            }
            else
            {
                Interlocked.Increment(ref _perfMediumCalls);
                if (_isUiThread) Interlocked.Increment(ref _perfMediumUiCalls);
                else Interlocked.Increment(ref _perfMediumWorkerCalls);
            }
        }

        public void Dispose()
        {
            if (!_enabled) return;
            long elapsed = System.Diagnostics.Stopwatch.GetTimestamp() - _started;
            if (_isDecode)
            {
                Interlocked.Add(ref _perfMediumDecodeTicks, elapsed);
                if (_isUiThread) Interlocked.Add(ref _perfMediumUiDecodeTicks, elapsed);
            }
            else
            {
                Interlocked.Add(ref _perfMediumCallTicks, elapsed);
                if (_isUiThread) Interlocked.Add(ref _perfMediumUiCallTicks, elapsed);
            }
        }
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

    private static void RecordMediumCacheHit() { if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1") Interlocked.Increment(ref _perfMediumCacheHits); }
    private static void RecordMediumCacheMiss() { if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1") Interlocked.Increment(ref _perfMediumCacheMisses); }
    private static void RecordMediumDiskHit() { if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1") Interlocked.Increment(ref _perfMediumDiskHits); }
    private static void RecordMediumThumbnailHit() { if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1") Interlocked.Increment(ref _perfMediumThumbnailHits); }

    public static bool ScrollingActive => _scrollingActive;

    public static void SetScrollingActive(bool active) => _scrollingActive = active;

    private const int MaxCacheSize = 2500;
    private const int DecodePixelSize = 80;   // list thumbnails: 2× the ~40px display slot
    private const int MediumDecodeSize = 220; // album cards and mini-player: crisp and fast at 150px display slot
    private const int ThumbEncodeSize = 512;
    private const int MaxThumbFileBytes = 1024 * 1024;
    private const int MaxMediumCacheSize = 500; // Increased to 500 so albums view does not aggressively evict

    // Caps simultaneous background TagLib decodes so the post-scroll catch-up
    // backlog (measured: dozens of parses flushing at once after a wheel burst
    // cleared, spiking CPU to ~64-76%) is serialized instead of flooding the
    // thread pool with parallel decodes.
    private static readonly SemaphoreSlim _decodeGate = new(2, 2);

    public AlbumArtLoader(DatabaseService db, ILogger<AlbumArtLoader>? logger = null)
    {
        _db = db;
        _logger = logger;
        AppPaths.EnsureDirectories();
        PurgeOldDiskCache();
    }

    public static AlbumArtLoader Instance { get; internal set; } = null!;

    private static void PurgeOldDiskCache()
    {
        try
        {
            var dir = AppPaths.AlbumArtCachePath;
            if (!Directory.Exists(dir)) { Directory.CreateDirectory(dir); return; }
            var flag = Path.Combine(dir, ".v3");
            if (File.Exists(flag)) return;
            Directory.Delete(dir, true);
            Directory.CreateDirectory(dir);
            File.WriteAllText(flag, "ok");
        }
        catch { }
    }

    public ImageSource? LoadAlbumArt(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return GetDefaultAlbumArt();

        var uiProbe = GetActiveUiImageProbe();
        bool cacheHit;
        ImageSource? cached;
        using (MeasureUiImage(AlbumArtUiMetric.MemoryLookup))
            cacheHit = _cache.TryGetValue(filePath, out cached) && cached != null;

        uiProbe?.RecordMemoryResult(cacheHit);
        if (cacheHit)
            return cached;

        // While scrolling is active, return placeholder immediately — never block the UI thread on disk IO during scroll!
        if (_scrollingActive)
        {
            uiProbe?.RecordScrollSkip();
            return GetDefaultAlbumArt();
        }

        using var diskPathMeasurement = MeasureUiImage(AlbumArtUiMetric.DiskPath);
        try
        {
            var hash = GetMd5Hash(filePath);
            var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
            var cachedPngPath   = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.png");

            var existing = File.Exists(cachedImagePath) ? cachedImagePath : (File.Exists(cachedPngPath) ? cachedPngPath : null);
            if (existing != null)
            {
                ImageSource? img;
                using (MeasureUiImage(AlbumArtUiMetric.Decode))
                    img = LoadThumbnailFromFile(existing);
                uiProbe?.RecordDecodeResult(img != null);
                if (img != null)
                {
                    uiProbe?.RecordDiskResult(true);
                    _cache[filePath] = img;
                    TrimCache();
                    return img;
                }
            }

            // If not found in disk cache, NEVER block the UI thread parsing with TagLib!
            // Queue a background parse and return default placeholder immediately.
            EnqueueFullArtParse(filePath);
        }
        catch { }

        uiProbe?.RecordDiskResult(false);
        return GetDefaultAlbumArt();
    }

    public ImageSource? LoadAlbumArt(MusicTrack? track)
    {
        if (track == null) return GetDefaultAlbumArt();
        if (!string.IsNullOrEmpty(track.AlbumArtPath))
            return LoadAlbumArt(track.AlbumArtPath);
        return LoadAlbumArt(track.FilePath);
    }

    /// <summary>
    /// Returns the on-disk cached artwork file ({hash}.jpg or {hash}.png) for a track,
    /// extracting it directly from the audio tags or sidecar files on first use.
    /// Thread-safe and executable on MTA background threads. Used by Windows SMTC overlay.
    /// </summary>
    public string? GetCachedArtFilePath(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return null;
        try
        {
            var hash = GetMd5Hash(filePath);
            var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
            var cachedPngPath   = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.png");
            var metaPath        = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.meta");

            if (!InvalidateStaleCache(filePath, cachedImagePath, metaPath) && File.Exists(cachedImagePath))
                return cachedImagePath;

            if (!InvalidateStaleCache(filePath, cachedPngPath, metaPath) && File.Exists(cachedPngPath))
                return cachedPngPath;

            // Extract embedded picture directly from audio file
            if (File.Exists(filePath))
            {
                using var tagFile = TagLib.File.Create(filePath);
                foreach (var picture in tagFile.Tag.Pictures)
                {
                    if (picture.Data.Data == null || picture.Data.Data.Length == 0) continue;
                    long sourceTicks = File.GetLastWriteTimeUtc(filePath).Ticks;

                    bool isPng = picture.Data.Data.Length >= 4 &&
                                 picture.Data.Data[0] == 0x89 &&
                                 picture.Data.Data[1] == 0x50 &&
                                 picture.Data.Data[2] == 0x4E &&
                                 picture.Data.Data[3] == 0x47;
                    var targetPath = isPng ? cachedPngPath : cachedImagePath;

                    File.WriteAllBytes(targetPath, picture.Data.Data);
                    try { File.WriteAllText(metaPath, sourceTicks.ToString()); } catch { }

                    if (File.Exists(targetPath))
                        return targetPath;
                }
            }

            // Check for sidecar image in same directory (cover.jpg, folder.jpg)
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                foreach (var name in new[] { "cover.jpg", "folder.jpg", "front.jpg", "album.jpg", "cover.png", "folder.png" })
                {
                    var sidecar = Path.Combine(dir, name);
                    if (File.Exists(sidecar)) return sidecar;
                }
            }
        }
        catch (Exception ex)
        {
            LogHelper.LogError("GetCachedArtFilePath", ex);
        }
        return null;
    }

    public ImageSource? LoadFullAlbumArt(MusicTrack? track)
    {
        if (track == null) return GetDefaultAlbumArt();
        var path = track.FilePath;
        if (string.IsNullOrEmpty(path)) return GetDefaultAlbumArt();

        if (_fullQualityCache.TryGetValue(path, out var cached) && cached != null)
            return cached;

        var fullArt = DecodeFirstValidPicture(track);
        if (fullArt != null)
        {
            _fullQualityCache[path] = fullArt;
            TrimCache();
            return fullArt;
        }

        // Fallback: standard thumbnail if no embedded or dedicated full-res art exists
        var normalArt = LoadAlbumArt(track);
        if (normalArt != null && normalArt != _defaultArt)
        {
            _fullQualityCache[path] = normalArt;
            TrimCache();
            return normalArt;
        }

        return GetDefaultAlbumArt();
    }

    // ── Medium quality (380px) — Mini Player ──────────────────────────────────
    public ImageSource? LoadMediumAlbumArt(MusicTrack? track)
    {
        using var perfCall = new PerfMeasurement(isDecode: false);
        if (track == null) return GetDefaultAlbumArt();
        var path = !string.IsNullOrEmpty(track.AlbumArtPath) ? track.AlbumArtPath : track.FilePath;
        if (string.IsNullOrEmpty(path)) return GetDefaultAlbumArt();

        if (_mediumCache.TryGetValue(path, out var cached) && cached != null)
        {
            RecordMediumCacheHit();
            return cached;
        }
        RecordMediumCacheMiss();

        // 1. Try disk cache first at MediumDecodeSize (220px)
        try
        {
            var hash = GetMd5Hash(path);
            var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
            var cachedPngPath   = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.png");

            var existingPath = File.Exists(cachedImagePath) ? cachedImagePath : (File.Exists(cachedPngPath) ? cachedPngPath : null);
            if (existingPath != null)
            {
                ImageSource? img;
                using (new PerfMeasurement(isDecode: true))
                    img = LoadSizedImageFromFile(existingPath, MediumDecodeSize);
                if (img != null)
                {
                    RecordMediumDiskHit();
                    _mediumCache[path] = img;
                    TrimCache();
                    return img;
                }
            }
        }
        catch { }

        // If thumbnail cache already has it in memory, use that instantly
        if (_cache.TryGetValue(path, out var thumb) && thumb != null && thumb != _defaultArt)
        {
            RecordMediumThumbnailHit();
            _mediumCache[path] = thumb;
            return thumb;
        }

        if (_scrollingActive)
            return GetDefaultAlbumArt();

        // Never block the caller with TagLib/File IO. Defer the decode to a bounded
        // background worker and show the placeholder now.
        EnqueueMediumArtParse(path, track);
        return GetDefaultAlbumArt();

    }

    public ImageSource? LoadMediumAlbumArt(string? filePath)
    {
        using var perfCall = new PerfMeasurement(isDecode: false);
        if (string.IsNullOrWhiteSpace(filePath)) return GetDefaultAlbumArt();

        if (_mediumCache.TryGetValue(filePath, out var cached) && cached != null)
        {
            RecordMediumCacheHit();
            return cached;
        }
        RecordMediumCacheMiss();

        // Try loading from disk cache at medium decode size
        try
        {
            var hash = GetMd5Hash(filePath);
            var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
            var cachedPngPath   = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.png");

            var existingPath = File.Exists(cachedImagePath) ? cachedImagePath : (File.Exists(cachedPngPath) ? cachedPngPath : null);
            if (existingPath != null)
            {
                ImageSource? img;
                using (new PerfMeasurement(isDecode: true))
                    img = LoadSizedImageFromFile(existingPath, MediumDecodeSize);
                if (img != null)
                {
                    RecordMediumDiskHit();
                    _mediumCache[filePath] = img;
                    TrimCache();
                    return img;
                }
            }
        }
        catch { }

        // If thumbnail cache already has it in memory, use that instantly
        if (_cache.TryGetValue(filePath, out var thumb) && thumb != null && thumb != _defaultArt)
        {
            RecordMediumThumbnailHit();
            _mediumCache[filePath] = thumb;
            return thumb;
        }

        if (_scrollingActive)
            return GetDefaultAlbumArt();

        EnqueueMediumArtParse(filePath, null);
        return GetDefaultAlbumArt();

    }

    public ImageSource? LoadFullAlbumArt(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return GetDefaultAlbumArt();
        if (_fullQualityCache.TryGetValue(filePath, out var cached) && cached != null)
            return cached;

        var dummyTrack = new MusicTrack { FilePath = filePath };
        var fullArt = DecodeFirstValidPicture(dummyTrack);
        if (fullArt != null)
        {
            _fullQualityCache[filePath] = fullArt;
            TrimCache();
            return fullArt;
        }

        var normalArt = LoadAlbumArt(filePath);
        if (normalArt != null && normalArt != _defaultArt)
        {
            _fullQualityCache[filePath] = normalArt;
            TrimCache();
            return normalArt;
        }

        return GetDefaultAlbumArt();
    }

    private ImageSource? LoadFullAlbumArtCore(MusicTrack track)
    {
        return DecodeFirstValidPicture(track);
    }

    // Runs the TagLib Pictures scan + decode + Freeze() on a worker thread.
    // Freezing on the background thread makes the result safe to hand to the UI
    // virtualizer without any cross-thread marshalling cost.
    private void EnqueueFullArtParse(string path)
    {
        if (_pendingParses.ContainsKey(path)) return;
        _pendingParses[path] = 0;

        Task.Run(async () =>
        {
            try
            {
                await _decodeGate.WaitAsync();
                try
                {
                    // Re-check memory first — the thumbnail may have been filled in
                    // by the warm-up while this task was queued.
                    if (_fullQualityCache.TryGetValue(path, out var done) && done != null)
                        return;
                    if (_cache.TryGetValue(path, out var thumb) && thumb != null)
                    {
                        _fullQualityCache.TryAdd(path, thumb);
                        return;
                    }

                    var dummyTrack = new MusicTrack { FilePath = path };
                    var img = DecodeFirstValidPicture(dummyTrack);
                    if (img != null)
                        _fullQualityCache[path] = img;
                }
                finally { _decodeGate.Release(); }
            }
            catch { }
            finally
            {
                _pendingParses.TryRemove(path, out _);
            }
        });
    }

    private void NotifyMediumArtReady(string path)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher != null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(() => MediumArtReady?.Invoke(path)));
        }
        else
        {
            MediumArtReady?.Invoke(path);
        }
    }

    private void EnqueueMediumArtParse(string path, MusicTrack? track)
    {
        if (_pendingMediumParses.ContainsKey(path)) return;
        _pendingMediumParses[path] = 0;

        Task.Run(async () =>
        {
            try
            {
                await _decodeGate.WaitAsync();
                try
                {
                    if (_mediumCache.TryGetValue(path, out var done) && done != null)
                        return;

                    var sourceTrack = track ?? new MusicTrack { FilePath = path };
                    var img = DecodeMediumValidPicture(sourceTrack);
                    if (img != null)
                    {
                        _mediumCache[path] = img;
                        _cache.TryAdd(path, img);
                        NotifyMediumArtReady(path);
                    }
                }
                finally { _decodeGate.Release(); }
            }
            catch { }
            finally
            {
                _pendingMediumParses.TryRemove(path, out _);
            }
        });
    }

    private ImageSource? DecodeFirstValidPicture(MusicTrack track)
    {
        try
        {
            var path = track.FilePath;
            if (!string.IsNullOrEmpty(track.AlbumArtPath) && File.Exists(track.AlbumArtPath))
            {
                var img = LoadFullQualityFromFile(track.AlbumArtPath);
                if (img != null) return img;
            }

            using var tagFile = TagLib.File.Create(path);

            // Some exporters (e.g. AI tooling) store a non-image C2PA content
            // credential manifest as an ID3v2 picture frame BEFORE the real
            // cover. Iterate every frame and decode until a real image is found
            // instead of assuming Pictures[0] is always the artwork.
            foreach (var picture in tagFile.Tag.Pictures)
            {
                if (picture.Data.Data == null || picture.Data.Data.Length == 0) continue;
                using var ms = new MemoryStream(picture.Data.Data);
                var img = LoadFullQualityFromStream(ms);
                if (img != null) return img;
            }
        }
        catch { }
        return null;
    }

    private ImageSource? DecodeMediumValidPicture(MusicTrack track)
    {
        try
        {
            var path = track.FilePath;
            if (!string.IsNullOrEmpty(track.AlbumArtPath) && File.Exists(track.AlbumArtPath))
            {
                var img = LoadSizedImageFromFile(track.AlbumArtPath, MediumDecodeSize);
                if (img != null) return img;
            }

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

            using var tagFile = TagLib.File.Create(path);

            foreach (var picture in tagFile.Tag.Pictures)
            {
                if (picture.Data.Data == null || picture.Data.Data.Length == 0) continue;
                using var ms = new MemoryStream(picture.Data.Data);
                var img = LoadSizedImageFromStream(ms, MediumDecodeSize);
                if (img != null)
                {
                    try
                    {
                        var hash = GetMd5Hash(path);
                        var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
                        if (!File.Exists(cachedImagePath))
                        {
                            long sourceTicks = File.GetLastWriteTimeUtc(path).Ticks;
                            SaveThumbnail(picture.Data.Data, cachedImagePath, sourceTicks);
                        }
                    }
                    catch { }
                    return img;
                }
            }
        }
        catch { }
        return null;
    }

    private static BitmapImage? LoadFullQualityFromFile(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return LoadFullQualityFromStream(ms);
        }
        catch { return null; }
    }

    private static BitmapImage? LoadFullQualityFromStream(MemoryStream ms)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            // Archive presentation: the Full Player frame is a fixed square
            // (Viewbox Uniform, max 560). Every full-quality source is decoded
            // to 1024x1024 so all covers fill the frame identically regardless
            // of embedded size/aspect. Full path only — list thumbnails keep
            // their own 256px pipeline.
            image.DecodePixelWidth = 1024;
            image.DecodePixelHeight = 1024;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    public ImageSource GetDefaultAlbumArt()
    {
        if (_defaultArt != null) return _defaultArt;

        try
        {
            _defaultArt = CreateProgrammedDefaultArt();
            return _defaultArt;
        }
        catch
        {
            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(
                    new SolidColorBrush(Color.FromRgb(0x18, 0x1B, 0x22)),
                    null,
                    new System.Windows.Rect(0, 0, 120, 120));
                context.DrawEllipse(
                    new SolidColorBrush(Color.FromRgb(0x49, 0x95, 0xF3)),
                    null,
                    new System.Windows.Point(60, 60), 36, 36);
            }
            var rtb = new RenderTargetBitmap(120, 120, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);
            rtb.Freeze();
            _defaultArt = rtb;
            return _defaultArt;
        }
    }

    public static ImageSource CreateProgrammedDefaultArt()
    {
        var group = new DrawingGroup();

        // 1. Background Rounded Rect (256x256 — half size for performance)
        var bgBrush = new LinearGradientBrush(
            Color.FromRgb(0x18, 0x1B, 0x22),
            Color.FromRgb(0x0E, 0x10, 0x14),
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 1));
        var bgGeom = new RectangleGeometry(new System.Windows.Rect(0, 0, 256, 256), 14, 14);
        group.Children.Add(new GeometryDrawing(bgBrush, null, bgGeom));

        // 2. Subtle Radial Glow in Center
        var glowBrush = new RadialGradientBrush(
            Color.FromArgb(0x35, 0x49, 0x95, 0xF3),
            Color.FromArgb(0x00, 0x0E, 0x10, 0x14))
        {
            Center = new System.Windows.Point(0.5, 0.5),
            GradientOrigin = new System.Windows.Point(0.5, 0.5),
            RadiusX = 0.55,
            RadiusY = 0.55
        };
        group.Children.Add(new GeometryDrawing(glowBrush, null, new RectangleGeometry(new System.Windows.Rect(0, 0, 256, 256))));

        // 3. Stylized Turntable / Vinyl Ring
        var discBrush = new LinearGradientBrush(
            Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF),
            new System.Windows.Point(0, 0),
            new System.Windows.Point(0, 1));
        var discPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
        group.Children.Add(new GeometryDrawing(discBrush, discPen, new EllipseGeometry(new System.Windows.Point(128, 128), 85, 85)));

        // 4. Subtle Concentric Vinyl Grooves
        var ringPen1 = new Pen(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), 0.75);
        group.Children.Add(new GeometryDrawing(null, ringPen1, new EllipseGeometry(new System.Windows.Point(128, 128), 67.5, 67.5)));

        var ringPen2 = new Pen(new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)), 0.5);
        group.Children.Add(new GeometryDrawing(null, ringPen2, new EllipseGeometry(new System.Windows.Point(128, 128), 50, 50)));

        // 5. Note Icon via DrawingGroup Transform (scaled for 256x256)
        var rawGeom = Geometry.Parse("M18.85,2.00 L9.54,3.74 L8.76,4.34 L8.64,17.20 L7.44,16.83 L6.35,16.83 L5.39,17.08 L4.13,17.98 L3.65,18.94 L3.65,19.84 L4.13,20.86 L4.91,21.52 L6.41,22.00 L7.32,22.00 L8.64,21.64 L9.72,20.74 L10.08,19.96 L10.14,9.09 L10.62,8.43 L18.07,6.80 L18.79,7.05 L18.85,15.03 L17.83,14.67 L16.38,14.67 L15.00,15.21 L14.04,16.29 L13.86,17.62 L14.16,18.40 L14.82,19.12 L16.38,19.78 L17.29,19.84 L18.67,19.54 L19.33,19.18 L20.05,18.40 L20.35,17.62 L20.35,2.96 L19.93,2.24 L19.45,2.00 L18.91,2.00 Z");
        var bounds = rawGeom.Bounds;

        double targetSize = 65.0;
        double scale = targetSize / Math.Max(bounds.Width, bounds.Height);
        double offsetX = 128 - (bounds.X + bounds.Width / 2) * scale;
        double offsetY = 128 - (bounds.Y + bounds.Height / 2) * scale;

        var noteBrush = new LinearGradientBrush(
            Color.FromRgb(0x49, 0x95, 0xF3),
            Color.FromRgb(0x6A, 0xB3, 0xFF),
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 1));

        var noteGroup = new DrawingGroup();
        var tg = new TransformGroup();
        tg.Children.Add(new ScaleTransform(scale, scale));
        tg.Children.Add(new TranslateTransform(offsetX, offsetY));
        noteGroup.Transform = tg;
        noteGroup.Children.Add(new GeometryDrawing(noteBrush, null, rawGeom));
        group.Children.Add(noteGroup);

        var drawingImage = new DrawingImage(group);
        drawingImage.Freeze();
        return drawingImage;
    }

    public void PreloadArtwork(IEnumerable<string> filePaths)
    {
        if (_preloading) return;
        _preloading = true;

        Task.Run(() =>
        {
            try
            {
                foreach (var path in filePaths)
                {
                    if (_cache.ContainsKey(path)) continue;
                    if (_scrollingActive) return; // defer CPU work until layout settles
                    try
                    {
                        var art = LoadAlbumArtCore(path);
                        if (art != null && art != _defaultArt)
                            _cache[path] = art;
                    }
                    catch { }
                }
            }
            finally
            {
                _preloading = false;
            }
        });
    }

    public Task PreloadTrackArtwork(
        IEnumerable<MusicTrack> tracks,
        CancellationToken cancellationToken = default)
    {
        // Lightweight: checks disk cache in parallel. Loads instantly into memory.
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
                if (_cache.ContainsKey(track.FilePath)) return;
                try
                {
                    var art = LoadAlbumArtCore(track.FilePath);
                    if (art != null && art != _defaultArt)
                        _cache[track.FilePath] = art;
                }
                catch { }
            });
        });
    }

    // Background warm-up: loads disk cache in parallel, then extracts missing with TagLib.
    public Task WarmUpArtCacheAsync(
        IEnumerable<MusicTrack> tracks,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(async () =>
        {
            var list = tracks.Where(t => t != null && !string.IsNullOrEmpty(t.FilePath)).ToList();
            if (list.Count == 0) return;

            // Phase 1: fast parallel memory load of already-cached disk thumbnails
            await PreloadTrackArtwork(list, cancellationToken);

            // Phase 2: any tracks without a disk thumbnail can be extracted via TagLib in the background
            foreach (var track in list)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_cache.ContainsKey(track.FilePath)) continue;
                try { WarmUpArtCore(track.FilePath); } catch { }
            }
        }, cancellationToken);
    }

    private ImageSource? LoadAlbumArtCore(string filePath)
    {
        // Lightweight: only checks disk cache. No TagLib. Safe for any thread.
        try
        {
            var hash = GetMd5Hash(filePath);
            var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
            var metaPath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.meta");
            if (InvalidateStaleCache(filePath, cachedImagePath, metaPath))
            {
                _cache.TryRemove(filePath, out _);
            }
            else if (File.Exists(cachedImagePath))
            {
                var img = LoadThumbnailFromFile(cachedImagePath);
                if (img != null) return img;
            }
        }
        catch { }
        return GetDefaultAlbumArt();
    }

    // Heavy path: TagLib parse + disk-cache write. Called ONLY from the
    // background warm-up AFTER the UI is visible — never from the preload
    // or any UI-thread path.
    private ImageSource? WarmUpArtCore(string filePath)
    {
        try
        {
            // ── CPU saver: in-memory hit → zero file read, zero TagLib loop ──
            if (_cache.TryGetValue(filePath, out var cached) && cached != null)
                return cached;

            var hash = GetMd5Hash(filePath);
            var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
            var cachedPngPath   = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.png");
            var metaPath        = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.meta");
            long sourceTicks = 0;
            try { if (File.Exists(filePath)) sourceTicks = File.GetLastWriteTimeUtc(filePath).Ticks; } catch { }

            var existingPath = File.Exists(cachedImagePath) ? cachedImagePath : (File.Exists(cachedPngPath) ? cachedPngPath : null);
            if (existingPath != null && !InvalidateStaleCache(filePath, existingPath, metaPath))
            {
                var img = LoadThumbnailFromFile(existingPath);
                if (img != null)
                {
                    _cache[filePath] = img;
                    return img;
                }
            }

            // If the thumbnail is not in disk cache yet, and scrolling is active, defer heavy extraction
            if (_scrollingActive)
                return GetDefaultAlbumArt();

            if (File.Exists(filePath))
            {
                using var tagFile = TagLib.File.Create(filePath);

                // See LoadFullAlbumArtCore: skip non-image frames (e.g. C2PA
                // argument manifests) and decode every frame until the real
                // cover is found.
                foreach (var picture in tagFile.Tag.Pictures)
                {
                    if (picture.Data.Data == null || picture.Data.Data.Length == 0) continue;
                    var thumb = SaveThumbnail(picture.Data.Data, cachedImagePath, sourceTicks);
                    if (thumb != null)
                    {
                        _cache[filePath] = thumb;
                        return thumb;
                    }
                }
            }
        }
        catch { }

        return GetDefaultAlbumArt();
    }

    // Thread-safe cover caching: writes raw bytes directly without any
    // STA / RenderTargetBitmap threading requirements, and returns a thumbnail.
    private static ImageSource? SaveThumbnail(byte[] sourceBytes, string destPath, long sourceLastWriteTicks = 0)
    {
        try
        {
            File.WriteAllBytes(destPath, sourceBytes);
            if (sourceLastWriteTicks != 0)
            {
                var metaPath = Path.ChangeExtension(destPath, ".meta");
                try { File.WriteAllText(metaPath, sourceLastWriteTicks.ToString()); } catch { }
            }

            return LoadThumbnailFromBytes(sourceBytes);
        }
        catch { return null; }
    }

    private static BitmapImage? LoadThumbnailFromFile(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return LoadThumbnailFromBytes(bytes);
        }
        catch { return null; }
    }

    private static BitmapImage? LoadThumbnailFromBytes(byte[] bytes)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            return LoadThumbnailFromStream(ms);
        }
        catch { return null; }
    }

    private static BitmapImage? LoadThumbnailFromStream(MemoryStream ms)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.DecodePixelWidth = DecodePixelSize;
            image.DecodePixelHeight = DecodePixelSize;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private static BitmapImage? LoadSizedImageFromStream(MemoryStream ms, int decodeSize)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.DecodePixelWidth = decodeSize;
            image.DecodePixelHeight = decodeSize;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    private static BitmapImage? LoadSizedImageFromFile(string path, int decodeSize)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            using var ms = new MemoryStream(bytes);
            return LoadSizedImageFromStream(ms, decodeSize);
        }
        catch { return null; }
    }

    private void TrimCache()
    {
        if (_cache.Count > MaxCacheSize)
        {
            int toRemove = _cache.Count - MaxCacheSize + 100;
            int removed = 0;
            foreach (var key in _cache.Keys)
            {
                if (_cache.TryRemove(key, out _))
                {
                    if (++removed >= toRemove) break;
                }
            }
        }

        if (_mediumCache.Count > MaxMediumCacheSize)
        {
            int toRemove = _mediumCache.Count - MaxMediumCacheSize + 50;
            int removed = 0;
            foreach (var key in _mediumCache.Keys)
            {
                if (_mediumCache.TryRemove(key, out _))
                {
                    if (++removed >= toRemove) break;
                }
            }
        }

        while (_fullQualityCache.Count > 12)
        {
            var oldest = _fullQualityCache.Keys.FirstOrDefault();
            if (oldest != null) _fullQualityCache.TryRemove(oldest, out _);
            else break;
        }
    }

    private static readonly ConcurrentDictionary<string, string> _hashCache = new(StringComparer.OrdinalIgnoreCase);

    private static string GetMd5Hash(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;
        if (_hashCache.TryGetValue(input, out var cached)) return cached;

        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder(32);
        for (int i = 0; i < bytes.Length; i++) sb.Append(bytes[i].ToString("x2"));
        var hash = sb.ToString();
        _hashCache.TryAdd(input, hash);
        return hash;
    }

    private static bool InvalidateStaleCache(string sourcePath, string cachedImagePath, string? metaPath)
    {
        try
        {
            if (!File.Exists(sourcePath)) return false;
            if (string.IsNullOrEmpty(metaPath)) return false;
            var sourceLastWrite = File.GetLastWriteTimeUtc(sourcePath).Ticks;
            if (File.Exists(metaPath) && File.Exists(cachedImagePath))
            {
                var metaText = File.ReadAllText(metaPath).Trim();
                if (long.TryParse(metaText, out var storedTicks) && storedTicks == sourceLastWrite)
                    return false;
            }
            if (File.Exists(metaPath))
            {
                try { File.Delete(cachedImagePath); } catch { }
                try { File.Delete(metaPath); } catch { }
                return true;
            }
            return false;
        }
        catch { return false; }
    }

    public void ClearCache()
    {
        _cache.Clear();
        _mediumCache.Clear();
        _fullQualityCache.Clear();
        try
        {
            if (Directory.Exists(AppPaths.AlbumArtCachePath))
                Directory.Delete(AppPaths.AlbumArtCachePath, true);
            Directory.CreateDirectory(AppPaths.AlbumArtCachePath);
        }
        catch { }
    }
}
