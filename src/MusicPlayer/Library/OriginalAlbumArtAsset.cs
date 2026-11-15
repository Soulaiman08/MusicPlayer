using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.ImageLoading;
using MusicPlayer.Models;

namespace MusicPlayer.Library;

internal enum AlbumArtSourceKind
{
    Unknown,
    External,
    Embedded,
    Sidecar
}

internal enum AlbumArtQuality
{
    Thumbnail,
    Medium,
    Full
}

internal sealed record AlbumArtSourceIdentity(
    string NormalizedPath,
    AlbumArtSourceKind SourceKind,
    string SourcePath,
    long FileSizeBytes,
    long LastModifiedUtcTicks,
    string CacheVersion,
    string? ContentFingerprint)
{
    public string CacheKey =>
        $"{CacheVersion}|{SourceKind}|{NormalizedPath}|{LastModifiedUtcTicks}|{FileSizeBytes}";
}

internal sealed record AlbumArtMetadata(
    int Width,
    int Height,
    string Format,
    long DataSizeBytes,
    AlbumArtSourceKind SourceKind,
    string SourcePath,
    long SourceVersionTicks,
    string? SourceFingerprint,
    bool IsValid,
    bool DerivedAssetsReady);

internal sealed class OriginalAlbumArtAsset
{
    public OriginalAlbumArtAsset(
        AlbumArtSourceIdentity identity,
        AlbumArtMetadata metadata,
        byte[] sourceBytes)
    {
        Identity = identity;
        Metadata = metadata;
        SourceBytes = sourceBytes ?? Array.Empty<byte>();
    }

    public AlbumArtSourceIdentity Identity { get; }
    public AlbumArtMetadata Metadata { get; }
    public byte[] SourceBytes { get; }
    public bool IsValid => Metadata.IsValid && SourceBytes.Length > 0;
    public string SourcePath => Metadata.SourcePath;
}

internal static class AlbumArtSourceResolver
{
    private static readonly string[] SidecarCandidates =
    {
        "cover.jpg",
        "folder.jpg",
        "front.jpg",
        "album.jpg",
        "cover.png",
        "folder.png",
        "front.png",
        "album.png"
    };

    public static string? ResolveTrackSourcePath(MusicTrack? track)
    {
        if (track == null) return null;

        if (!string.IsNullOrWhiteSpace(track.AlbumArtPath) && File.Exists(track.AlbumArtPath))
            return track.AlbumArtPath;

        if (!string.IsNullOrWhiteSpace(track.FilePath) && File.Exists(track.FilePath))
            return track.FilePath;

        if (!string.IsNullOrWhiteSpace(track.AlbumArtPath))
            return track.AlbumArtPath;

        return track.FilePath;
    }

    public static AlbumArtSourceKind ResolveSourceKind(MusicTrack? track, string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return AlbumArtSourceKind.Unknown;

        if (!string.IsNullOrWhiteSpace(track?.AlbumArtPath) &&
            string.Equals(track.AlbumArtPath, sourcePath, StringComparison.OrdinalIgnoreCase) &&
            File.Exists(sourcePath))
        {
            return IsKnownImagePath(sourcePath) ? AlbumArtSourceKind.External : AlbumArtSourceKind.Unknown;
        }

        if (IsKnownImagePath(sourcePath))
            return AlbumArtSourceKind.External;

        if (IsSidecarPath(sourcePath))
            return AlbumArtSourceKind.Sidecar;

        if (track != null && !string.IsNullOrWhiteSpace(track.FilePath) &&
            string.Equals(track.FilePath, sourcePath, StringComparison.OrdinalIgnoreCase) &&
            IsAudioFile(sourcePath))
        {
            return AlbumArtSourceKind.Embedded;
        }

        return AlbumArtSourceKind.Unknown;
    }

