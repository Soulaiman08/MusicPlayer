using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                foreach (var picture in tagFile.Tag.Pictures)
                {
                    if (picture.Data.Data is { Length: > 0 } data)
                        return data;
                }
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

        var kind = ResolveSourceKind(track, sourcePath);
        var normalizedPath = Path.GetFullPath(sourcePath);
        var fileInfo = File.Exists(sourcePath) ? new FileInfo(sourcePath) : null;
        var contentFingerprint = ComputeSha256(bytes);
        var identity = new AlbumArtSourceIdentity(
            normalizedPath,
            kind,
            sourcePath,
            fileInfo?.Length ?? bytes.Length,
            fileInfo?.LastWriteTimeUtc.Ticks ?? 0,
            "album-art-v1",
            contentFingerprint);

        var metadata = ReadMetadata(bytes, identity);
        return new OriginalAlbumArtAsset(identity, metadata, bytes);
    }

    private static AlbumArtMetadata ReadMetadata(byte[] bytes, AlbumArtSourceIdentity identity)
    {
        int width = 0;
        int height = 0;
        string format = "unknown";
        bool valid = false;

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
}

internal sealed class AlbumArtDerivedAssetCache
{
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<AlbumArtQuality, ImageSource?>> _bySource =
        new(StringComparer.OrdinalIgnoreCase);

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
        bucket[quality] = image;
    }

    public void Invalidate(string identityKey)
    {
        if (!string.IsNullOrWhiteSpace(identityKey))
            _bySource.TryRemove(identityKey, out _);
    }
}

internal sealed class AlbumArtAssetService
{
    private readonly ConcurrentDictionary<string, OriginalAlbumArtAsset> _originalByIdentity =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly AlbumArtDerivedAssetCache _derivedCache = new();

    public OriginalAlbumArtAsset? Resolve(MusicTrack? track)
    {
        var source = AlbumArtSourceResolver.Resolve(track);
        if (source == null || !source.IsValid)
            return source;

        _originalByIdentity[source.Identity.CacheKey] = source;
        return source;
    }

    public bool TryGetDerived(OriginalAlbumArtAsset asset, AlbumArtQuality quality, out ImageSource? image)
        => _derivedCache.TryGet(asset.Identity.CacheKey, quality, out image);

    public void StoreDerived(OriginalAlbumArtAsset asset, AlbumArtQuality quality, ImageSource? image)
        => _derivedCache.Store(asset.Identity.CacheKey, quality, image);

    public void InvalidateDerived(OriginalAlbumArtAsset asset)
        => _derivedCache.Invalidate(asset.Identity.CacheKey);
}
