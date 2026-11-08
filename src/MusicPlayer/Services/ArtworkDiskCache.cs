using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows.Media.Imaging;
using MusicPlayer.Utilities;

namespace MusicPlayer.Services
{
    /// <summary>
    /// Multi-tier disk cache for pre-scaled artwork derivatives (80px, 160px, 240px, original).
    /// Eliminates repeated TagLib parsing and downscaling on subsequent application launches.
    /// </summary>
    public sealed class ArtworkDiskCache
    {
        private const string CacheVersion = ".v4";
        private readonly string _cacheDir;
        private readonly ConcurrentDictionary<string, string> _hashCache = new(StringComparer.OrdinalIgnoreCase);

        public ArtworkDiskCache(string? cacheDirectory = null)
        {
            _cacheDir = cacheDirectory ?? AppPaths.AlbumArtCachePath;
            Directory.CreateDirectory(_cacheDir);
            EnsureCacheVersion();
        }

        private void EnsureCacheVersion()
        {
            try
            {
                var flagPath = Path.Combine(_cacheDir, CacheVersion);
                if (File.Exists(flagPath)) return;

                // Safe migration: write the version flag to anchor v4 caching
                File.WriteAllText(flagPath, "MusicPlayer Artwork Cache v4 Derivative Pipeline");
            }
            catch { }
        }

        public string GetHash(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            return _hashCache.GetOrAdd(key, k =>
            {
                using var md5 = MD5.Create();
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(k));
                var sb = new StringBuilder(bytes.Length * 2);
                foreach (var b in bytes)
                    sb.Append(b.ToString("x2"));
                return sb.ToString();
            });
        }

        public string GetDerivativePath(string key, ArtworkQuality quality, bool isPng = false)
        {
            var hash = GetHash(key);
            string ext = isPng ? "png" : "jpg";
            if (quality == ArtworkQuality.Full)
                return Path.Combine(_cacheDir, $"{hash}_orig.{ext}");

            return Path.Combine(_cacheDir, $"{hash}_{(int)quality}.{ext}");
        }

        public string GetMetaPath(string key)
        {
            var hash = GetHash(key);
            return Path.Combine(_cacheDir, $"{hash}.meta");
        }

        public string GetNoArtPath(string key)
        {
            var hash = GetHash(key);
            return Path.Combine(_cacheDir, $"{hash}.noart");
        }

        /// <summary>
        /// Checks if a valid pre-scaled derivative exists on disk for the given source file.
        /// Validates source file modification ticks to guarantee freshness.
        /// </summary>
        public bool TryGetDerivativeFile(string sourcePath, ArtworkQuality quality, out string? filePath, out bool isExactSize)
        {
            filePath = null;
            isExactSize = false;

            if (string.IsNullOrWhiteSpace(sourcePath)) return false;

            try
            {
                long currentTicks = 0;
                if (File.Exists(sourcePath))
                {
                    currentTicks = File.GetLastWriteTimeUtc(sourcePath).Ticks;
                }

                // Check negative cache on disk
                var noArtPath = GetNoArtPath(sourcePath);
                if (File.Exists(noArtPath))
                {
                    if (ValidateTicks(noArtPath, currentTicks))
                    {
                        Interlocked.Increment(ref ArtworkMetrics.NegativeHits);
                        return false;
                    }
                    try { File.Delete(noArtPath); } catch { }
                }

                var metaPath = GetMetaPath(sourcePath);
                if (!ValidateTicks(metaPath, currentTicks))
                {
                    // Source file changed since cache generation: invalidate stale entries
                    InvalidateCache(sourcePath);
                    return false;
                }

                // Check exact derivative
                string exactJpg = GetDerivativePath(sourcePath, quality, false);
                if (File.Exists(exactJpg))
                {
                    filePath = exactJpg;
                    isExactSize = true;
                    Interlocked.Increment(ref ArtworkMetrics.DiskHits);
                    return true;
                }

                string exactPng = GetDerivativePath(sourcePath, quality, true);
                if (File.Exists(exactPng))
                {
                    filePath = exactPng;
                    isExactSize = true;
                    Interlocked.Increment(ref ArtworkMetrics.DiskHits);
                    return true;
                }

                // Check extracted original on disk (can be downscaled without TagLib re-read)
                string origJpg = GetDerivativePath(sourcePath, ArtworkQuality.Full, false);
                if (File.Exists(origJpg))
                {
                    filePath = origJpg;
                    isExactSize = false;
                    Interlocked.Increment(ref ArtworkMetrics.DiskHits);
                    return true;
                }

                string origPng = GetDerivativePath(sourcePath, ArtworkQuality.Full, true);
                if (File.Exists(origPng))
                {
                    filePath = origPng;
                    isExactSize = false;
                    Interlocked.Increment(ref ArtworkMetrics.DiskHits);
                    return true;
                }
            }
            catch { }

            Interlocked.Increment(ref ArtworkMetrics.DiskMisses);
            return false;
        }

