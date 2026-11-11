using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using MusicPlayer.Database;
using MusicPlayer.Models;
using MusicPlayer.Utilities;

namespace MusicPlayer.ImageLoading;

public sealed class AlbumArtProvider
{
    private static readonly string[] SidecarNames =
    {
        "cover.jpg", "folder.jpg", "front.jpg", "album.jpg",
        "cover.png", "folder.png", "front.png", "album.png"
    };

    private readonly ImageDiskCache _diskCache;
    private readonly ImageMemoryCache _memoryCache;
    private readonly ImageLoadScheduler _scheduler;
    private readonly DatabaseService? _databaseService;
    private readonly ILogger? _logger;
    private readonly SingleFlightCoordinator<ImageSource> _singleFlight = new();

    public AlbumArtProvider(
        ImageDiskCache diskCache,
        ImageMemoryCache memoryCache,
        ImageLoadScheduler scheduler,
        DatabaseService? databaseService = null,
        ILogger? logger = null)
    {
        _diskCache = diskCache;
        _memoryCache = memoryCache;
        _scheduler = scheduler;
        _databaseService = databaseService;
        _logger = logger;
    }

    public ImageSource? TryGetFromMemory(string? trackPath, ImageQuality quality)
    {
        if (string.IsNullOrWhiteSpace(trackPath)) return null;
        string normPath = SourceIdentity.NormalizePath(trackPath);
        if (_memoryCache.TryGet(normPath, ImageKind.AlbumArt, quality, out var cached) && cached != null)
        {
            return cached;
        }
        return null;
    }

