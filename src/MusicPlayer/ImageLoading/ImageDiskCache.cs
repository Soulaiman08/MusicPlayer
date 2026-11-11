using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MusicPlayer.Utilities;

namespace MusicPlayer.ImageLoading;

public sealed class ImageDiskCache
{
    private readonly string _baseDirectory;
    private readonly string _tempDirectory;
    private readonly ILogger? _logger;
    private readonly object _cleanupGate = new();

    public const string SchemaVersion = "v4";

    public ImageDiskCache(string baseDirectory, ILogger? logger = null)
    {
        _baseDirectory = baseDirectory;
        _tempDirectory = Path.Combine(baseDirectory, "temp");
        _logger = logger;
        EnsureDirectories();
    }

    public string BaseDirectory => _baseDirectory;
    public string TempDirectory => _tempDirectory;

    public void EnsureDirectories()
    {
        try
        {
            if (!Directory.Exists(_baseDirectory)) Directory.CreateDirectory(_baseDirectory);
            if (!Directory.Exists(_tempDirectory)) Directory.CreateDirectory(_tempDirectory);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to ensure disk cache directories at {Path}", _baseDirectory);
        }
    }

    public string GetCacheFilePath(string contentHash, ImageQuality quality, bool isPng = false)
    {
        string extension = isPng ? ".png" : ".jpg";
        string qualityTag = quality.ToString().ToLowerInvariant();
        return Path.Combine(_baseDirectory, $"{contentHash}_{qualityTag}{extension}");
    }

    public string GetMetadataFilePath(string contentHash, ImageQuality quality)
    {
        string qualityTag = quality.ToString().ToLowerInvariant();
        return Path.Combine(_baseDirectory, $"{contentHash}_{qualityTag}.meta");
    }

    public bool TryGetValidCachedFile(string contentHash, ImageQuality quality, out string? filePath)
    {
        filePath = null;
        if (string.IsNullOrWhiteSpace(contentHash)) return false;

        string jpgPath = GetCacheFilePath(contentHash, quality, isPng: false);
        string pngPath = GetCacheFilePath(contentHash, quality, isPng: true);

        string? candidate = File.Exists(jpgPath) ? jpgPath : (File.Exists(pngPath) ? pngPath : null);
        if (candidate == null) return false;

        try
        {
            var fi = new FileInfo(candidate);
            if (fi.Length <= 0)
            {
                // Corrupt / 0-byte file, delete it
                try { File.Delete(candidate); } catch { }
                return false;
            }

            filePath = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string?> SaveAtomicAsync(
        string contentHash,
        ImageQuality quality,
        byte[] data,
        bool isPng = false,
        string? metadataText = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) return null;

        EnsureDirectories();
        string targetPath = GetCacheFilePath(contentHash, quality, isPng);
        string metaPath = GetMetadataFilePath(contentHash, quality);
        string tempFile = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.tmp");
        string tempMeta = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.tmpmeta");

        try
        {
            await File.WriteAllBytesAsync(tempFile, data, cancellationToken).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(metadataText))
            {
                await File.WriteAllTextAsync(tempMeta, metadataText, cancellationToken).ConfigureAwait(false);
            }

            // Atomic move/replace
            File.Move(tempFile, targetPath, overwrite: true);

            if (File.Exists(tempMeta))
            {
                File.Move(tempMeta, metaPath, overwrite: true);
            }

            return targetPath;
        }
        catch (OperationCanceledException)
        {
            TryDeleteFile(tempFile);
            TryDeleteFile(tempMeta);
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to write disk cache file for {Hash}", contentHash);
            TryDeleteFile(tempFile);
            TryDeleteFile(tempMeta);
            return null;
        }
    }

    public string? SaveAtomicSync(
        string contentHash,
        ImageQuality quality,
        byte[] data,
        bool isPng = false,
        string? metadataText = null)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) return null;

        EnsureDirectories();
        string targetPath = GetCacheFilePath(contentHash, quality, isPng);
        string metaPath = GetMetadataFilePath(contentHash, quality);
        string tempFile = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.tmp");
        string tempMeta = Path.Combine(_tempDirectory, $"{Guid.NewGuid():N}.tmpmeta");

        try
        {
            File.WriteAllBytes(tempFile, data);

            if (!string.IsNullOrEmpty(metadataText))
            {
                File.WriteAllText(tempMeta, metadataText);
            }

            File.Move(tempFile, targetPath, overwrite: true);

            if (File.Exists(tempMeta))
            {
                File.Move(tempMeta, metaPath, overwrite: true);
            }

            return targetPath;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to write disk cache file for {Hash}", contentHash);
            TryDeleteFile(tempFile);
            TryDeleteFile(tempMeta);
            return null;
        }
    }

    public void CleanOldTempFiles(TimeSpan maxAge)
    {
        lock (_cleanupGate)
        {
            try
            {
                if (!Directory.Exists(_tempDirectory)) return;
                var now = DateTime.UtcNow;
                var di = new DirectoryInfo(_tempDirectory);
                foreach (var file in di.GetFiles())
                {
                    try
                    {
                        if (now - file.LastWriteTimeUtc > maxAge)
                        {
                            file.Delete();
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Temp files cleanup encountered an issue");
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { }
    }
}

