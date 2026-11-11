using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace MusicPlayer.ImageLoading;

public sealed record SourceIdentity(
    string NormalizedPath,
    SourceKind Kind,
    string SourcePath,
    long FileSizeBytes,
    long LastModifiedUtcTicks,
    string SchemaVersion,
    string? ContentFingerprint)
{
    public const string CurrentSchemaVersion = "v4";

    public string CacheKey =>
        $"{SchemaVersion}|{Kind}|{NormalizedPath}|{LastModifiedUtcTicks}|{FileSizeBytes}";

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            return Path.GetFullPath(path).Trim();
        }
        catch
        {
            return path.Trim();
        }
    }

    public static bool FastVerify(string path, long expectedSizeBytes, long expectedLastModifiedUtcTicks)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return false;

        try
        {
            var fi = new FileInfo(path);
            return fi.Length == expectedSizeBytes && fi.LastWriteTimeUtc.Ticks == expectedLastModifiedUtcTicks;
        }
        catch
        {
            return false;
        }
    }

    public static string ComputeSha256(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(data));
    }

    public static string ComputeSha256(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(stream));
    }
}

