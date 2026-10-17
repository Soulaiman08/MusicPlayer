using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using MusicPlayer.Database;
using MusicPlayer.Models;
using MusicPlayer.Utilities;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Library;

public class AlbumArtLoader
{
    private readonly DatabaseService _db;
    private readonly ILogger<AlbumArtLoader>? _logger;
    private readonly ConcurrentDictionary<string, ImageSource?> _cache = new();
    private readonly ConcurrentDictionary<string, ImageSource?> _mediumCache = new(); // mini-player quality
    private readonly ConcurrentDictionary<string, ImageSource?> _fullQualityCache = new();
    private readonly ConcurrentDictionary<string, byte> _pendingParses = new();
    private readonly ConcurrentQueue<string> _preloadQueue = new();
    private ImageSource? _defaultArt;
    private volatile bool _preloading;

    // Scroll-layout gate (set by the smooth-scroll engine): while frantic mouse
    // wheel activity is animating the list rows, any cache-miss returns the
    // frozen fallback instantly — no file IO, no TagLib tag iteration, no cache
    // growth/trim on the UI thread. The warm-ups and background parses simply
    // defer until scrolling settles.
    private static volatile bool _scrollingActive;

    public static bool ScrollingActive => _scrollingActive;

    public static void SetScrollingActive(bool active) => _scrollingActive = active;

    private const int MaxCacheSize = 250;
    private const int DecodePixelSize = 80;   // list thumbnails: 2× the ~40px display slot
    private const int MediumDecodeSize = 220; // album cards and mini-player: crisp and fast at 150px display slot
    private const int ThumbEncodeSize = 512;
    private const int MaxThumbFileBytes = 1024 * 1024;
    private const int MaxMediumCacheSize = 150; // Increased to 150 so albums view does not aggressively evict

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

        if (_cache.TryGetValue(filePath, out var cached) && cached != null)
            return cached;

        if (_scrollingActive)
            return GetDefaultAlbumArt();

        try
        {
            var hash = GetMd5Hash(filePath);
            var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
            var metaPath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.meta");

            if (InvalidateStaleCache(filePath, cachedImagePath, metaPath))
                _cache.TryRemove(filePath, out _);
            else if (File.Exists(cachedImagePath))
            {
                var img = LoadThumbnailFromFile(cachedImagePath);
                if (img != null)
                {
                    _cache[filePath] = img;
                    TrimCache();
                    return img;
                }
            }

            // Do NOT do TagLib I/O on UI thread — return default art.
            // WarmUpArtCacheAsync (background) populates the disk cache.
        }
        catch { }

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
        if (track == null) return GetDefaultAlbumArt();
        var path = !string.IsNullOrEmpty(track.AlbumArtPath) ? track.AlbumArtPath : track.FilePath;
        if (string.IsNullOrEmpty(path)) return GetDefaultAlbumArt();

        if (_mediumCache.TryGetValue(path, out var cached) && cached != null)
            return cached;

        if (_scrollingActive)
            return GetDefaultAlbumArt();

        // 1. Try disk cache first at MediumDecodeSize (380px)
        try
        {
            var hash = GetMd5Hash(path);
            var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
            var cachedPngPath   = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.png");
            var metaPath        = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.meta");

            var existingPath = File.Exists(cachedImagePath) ? cachedImagePath : (File.Exists(cachedPngPath) ? cachedPngPath : null);
            if (existingPath != null && !InvalidateStaleCache(path, existingPath, metaPath))
            {
                var img = LoadSizedImageFromFile(existingPath, MediumDecodeSize);
                if (img != null)
                {
                    _mediumCache[path] = img;
                    TrimCache();
                    return img;
                }
            }
        }
        catch { }

        // 2. If disk cache miss or not yet created, decode from track at medium size
        var mediumArt = DecodeMediumValidPicture(track);
        if (mediumArt != null)
        {
            _mediumCache[path] = mediumArt;
            TrimCache();
            return mediumArt;
        }