    public static byte[]? TryReadSourceBytes(string? sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        if (File.Exists(sourcePath) && IsKnownImagePath(sourcePath))
        {
            try { return File.ReadAllBytes(sourcePath); }
            catch { return null; }
        }

        if (File.Exists(sourcePath) && IsAudioFile(sourcePath))
        {
            try
            {
                using var tagFile = TagLib.File.Create(sourcePath);

                // Two-pass: skip non-image frames (e.g. C2PA manifests with MIME
                // "application/c2pa"). Prefer FrontCover; fall back to first image/* frame.
                TagLib.IPicture? best = null;
                foreach (var picture in tagFile.Tag.Pictures)
                {
                    if (picture.Data.Data is not { Length: > 0 }) continue;
                    if (!IsImageMime(picture.MimeType)) continue; // skip C2PA, PDF, etc.

                    if (picture.Type == TagLib.PictureType.FrontCover)
                    {
                        best = picture;
                        break;
                    }
                    best ??= picture;
                }

                if (best?.Data.Data is { Length: > 0 } data)
                    return data;
            }
            catch { }
        }

        return null;
    }

    public static OriginalAlbumArtAsset? Resolve(MusicTrack? track)
    {
        var sourcePath = ResolveTrackSourcePath(track);
        if (string.IsNullOrWhiteSpace(sourcePath)) return null;

        var bytes = TryReadSourceBytes(sourcePath);
        if (bytes == null || bytes.Length == 0) return null;

        var fileInfo = File.Exists(sourcePath) ? new FileInfo(sourcePath) : null;
        var contentFingerprint = ComputeSha256(bytes);
        var identity = CreateIdentity(
            track,
            sourcePath,
            fileInfo?.Length ?? bytes.Length,
            fileInfo?.LastWriteTimeUtc.Ticks ?? 0,
            contentFingerprint);

        var metadata = ReadMetadata(bytes, identity);
        return new OriginalAlbumArtAsset(identity, metadata, bytes);
    }

    /// <summary>
    /// Builds the source identity. Every field except the content fingerprint
    /// can be derived from the path and the file system, which is what the
    /// derived-asset cache key is built from - the fingerprint itself is not
    /// part of <see cref="AlbumArtSourceIdentity.CacheKey"/>.
    /// </summary>
    public static AlbumArtSourceIdentity CreateIdentity(
        MusicTrack? track,
        string sourcePath,
        long fileSizeBytes,
        long lastModifiedUtcTicks,
        string? contentFingerprint)
    {
        var kind = ResolveSourceKind(track, sourcePath);
        string normalizedPath;
        try { normalizedPath = Path.GetFullPath(sourcePath); }
        catch { normalizedPath = sourcePath; }

        return new AlbumArtSourceIdentity(
            normalizedPath,
            kind,
            sourcePath,
            fileSizeBytes,
            lastModifiedUtcTicks,
            "album-art-v1",
            contentFingerprint);
    }