        public void SaveDerivative(string sourcePath, ArtworkQuality quality, BitmapSource image, long sourceTicks)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || image == null || quality == ArtworkQuality.Full) return;

            try
            {
                string targetPath = GetDerivativePath(sourcePath, quality, false);
                var encoder = new JpegBitmapEncoder { QualityLevel = 88 };
                encoder.Frames.Add(BitmapFrame.Create(image));

                using (var fs = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    encoder.Save(fs);
                }

                SaveMeta(sourcePath, sourceTicks);
            }
            catch { }
        }

        public string? SaveExtractedOriginal(string sourcePath, byte[] pictureData, long sourceTicks)
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || pictureData == null || pictureData.Length == 0) return null;

            try
            {
                bool isPng = pictureData.Length >= 8 &&
                             pictureData[0] == 0x89 &&
                             pictureData[1] == 0x50 &&
                             pictureData[2] == 0x4E &&
                             pictureData[3] == 0x47;

                string targetPath = GetDerivativePath(sourcePath, ArtworkQuality.Full, isPng);
                File.WriteAllBytes(targetPath, pictureData);
                SaveMeta(sourcePath, sourceTicks);
                return targetPath;
            }
            catch
            {
                return null;
            }
        }

        public void RecordNegativeDisk(string sourcePath, long sourceTicks)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return;
            try
            {
                var noArtPath = GetNoArtPath(sourcePath);
                File.WriteAllText(noArtPath, sourceTicks.ToString());
            }
            catch { }
        }

        public void InvalidateCache(string sourcePath)
        {
            if (string.IsNullOrWhiteSpace(sourcePath)) return;
            try
            {
                foreach (ArtworkQuality q in Enum.GetValues<ArtworkQuality>())
                {
                    string f1 = GetDerivativePath(sourcePath, q, false);
                    string f2 = GetDerivativePath(sourcePath, q, true);
                    if (File.Exists(f1)) File.Delete(f1);
                    if (File.Exists(f2)) File.Delete(f2);
                }
                var m = GetMetaPath(sourcePath);
                if (File.Exists(m)) File.Delete(m);
                var na = GetNoArtPath(sourcePath);
                if (File.Exists(na)) File.Delete(na);
            }
            catch { }
        }

        private void SaveMeta(string sourcePath, long sourceTicks)
        {
            try
            {
                var metaPath = GetMetaPath(sourcePath);
                File.WriteAllText(metaPath, $"{sourceTicks}|{CacheVersion}");
            }
            catch { }
        }

        private static bool ValidateTicks(string metaPath, long currentTicks)
        {
            if (currentTicks == 0 || !File.Exists(metaPath)) return true;
            try
            {
                var content = File.ReadAllText(metaPath);
                var parts = content.Split('|');
                if (parts.Length > 0 && long.TryParse(parts[0], out long cachedTicks))
                {
                    return cachedTicks == currentTicks;
                }
            }
            catch { }
            return true;
        }
    }
}