        // 3. Fallback: thumbnail cache or default
        var normalArt = LoadAlbumArt(track);
        if (normalArt != null && normalArt != _defaultArt)
        {
            _mediumCache[path] = normalArt;
            TrimCache();
            return normalArt;
        }

        return GetDefaultAlbumArt();
    }

    public ImageSource? LoadMediumAlbumArt(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return GetDefaultAlbumArt();

        if (_mediumCache.TryGetValue(filePath, out var cached) && cached != null)
            return cached;

        if (_scrollingActive)
            return GetDefaultAlbumArt();

        // Try loading from disk cache at medium decode size
        try
        {
            var hash = GetMd5Hash(filePath);
            var cachedImagePath = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.jpg");
            var cachedPngPath   = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.png");
            var metaPath        = Path.Combine(AppPaths.AlbumArtCachePath, $"{hash}.meta");

            var existingPath = File.Exists(cachedImagePath) ? cachedImagePath : (File.Exists(cachedPngPath) ? cachedPngPath : null);
            if (existingPath != null && !InvalidateStaleCache(filePath, existingPath, metaPath))
            {
                var img = LoadSizedImageFromFile(existingPath, MediumDecodeSize);
                if (img != null)
                {
                    _mediumCache[filePath] = img;
                    TrimCache();
                    return img;
                }
            }
        }
        catch { }

        // Fallback: decode directly from file
        var dummy = new MusicTrack { FilePath = filePath };
        var mediumArt = DecodeMediumValidPicture(dummy);
        if (mediumArt != null)
        {
            _mediumCache[filePath] = mediumArt;
            TrimCache();
            return mediumArt;
        }

        // Fallback: use the list thumbnail (still better than nothing)
        return LoadAlbumArt(filePath);
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

    public Task PreloadTrackArtwork(IEnumerable<MusicTrack> tracks)
    {
        // Lightweight: only checks disk cache, no TagLib. Instant startup.
        return Task.Run(() =>
        {
            foreach (var track in tracks)
            {
                if (track == null || string.IsNullOrEmpty(track.FilePath)) continue;
                if (_cache.ContainsKey(track.FilePath)) continue;
                if (_scrollingActive) return; // defer CPU work until layout settles
                try
                {
                    var art = LoadAlbumArtCore(track.FilePath);
                    if (art != null && art != _defaultArt)
                        _cache[track.FilePath] = art;
                }
                catch { }
            }
        });
    }

    // Background warm-up: runs AFTER the UI is visible. Does TagLib parses
    // to populate the disk cache + memory cache. Throttled to avoid starving
    // the UI thread.
    public Task WarmUpArtCacheAsync(IEnumerable<MusicTrack> tracks)
    {
        return Task.Run(() =>
        {
            foreach (var track in tracks)
            {
                if (track == null || string.IsNullOrEmpty(track.FilePath)) continue;
                if (_cache.ContainsKey(track.FilePath)) continue;
                if (_scrollingActive) return; // defer CPU work until layout settles
                try { WarmUpArtCore(track.FilePath); } catch { }
            }
        });
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
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return LoadThumbnailFromStream(ms);
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
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return LoadSizedImageFromStream(ms, decodeSize);
        }
        catch { return null; }
    }

    private void TrimCache()
    {
        while (_cache.Count > MaxCacheSize)
        {
            var oldest = _cache.Keys.FirstOrDefault();
            if (oldest != null) _cache.TryRemove(oldest, out _);
            else break;
        }

        while (_mediumCache.Count > MaxMediumCacheSize)
        {
            var oldest = _mediumCache.Keys.FirstOrDefault();
            if (oldest != null) _mediumCache.TryRemove(oldest, out _);
            else break;
        }

        while (_fullQualityCache.Count > 8)
        {
            var oldest = _fullQualityCache.Keys.FirstOrDefault();
            if (oldest != null) _fullQualityCache.TryRemove(oldest, out _);
            else break;
        }
    }

    private static string GetMd5Hash(string input)
    {
        using var md5 = MD5.Create();
        var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
        var sb = new StringBuilder();
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
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