    private static AlbumArtMetadata ReadMetadata(byte[] bytes, AlbumArtSourceIdentity identity)
    {
        int width = 0;
        int height = 0;
        string format = "unknown";
        bool valid = false;

        // This decodes the source at its native size only to read the pixel
        // dimensions. It is counted separately so the report can tell a
        // metadata probe apart from a real rendition decode.
        long probeStart = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count > 0)
            {
                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
                format = frame.Format.ToString();
                valid = width > 0 && height > 0;
            }
        }
        catch { }

        ImageDecodeMetrics.RecordMetadataProbe(
            ImageDecodeMetrics.CompactKey(identity.SourcePath),
            System.Diagnostics.Stopwatch.GetTimestamp() - probeStart);

        return new AlbumArtMetadata(
            width,
            height,
            format,
            bytes.Length,
            identity.SourceKind,
            identity.SourcePath,
            identity.LastModifiedUtcTicks,
            identity.ContentFingerprint,
            valid,
            true);
    }

    private static string ComputeSha256(byte[] bytes)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(bytes));
    }

    private static bool IsKnownImagePath(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".bmp", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".gif", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".webp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSidecarPath(string path)
    {
        var fileName = Path.GetFileName(path);
        foreach (var candidate in SidecarCandidates)
        {
            if (string.Equals(fileName, candidate, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsAudioFile(string path)
    {
        var extension = Path.GetExtension(path);
        return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".flac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".aac", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wav", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".wma", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Returns true when the MIME type is an image/* type.
    /// Rejects non-image embedded frames such as C2PA manifests ("application/c2pa").
    /// </summary>
    private static bool IsImageMime(string? mime)
        => !string.IsNullOrEmpty(mime) &&
           mime.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
}

internal sealed class AlbumArtDerivedAssetCache
{
    /// <summary>
    /// Upper bound for the derived (re-sized) artwork cache. Every entry keeps a
    /// decoded bitmap alive, and the invalidation hook was never called from
    /// anywhere, so an unbounded map pinned one bitmap per (track, quality) for the
    /// whole session. The map is trimmed oldest-first once it grows past the cap.
    /// </summary>
    private const int MaxEntries = 512;

    /// <summary>
    /// Byte budget for the derived cache. The entry cap alone was not enough:
    /// a single Full rendition is a 1024x1024 bitmap (4 MB), so 512 entries
    /// could pin hundreds of megabytes - and because the derived cache holds a
    /// strong reference it also defeated the LRU eviction of the coordinator's
    /// memory cache. The cache now evicts oldest-first until it fits.
    /// </summary>
    private const long MaxBytes = 64L * 1024 * 1024;

    private readonly ConcurrentDictionary<string, ConcurrentDictionary<AlbumArtQuality, ImageSource?>> _bySource =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentQueue<string> _insertionOrder = new();
    private readonly ConcurrentDictionary<string, long> _sizes = new(StringComparer.OrdinalIgnoreCase);
    private long _currentBytes;

    public bool TryGet(string identityKey, AlbumArtQuality quality, out ImageSource? image)
    {
        image = null;
        if (string.IsNullOrWhiteSpace(identityKey)) return false;

        if (!_bySource.TryGetValue(identityKey, out var items))
            return false;

        if (!items.TryGetValue(quality, out image) || image == null)
            return false;

        return true;
    }

    public void Store(string identityKey, AlbumArtQuality quality, ImageSource? image)
    {
        if (string.IsNullOrWhiteSpace(identityKey) || image == null)
            return;

        var bucket = _bySource.GetOrAdd(identityKey, _ => new ConcurrentDictionary<AlbumArtQuality, ImageSource?>());
        long size = EstimateSizeBytes(image);
        if (bucket.TryAdd(quality, image))
            _insertionOrder.Enqueue(identityKey);
        else
            bucket[quality] = image;

        // Re-storing the same slot must not charge the budget twice: the old
        // size is subtracted before the new one is added.
        _sizes.AddOrUpdate(
            SlotKey(identityKey, quality),
            size,
            (_, old) => { Interlocked.Add(ref _currentBytes, -old); return size; });
        Interlocked.Add(ref _currentBytes, size);

        Trim();
    }

    /// <summary>Drops the oldest sources until the cache is back inside its cap.</summary>
    private void Trim()
    {
        while (_bySource.Count > MaxEntries && _insertionOrder.TryDequeue(out var oldest))
        {
            RemoveSource(oldest);
        }

        while (Volatile.Read(ref _currentBytes) > MaxBytes && _insertionOrder.TryDequeue(out var oldest))
        {
            RemoveSource(oldest);
        }
    }

    private void RemoveSource(string identityKey)
    {
        if (!_bySource.TryRemove(identityKey, out var bucket)) return;
        foreach (var quality in bucket.Keys)
        {
            if (_sizes.TryRemove(SlotKey(identityKey, quality), out long removed) && removed != 0)
            {
                Interlocked.Add(ref _currentBytes, -removed);
            }
        }
    }

    public void Invalidate(string identityKey)
    {
        if (string.IsNullOrWhiteSpace(identityKey)) return;
        RemoveSource(identityKey);
    }

    /// <summary>Drops every cached rendition.</summary>
    public void Clear()
    {
        _bySource.Clear();
        _sizes.Clear();
        while (_insertionOrder.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _currentBytes, 0);
    }

    private static string SlotKey(string identityKey, AlbumArtQuality quality)
        => identityKey + "|" + (int)quality;

    private static long EstimateSizeBytes(ImageSource image)
    {
        if (image is BitmapSource bitmap)
        {
            int w = Math.Max(1, bitmap.PixelWidth);
            int h = Math.Max(1, bitmap.PixelHeight);
            int bpp = bitmap.Format.BitsPerPixel > 0 ? bitmap.Format.BitsPerPixel : 32;
            return ((long)w * h * bpp) / 8 + 128;
        }

        return 4096;
    }
}

internal sealed class AlbumArtAssetService
{
    // The original artwork is cached by its source path only. A second map keyed by
    // the content identity was written but never read, so it only pinned the full
    // raw artwork bytes of every track for the whole session.
    private readonly ConcurrentDictionary<string, OriginalAlbumArtAsset> _originalByPath =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly AlbumArtDerivedAssetCache _derivedCache = new();

    public OriginalAlbumArtAsset? Resolve(MusicTrack? track)
    {
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        bool readSource = false;

        try
        {
            var path = AlbumArtSourceResolver.ResolveTrackSourcePath(track);
            if (string.IsNullOrWhiteSpace(path)) return null;

            if (_originalByPath.TryGetValue(path, out var cached) && cached != null)
            {
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    if (fi.Length == cached.Identity.FileSizeBytes && fi.LastWriteTimeUtc.Ticks == cached.Identity.LastModifiedUtcTicks)
                    {
                        return cached;
                    }
                }
                else
                {
                    return cached;
                }
            }

            readSource = true;
            var source = AlbumArtSourceResolver.Resolve(track);
            if (source == null || !source.IsValid)
            {
                return source;
            }

            _originalByPath[path] = source;
            return source;
        }
        finally
        {
            ImageDecodeMetrics.RecordAssetResolve(readSource, System.Diagnostics.Stopwatch.GetTimestamp() - started);
        }
    }

    public bool TryGetDerived(OriginalAlbumArtAsset asset, AlbumArtQuality quality, out ImageSource? image)
        => _derivedCache.TryGet(asset.Identity.CacheKey, quality, out image);

    public void StoreDerived(OriginalAlbumArtAsset asset, AlbumArtQuality quality, ImageSource? image)
        => _derivedCache.Store(asset.Identity.CacheKey, quality, image);

    /// <summary>
    /// Stores a derived rendition under the same key that
    /// <see cref="Resolve"/> would produce, without materialising the original
    /// artwork.
    ///
    /// The cache key is built from the cache version, the source kind, the
    /// normalised path, the file size and the modification time - none of which
    /// need the artwork bytes. Resolving the original artwork on the other hand
    /// re-reads the source file, hashes the artwork bytes and decodes the image
    /// at its native size just to learn its pixel dimensions. That work was done
    /// for every album that finished loading, and the result was thrown away:
    /// the decoded dimensions are never read by any production code path.
    /// </summary>
    public bool StoreDerivedForTrack(MusicTrack? track, AlbumArtQuality quality, ImageSource? image)
    {
        if (image == null) return false;

        var sourcePath = AlbumArtSourceResolver.ResolveTrackSourcePath(track);
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;

        long fileSize;
        long lastModifiedTicks;
        try
        {
            var fi = new FileInfo(sourcePath);
            if (!fi.Exists) return false;
            fileSize = fi.Length;
            lastModifiedTicks = fi.LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return false;
        }

        var identity = AlbumArtSourceResolver.CreateIdentity(
            track,
            sourcePath,
            fileSize,
            lastModifiedTicks,
            contentFingerprint: null);

        _derivedCache.Store(identity.CacheKey, quality, image);
        return true;
    }

    /// <summary>
    /// Looks a derived rendition up without resolving the original artwork.
    /// </summary>
    public bool TryGetDerivedForTrack(MusicTrack? track, AlbumArtQuality quality, out ImageSource? image)
    {
        image = null;
        var sourcePath = AlbumArtSourceResolver.ResolveTrackSourcePath(track);
        if (string.IsNullOrWhiteSpace(sourcePath)) return false;

        long fileSize;
        long lastModifiedTicks;
        try
        {
            var fi = new FileInfo(sourcePath);
            if (!fi.Exists) return false;
            fileSize = fi.Length;
            lastModifiedTicks = fi.LastWriteTimeUtc.Ticks;
        }
        catch
        {
            return false;
        }

        var identity = AlbumArtSourceResolver.CreateIdentity(
            track,
            sourcePath,
            fileSize,
            lastModifiedTicks,
            contentFingerprint: null);

        return _derivedCache.TryGet(identity.CacheKey, quality, out image);
    }

    public void InvalidateDerived(OriginalAlbumArtAsset asset)
        => _derivedCache.Invalidate(asset.Identity.CacheKey);

    /// <summary>Drops every derived rendition (and the cached originals).</summary>
    public void ClearDerived()
    {
        _originalByPath.Clear();
        _derivedCache.Clear();
    }
}
