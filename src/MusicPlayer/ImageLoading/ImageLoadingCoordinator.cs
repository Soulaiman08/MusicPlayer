using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using MusicPlayer.Database;
using MusicPlayer.Models;
using MusicPlayer.Utilities;

namespace MusicPlayer.ImageLoading;

public sealed class ImageLoadingCoordinator : IDisposable
{
    private readonly ImageDiskCache _albumArtDiskCache;
    private readonly ImageMemoryCache _memoryCache;
    private readonly ImageLoadScheduler _scheduler;
    private readonly AlbumArtProvider _albumArtProvider;
    private readonly ArtistImageProvider _artistImageProvider;
    private readonly DatabaseService? _databaseService;
    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _lifetimeCts = new();
    private ImageSource? _defaultArt;
    private static volatile bool _scrollingActive;
    private bool _disposed;

    public static ImageLoadingCoordinator? Instance { get; internal set; }

    public ImageLoadingCoordinator(
        DatabaseService? databaseService = null,
        ILogger? logger = null)
    {
        _databaseService = databaseService;
        _logger = logger;

        string baseArtCache = AppPaths.AlbumArtCachePath;
        _albumArtDiskCache = new ImageDiskCache(baseArtCache, logger);
        _memoryCache = new ImageMemoryCache(64 * 1024 * 1024); // 64 MB
        _scheduler = new ImageLoadScheduler(logger);

        _albumArtProvider = new AlbumArtProvider(
            _albumArtDiskCache,
            _memoryCache,
            _scheduler,
            databaseService,
            logger);

        _artistImageProvider = new ArtistImageProvider(
            _memoryCache,
            _scheduler,
            logger);

        Instance = this;
    }

    public AlbumArtProvider AlbumArt => _albumArtProvider;
    public ArtistImageProvider ArtistImages => _artistImageProvider;
    public ImageMemoryCache MemoryCache => _memoryCache;
    public ImageDiskCache DiskCache => _albumArtDiskCache;
    public ImageLoadScheduler Scheduler => _scheduler;

    public static bool ScrollingActive => _scrollingActive;

    public void SetScrollingActive(bool active)
    {
        _scrollingActive = active;
    }

    public ImageSource? TryGetAlbumArt(MusicTrack? track, ImageQuality quality)
    {
        string? path = AlbumArtProvider.ResolveSourcePath(track, null);
        return _albumArtProvider.TryGetFromMemory(path, quality);
    }

    public ImageSource? TryGetAlbumArt(string? path, ImageQuality quality)
    {
        return _albumArtProvider.TryGetFromMemory(path, quality);
    }

    public Task<ImageSource?> LoadAlbumArtAsync(
        MusicTrack? track,
        string? path,
        ImageQuality quality,
        ImagePriority priority,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromResult<ImageSource?>(null);
        return _albumArtProvider.GetOrLoadAlbumArtAsync(track, path, quality, priority, cancellationToken);
    }

    public Task<string?> GetCachedArtFilePathAsync(
        MusicTrack? track,
        string? path,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromResult<string?>(null);
        return _albumArtProvider.GetCachedArtFilePathAsync(track, path, cancellationToken);
    }

    public ImageSource? TryGetArtistImage(string artist, bool isHighRes)
    {
        return _artistImageProvider.TryGetFromMemory(artist, isHighRes);
    }

    public Task<ImageSource?> LoadArtistImageAsync(
        string artist,
        bool isHighRes,
        ImagePriority priority,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromResult<ImageSource?>(null);
        return _artistImageProvider.GetOrLoadArtistImageAsync(artist, isHighRes, priority, cancellationToken);
    }

    public Task<string?> GetOrFetchArtistImagePathAsync(string artist, CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.FromResult<string?>(null);
        return _artistImageProvider.GetOrFetchArtistImagePathAsync(artist, cancellationToken);
    }

    public ImageSource GetDefaultAlbumArt()
    {
        if (_defaultArt != null) return _defaultArt;
        _defaultArt = CreateDefaultArt();
        return _defaultArt;
    }

    public static ImageSource CreateDefaultArt()
    {
        var group = new DrawingGroup();

        var bgBrush = new LinearGradientBrush(
            Color.FromRgb(0x18, 0x1B, 0x22),
            Color.FromRgb(0x0E, 0x10, 0x14),
            new System.Windows.Point(0, 0),
            new System.Windows.Point(1, 1));
        var bgGeom = new RectangleGeometry(new System.Windows.Rect(0, 0, 256, 256), 14, 14);
        group.Children.Add(new GeometryDrawing(bgBrush, null, bgGeom));

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

        var discBrush = new LinearGradientBrush(
            Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF),
            Color.FromArgb(0x05, 0xFF, 0xFF, 0xFF),
            new System.Windows.Point(0, 0),
            new System.Windows.Point(0, 1));
        var discPen = new Pen(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), 1);
        group.Children.Add(new GeometryDrawing(discBrush, discPen, new EllipseGeometry(new System.Windows.Point(128, 128), 85, 85)));

        var ringPen1 = new Pen(new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), 0.75);
        group.Children.Add(new GeometryDrawing(null, ringPen1, new EllipseGeometry(new System.Windows.Point(128, 128), 67.5, 67.5)));

        var ringPen2 = new Pen(new SolidColorBrush(Color.FromArgb(0x0C, 0xFF, 0xFF, 0xFF)), 0.5);
        group.Children.Add(new GeometryDrawing(null, ringPen2, new EllipseGeometry(new System.Windows.Point(128, 128), 50, 50)));

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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }

        try { _lifetimeCts.Cancel(); } catch { }

        _scheduler.Dispose();
        _artistImageProvider.Dispose();
        _memoryCache.Clear();
        _lifetimeCts.Dispose();

        try
        {
            _albumArtDiskCache.CleanOldTempFiles(TimeSpan.FromMinutes(5));
        }
        catch { }
    }
}