    public async Task<ImageSource?> GetOrLoadAlbumArtAsync(
        MusicTrack? track,
        string? filePath,
        ImageQuality quality,
        ImagePriority priority,
        CancellationToken cancellationToken = default)
    {
        string? sourcePath = ResolveSourcePath(track, filePath);
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        string normPath = SourceIdentity.NormalizePath(sourcePath);

        // 1. Check Memory Cache
        if (_memoryCache.TryGet(normPath, ImageKind.AlbumArt, quality, out var memCached) && memCached != null)
        {
            return memCached;
        }

        // 2. Single-flight request by (normPath + quality)
        string flightKey = $"{normPath}:{(int)quality}";
        return await _singleFlight.RunOrJoinAsync(
            flightKey,
            ct => LoadAndCacheArtAsync(track, sourcePath, normPath, quality, priority, ct),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ImageSource?> LoadAndCacheArtAsync(
        MusicTrack? track,
        string sourcePath,
        string normPath,
        ImageQuality quality,
        ImagePriority priority,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 3. Fast database & disk verification
        if (_databaseService != null)
        {
            var record = _databaseService.GetAlbumArtRecord(normPath);
            bool sourceExists = File.Exists(sourcePath);
            bool sourceUnchanged = false;
            if (sourceExists && record != null)
            {
                try
                {
                    var fi = new FileInfo(sourcePath);
                    sourceUnchanged = record.FileSizeBytes == fi.Length && record.LastModifiedUtcTicks == fi.LastWriteTimeUtc.Ticks;
                }
                catch { }
            }

            if (record != null && (sourceUnchanged || !sourceExists))
            {
                if (!record.HasArtwork)
                {
                    // Known to have no artwork; return null quickly
                    return null;
                }

                if (!string.IsNullOrEmpty(record.ContentHash))
                {
                    if (_diskCache.TryGetValidCachedFile(record.ContentHash, quality, out var cachedFile) && cachedFile != null)
                    {
                        var decodedFromDisk = await _scheduler.ScheduleAsync(
                            OperationType.ImageDecode,
                            priority,
                            _ => Task.FromResult(ImageDecoder.DecodeFromFile(cachedFile, quality)),
                            cancellationToken).ConfigureAwait(false);

                        if (decodedFromDisk != null)
                        {
                            _memoryCache.Set(normPath, ImageKind.AlbumArt, quality, decodedFromDisk);
                            return decodedFromDisk;
                        }
                    }
                    // If target quality is not yet cached on disk, but the Full (original) asset is,
                    // decode target quality directly from the cached Full asset without TagLib re-extraction.
                    else if (_diskCache.TryGetValidCachedFile(record.ContentHash, ImageQuality.Full, out var fullCachedFile) && fullCachedFile != null)
                    {
                        var derivedFromFull = await _scheduler.ScheduleAsync(
                            OperationType.ImageDecode,
                            priority,
                            _ => Task.FromResult(ImageDecoder.DecodeFromFile(fullCachedFile, quality)),
                            cancellationToken).ConfigureAwait(false);

                        if (derivedFromFull != null)
                        {
                            _memoryCache.Set(normPath, ImageKind.AlbumArt, quality, derivedFromFull);

                            try
                            {
                                bool isPngFile = fullCachedFile.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
                                byte[]? derivedBytes = ImageDecoder.EncodeToBytes(derivedFromFull, isPngFile);
                                if (derivedBytes != null && derivedBytes.Length > 0)
                                {
                                    string? savedDerived = await _diskCache.SaveAtomicAsync(
                                        record.ContentHash, quality, derivedBytes, isPngFile,
                                        cancellationToken: cancellationToken).ConfigureAwait(false);

                                    if (savedDerived != null && _databaseService != null)
                                    {
                                        if (quality == ImageQuality.Thumbnail) record.ThumbnailPath = savedDerived;
                                        if (quality == ImageQuality.Medium) record.MediumPath = savedDerived;
                                        if (quality == ImageQuality.Large) record.LargePath = savedDerived;
                                        record.LastVerifiedUtcTicks = DateTime.UtcNow.Ticks;
                                        _databaseService.UpsertAlbumArtRecord(record);
                                    }
                                }
                            }
                            catch { }

                            return derivedFromFull;
                        }
                    }
                }
            }
        }

        // 4. Extraction required (off-UI thread via scheduler)
        ExtractedArt? extracted = await _scheduler.ScheduleAsync(
            OperationType.TagLibExtraction,
            priority,
            ct => Task.FromResult(ExtractArtwork(sourcePath, track, ct)),
            cancellationToken).ConfigureAwait(false);

        if (extracted == null || extracted.RawBytes == null || extracted.RawBytes.Length == 0)
        {
            // Record in DB that no artwork exists to avoid re-extraction
            if (_databaseService != null && File.Exists(sourcePath))
            {
                try
                {
                    var fi = new FileInfo(sourcePath);
                    _databaseService.UpsertAlbumArtRecord(new AlbumArtRecord
                    {
                        NormalizedTrackPath = normPath,
                        TrackId = track?.Id ?? 0,
                        FileSizeBytes = fi.Length,
                        LastModifiedUtcTicks = fi.LastWriteTimeUtc.Ticks,
                        HasArtwork = false,
                        SchemaVersion = 4,
                        LastVerifiedUtcTicks = DateTime.UtcNow.Ticks
                    });
                }
                catch { }
            }
            return null;
        }

        string contentHash = SourceIdentity.ComputeSha256(extracted.RawBytes);
        bool isPng = IsPngBytes(extracted.RawBytes);

        // 5. Decode target quality
        var decoded = await _scheduler.ScheduleAsync(
            OperationType.ImageDecode,
            priority,
            _ => Task.FromResult(ImageDecoder.Decode(extracted.RawBytes, quality)),
            cancellationToken).ConfigureAwait(false);

        if (decoded != null)
        {
            _memoryCache.Set(normPath, ImageKind.AlbumArt, quality, decoded);

            // 6. Save original and derived quality to disk cache atomically, and update DB record
            try
            {
                // Always preserve the original source bytes under Full quality
                string? fullSavedPath = await _diskCache.SaveAtomicAsync(
                    contentHash, ImageQuality.Full, extracted.RawBytes, isPng,
                    metadataText: $"{DateTime.UtcNow.Ticks}|{extracted.RawBytes.Length}|{extracted.Kind}",
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                string? savedPath = fullSavedPath;
                if (quality != ImageQuality.Full)
                {
                    byte[]? derivedBytes = ImageDecoder.EncodeToBytes(decoded, isPng);
                    savedPath = await _diskCache.SaveAtomicAsync(
                        contentHash, quality, derivedBytes ?? extracted.RawBytes, isPng,
                        metadataText: $"{DateTime.UtcNow.Ticks}|{(derivedBytes?.Length ?? extracted.RawBytes.Length)}|{extracted.Kind}",
                        cancellationToken: cancellationToken).ConfigureAwait(false);
                }

                if (_databaseService != null && File.Exists(sourcePath))
                {
                    var fi = new FileInfo(sourcePath);
                    var existingRec = _databaseService.GetAlbumArtRecord(normPath);
                    var rec = existingRec ?? new AlbumArtRecord
                    {
                        NormalizedTrackPath = normPath,
                        TrackId = track?.Id ?? 0,
                    };

                    rec.FileSizeBytes = fi.Length;
                    rec.LastModifiedUtcTicks = fi.LastWriteTimeUtc.Ticks;
                    rec.ContentHash = contentHash;
                    rec.SourceKind = (int)extracted.Kind;
                    rec.SourceArtPath = extracted.SourceArtPath;
                    rec.HasArtwork = true;
                    rec.SchemaVersion = 4;
                    rec.LastVerifiedUtcTicks = DateTime.UtcNow.Ticks;

                    if (fullSavedPath != null) rec.FullPath = fullSavedPath;
                    if (quality == ImageQuality.Thumbnail) rec.ThumbnailPath = savedPath;
                    if (quality == ImageQuality.Medium) rec.MediumPath = savedPath;
                    if (quality == ImageQuality.Large) rec.LargePath = savedPath;
                    if (quality == ImageQuality.Full) rec.FullPath = savedPath;

                    _databaseService.UpsertAlbumArtRecord(rec);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Disk cache or DB save failed for {Hash}", contentHash);
            }

            return decoded;
        }

        return null;
    }

    public async Task<string?> GetCachedArtFilePathAsync(
        MusicTrack? track,
        string? filePath,
        CancellationToken cancellationToken = default)
    {
        string? sourcePath = ResolveSourcePath(track, filePath);
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        string normPath = SourceIdentity.NormalizePath(sourcePath);

        // Check DB first
        if (_databaseService != null)
        {
            var record = _databaseService.GetAlbumArtRecord(normPath);
            bool sourceExists = File.Exists(sourcePath);
            bool sourceUnchanged = false;
            if (sourceExists && record != null)
            {
                try
                {
                    var fi = new FileInfo(sourcePath);
                    sourceUnchanged = record.FileSizeBytes == fi.Length && record.LastModifiedUtcTicks == fi.LastWriteTimeUtc.Ticks;
                }
                catch { }
            }

            if (record != null && (sourceUnchanged || !sourceExists) && record.HasArtwork)
            {
                if (!string.IsNullOrEmpty(record.MediumPath) && File.Exists(record.MediumPath))
                    return record.MediumPath;
                if (!string.IsNullOrEmpty(record.FullPath) && File.Exists(record.FullPath))
                    return record.FullPath;
                if (!string.IsNullOrEmpty(record.ThumbnailPath) && File.Exists(record.ThumbnailPath))
                    return record.ThumbnailPath;

                if (!string.IsNullOrEmpty(record.ContentHash))
                {
                    if (_diskCache.TryGetValidCachedFile(record.ContentHash, ImageQuality.Medium, out var medFile))
                        return medFile;
                    if (_diskCache.TryGetValidCachedFile(record.ContentHash, ImageQuality.Full, out var fullFile))
                        return fullFile;
                }
            }
        }

        // Otherwise extract and save
        var art = await GetOrLoadAlbumArtAsync(track, sourcePath, ImageQuality.Medium, ImagePriority.Playback, cancellationToken).ConfigureAwait(false);
        if (art != null && _databaseService != null)
        {
            var record = _databaseService.GetAlbumArtRecord(normPath);
            if (record?.MediumPath != null && File.Exists(record.MediumPath))
                return record.MediumPath;
            if (record?.ContentHash != null && _diskCache.TryGetValidCachedFile(record.ContentHash, ImageQuality.Medium, out var p))
                return p;
        }

        return null;
    }

    public void InvalidateTrack(string? trackPath)
    {
        if (string.IsNullOrWhiteSpace(trackPath)) return;
        string normPath = SourceIdentity.NormalizePath(trackPath);
        _memoryCache.RemoveAllForSource(normPath);
        _databaseService?.DeleteAlbumArtRecord(normPath);
    }

    public static string? ResolveSourcePath(MusicTrack? track, string? filePath)
    {
        if (!string.IsNullOrWhiteSpace(track?.AlbumArtPath) && File.Exists(track.AlbumArtPath))
            return track.AlbumArtPath;

        if (!string.IsNullOrWhiteSpace(filePath) && File.Exists(filePath))
            return filePath;

        if (!string.IsNullOrWhiteSpace(track?.FilePath) && File.Exists(track.FilePath))
            return track.FilePath;

        if (!string.IsNullOrWhiteSpace(track?.AlbumArtPath))
            return track.AlbumArtPath;

        return filePath ?? track?.FilePath;
    }

    internal sealed record ExtractedArt(byte[] RawBytes, SourceKind Kind, string? SourceArtPath);

    internal static ExtractedArt? ExtractArtwork(string sourcePath, MusicTrack? track, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        // 1. Check external image path
        if (File.Exists(sourcePath) && IsImagePath(sourcePath))
        {
            try
            {
                var bytes = File.ReadAllBytes(sourcePath);
                if (bytes.Length > 0) return new ExtractedArt(bytes, SourceKind.External, sourcePath);
            }
            catch { }
        }

        // 2. Check embedded tags in audio file
        if (File.Exists(sourcePath) && IsAudioPath(sourcePath))
        {
            try
            {
                using var tagFile = TagLib.File.Create(sourcePath);

                // Two-pass selection: skip non-image frames (e.g. C2PA manifests with MIME
                // "application/c2pa" that TagLib exposes as PictureType.NotAPicture).
                // Pass 1: FrontCover with an image/* MIME — best possible match.
                // Pass 2: any picture with an image/* MIME — fallback for unlabeled art.
                TagLib.IPicture? best = null;
                foreach (var picture in tagFile.Tag.Pictures)
                {
                    if (picture.Data.Data == null || picture.Data.Data.Length == 0) continue;
                    if (!IsImageMime(picture.MimeType)) continue; // skip C2PA, PDF, etc.

                    if (picture.Type == TagLib.PictureType.FrontCover)
                    {
                        best = picture;
                        break; // perfect match, stop immediately
                    }
                    best ??= picture; // remember first valid image as fallback
                }

                if (best != null)
                    return new ExtractedArt(best.Data.Data!, SourceKind.Embedded, sourcePath);
            }
            catch { }
        }

        // 3. Check sidecar images in track folder
        try
        {
            string? dir = Path.GetDirectoryName(sourcePath);
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                foreach (var sidecarName in SidecarNames)
                {
                    string sidecarPath = Path.Combine(dir, sidecarName);
                    if (File.Exists(sidecarPath))
                    {
                        var bytes = File.ReadAllBytes(sidecarPath);
                        if (bytes.Length > 0) return new ExtractedArt(bytes, SourceKind.Sidecar, sidecarPath);
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".webp", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".bmp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsAudioPath(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".wma", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".opus", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the MIME type is an image/* type.
    /// Rejects non-image frames such as C2PA manifests ("application/c2pa"),
    /// PDF attachments, and any other embedded binary that isn't a rendered image.
    /// </summary>
    private static bool IsImageMime(string? mime)
        => !string.IsNullOrEmpty(mime) &&
           mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    private static bool IsPngBytes(byte[] bytes)
    {
        return bytes.Length >= 4 &&
               bytes[0] == 0x89 &&
               bytes[1] == 0x50 &&
               bytes[2] == 0x4E &&
               bytes[3] == 0x47;
    }
}
