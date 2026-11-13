using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using MusicPlayer.Services;

namespace MusicPlayer.ImageLoading;

public sealed class ArtistImageProvider : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger? _logger;
    private readonly string _cacheDir;
    private readonly ImageMemoryCache _memoryCache;
    private readonly ImageLoadScheduler _scheduler;
    private readonly SingleFlightCoordinator<string> _singleFlightPath = new();
    private readonly SingleFlightCoordinator<ImageSource> _singleFlightSource = new();
    private readonly CancellationTokenSource _cts = new();
    private bool _disposed;

    public ArtistImageProvider(
        ImageMemoryCache memoryCache,
        ImageLoadScheduler scheduler,
        ILogger? logger = null)
    {
        _memoryCache = memoryCache;
        _scheduler = scheduler;
        _logger = logger;
        _cacheDir = MusicPlayer.Utilities.AppPaths.ArtistImagesPath;

        try { Directory.CreateDirectory(_cacheDir); } catch { }

        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicPlayer/1.0 (compatible; MusicBrainz-friendly)");
    }

    public string CacheDirectory => _cacheDir;

    public static string NormalizeKey(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return string.Empty;
        var s = artist.Trim().ToLowerInvariant();
        s = Regex.Replace(s, @"\s*feat\.?.*$", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*ft\.?.*$", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*,.*$", "");
        s = Regex.Replace(s, @"\s*&.*$", "");
        s = Regex.Replace(s, @"[^a-z0-9\u0600-\u06FF\s]", "");
        s = Regex.Replace(s, @"\s+", "_").Trim('_');
        return s;
    }

    public static bool IsInvalidArtistName(string? artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return true;
        var lower = artist.Trim().ToLowerInvariant();
        if (lower is "unknown" or "<unknown>" or "unknown artist" or "various artists" or
            "various" or "none" or "n/a" or ".misc" or "instrumental" or "soundtrack")
            return true;
        if (artist.Trim().Length <= 2) return true;
        if (Regex.IsMatch(artist.Trim(), @"^\d+$")) return true;
        return false;
    }

    public string? GetCachedPath(string artist)
    {
        if (IsInvalidArtistName(artist)) return null;
        var key = NormalizeKey(artist);
        if (string.IsNullOrEmpty(key)) return null;

        var path = Path.Combine(_cacheDir, key + ".jpg");
        if (File.Exists(path))
        {
            return path;
        }

        // Legacy cache files were written without the separator ("Sub Urban" ->
        // "suburban.jpg"), while the normalizer now produces "sub_urban".
        // Without this fallback those already downloaded images were ignored and
        // the tile stayed on the placeholder until the artist was re-fetched.
        var compact = key.Replace("_", string.Empty);
        if (!string.IsNullOrEmpty(compact) && !string.Equals(compact, key, StringComparison.Ordinal))
        {
            var legacyPath = Path.Combine(_cacheDir, compact + ".jpg");
            if (File.Exists(legacyPath))
            {
                return legacyPath;
            }
        }

        return null;
    }

    public ImageSource? TryGetFromMemory(string artist, bool isHighRes)
    {
        if (IsInvalidArtistName(artist)) return null;
        var key = NormalizeKey(artist);
        var quality = isHighRes ? ImageQuality.Large : ImageQuality.Thumbnail;
        if (_memoryCache.TryGet(key, ImageKind.Artist, quality, out var cached) && cached != null)
        {
            return cached;
        }
        return null;
    }

    public async Task<string?> GetOrFetchArtistImagePathAsync(string artist, CancellationToken ct = default)
    {
        if (IsInvalidArtistName(artist)) return null;
        var key = NormalizeKey(artist);
        if (string.IsNullOrEmpty(key)) return null;

        var cached = GetCachedPath(artist);
        if (cached != null) return cached;

        return await _singleFlightPath.RunOrJoinAsync(
            key,
            async token =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, token);
                return await FetchAndSaveArtistImageAsync(artist, key, linked.Token).ConfigureAwait(false);
            },
            ct).ConfigureAwait(false);
    }

    public async Task<ImageSource?> GetOrLoadArtistImageAsync(
        string artist,
        bool isHighRes,
        ImagePriority priority,
        CancellationToken ct = default)
    {
        if (IsInvalidArtistName(artist)) return null;
        var key = NormalizeKey(artist);
        if (string.IsNullOrEmpty(key)) return null;

        var quality = isHighRes ? ImageQuality.Large : ImageQuality.Thumbnail;
        if (_memoryCache.TryGet(key, ImageKind.Artist, quality, out var memCached) && memCached != null)
        {
            return memCached;
        }

        string flightKey = $"{key}:{(int)quality}";
        return await _singleFlightSource.RunOrJoinAsync(
            flightKey,
            async token =>
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, token);
                string? path = await GetOrFetchArtistImagePathAsync(artist, linked.Token).ConfigureAwait(false);
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;

                var decoded = await _scheduler.ScheduleAsync(
                    OperationType.ImageDecode,
                    priority,
                    _ => Task.FromResult(ImageDecoder.DecodeFromFile(path, quality)),
                    linked.Token).ConfigureAwait(false);

                if (decoded != null)
                {
                    _memoryCache.Set(key, ImageKind.Artist, quality, decoded);
                }
                return decoded;
            },
            ct).ConfigureAwait(false);
    }

    private async Task<string?> FetchAndSaveArtistImageAsync(string artist, string key, CancellationToken ct)
    {
        string targetPath = Path.Combine(_cacheDir, key + ".jpg");
        string tempPath = Path.Combine(_cacheDir, $"{Guid.NewGuid():N}.tmp");

        try
        {
            // Use Background priority for network fetch - this is a background operation
            string? imageUrl = await _scheduler.ScheduleAsync(
                OperationType.NetworkFetch,
                ImagePriority.Background,
                token => SearchArtistImageAsync(artist, token),
                ct).ConfigureAwait(false);

            if (string.IsNullOrEmpty(imageUrl)) return null;

            byte[] data = await _scheduler.ScheduleAsync(
                OperationType.NetworkFetch,
                ImagePriority.Background,
                token => _http.GetByteArrayAsync(imageUrl, token),
                ct).ConfigureAwait(false);

            if (data == null || data.Length < 2000) return null;

            // Process with ImageProcessing priority (face crop)
            data = await _scheduler.ScheduleAsync(
                OperationType.ImageDecode, // Using ImageDecode gate for CPU-bound processing
                ImagePriority.Background,
                _ => FaceCropHelper.ProcessArtistImageAsync(data, ct: ct),
                ct).ConfigureAwait(false);
            if (data.Length < 100) return null;

            await File.WriteAllBytesAsync(tempPath, data, ct).ConfigureAwait(false);
            File.Move(tempPath, targetPath, overwrite: true);

            return targetPath;
        }
        catch (OperationCanceledException)
        {
            TryDelete(tempPath);
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Failed to fetch image for artist: {Artist}", artist);
            TryDelete(tempPath);
            return null;
        }
    }

    public async Task UpgradeExistingCacheAsync(CancellationToken ct = default)
    {
        try
        {
            var files = Directory.GetFiles(_cacheDir, "*.jpg");
            var sem = new SemaphoreSlim(2); // Limit concurrent processing
            var tasks = files.Select(async file =>
            {
                await sem.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length < 2000) return;

                    var original = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false);
                    var isAlreadySquare = await IsImageSquareAsync(original).ConfigureAwait(false);
                    if (isAlreadySquare) return;

                    // Use the scheduler with ImageProcessing priority
                    var processed = await _scheduler.ScheduleAsync(
                        OperationType.ImageDecode, // CPU-bound work
                        ImagePriority.Background,
                        _ => FaceCropHelper.ProcessArtistImageAsync(original, ct: ct),
                        ct).ConfigureAwait(false);

                    if (processed.Length > 1000)
                    {
                        string temp = file + ".upgtmp";
                        await File.WriteAllBytesAsync(temp, processed, ct).ConfigureAwait(false);
                        File.Move(temp, file, overwrite: true);

                        var key = Path.GetFileNameWithoutExtension(file);
                        _memoryCache.RemoveAllForSource(key);
                    }
                }
                catch { }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Artist image upgrade encountered an error");
        }
    }

    public void InvalidateArtist(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return;
        var key = NormalizeKey(artist);
        _memoryCache.RemoveAllForSource(key);
        var path = Path.Combine(_cacheDir, key + ".jpg");
        TryDelete(path);
    }

    private static Task<bool> IsImageSquareAsync(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            return Task.FromResult(Math.Abs(frame.PixelWidth - frame.PixelHeight) <= Math.Min(frame.PixelWidth, frame.PixelHeight) * 0.10);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private async Task<string?> SearchArtistImageAsync(string artist, CancellationToken ct)
    {
        var cleanName = CleanArtistName(artist);

        var url = await TryTheAudioDBAsync(cleanName, ct).ConfigureAwait(false);
        if (url != null) return url;

        url = await TryITunesAsync(cleanName, ct).ConfigureAwait(false);
        if (url != null) return url;

        url = await TryDeezerAsync(cleanName, ct).ConfigureAwait(false);
        if (url != null) return url;

        url = await TryWikipediaAsync(cleanName, ct).ConfigureAwait(false);
        if (url != null) return url;

        url = await TryWikipediaSearchAsync(cleanName, ct).ConfigureAwait(false);
        if (url != null) return url;

        if (!string.Equals(cleanName, artist, StringComparison.OrdinalIgnoreCase))
        {
            url = await TryTheAudioDBAsync(artist, ct).ConfigureAwait(false);
            if (url != null) return url;

            url = await TryITunesAsync(artist, ct).ConfigureAwait(false);
            if (url != null) return url;

            url = await TryDeezerAsync(artist, ct).ConfigureAwait(false);
            if (url != null) return url;

            url = await TryWikipediaAsync(artist, ct).ConfigureAwait(false);
            if (url != null) return url;
        }

        url = await TryDuckDuckGoAsync(cleanName, ct).ConfigureAwait(false);
        if (url != null) return url;

        url = await TryWikidataAsync(cleanName, ct).ConfigureAwait(false);
        if (url != null) return url;

        url = await TryDiscogsAsync(cleanName, ct).ConfigureAwait(false);
        if (url != null) return url;

        return await TryYouTubeChannelAsync(cleanName, ct).ConfigureAwait(false);
    }

    private static string CleanArtistName(string artist)
    {
        var s = artist.Trim();
        s = Regex.Replace(s, @"\s*feat\.?.*$", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*ft\.?.*$", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*\(.*?\)", "");
        s = Regex.Replace(s, @"\s*\[.*?\]", "");
        s = Regex.Replace(s, @"\s*,.*$", "");
        s = Regex.Replace(s, @"\s*&.*$", "");
        s = Regex.Replace(s, @"\s*vs\.?.*$", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*x\s+.*$", "", RegexOptions.IgnoreCase);
        return s.Trim();
    }

    private async Task<string?> TryTheAudioDBAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist);
            var apiUrl = $"https://www.theaudiodb.com/api/v1/json/2/search.php?s={encoded}";
            var resp = await _http.GetAsync(apiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("artists", out var artists) ||
                artists.ValueKind == JsonValueKind.Null ||
                artists.GetArrayLength() == 0) return null;

            var a = artists[0];
            foreach (var prop in new[] { "strArtistThumb", "strArtistCutout", "strArtistFanart" })
            {
                if (a.TryGetProperty(prop, out var val))
                {
                    var u = val.GetString();
                    if (!string.IsNullOrEmpty(u) && u.StartsWith("http"))
                        return u;
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryITunesAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist);
            var apiUrl = $"https://itunes.apple.com/search?term={encoded}&entity=musicArtist&limit=1&country=US";
            var resp = await _http.GetAsync(apiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("results", out var results) &&
                results.GetArrayLength() > 0)
            {
                var artistData = results[0];
                if (artistData.TryGetProperty("artistArtworkUrl", out var artwork))
                {
                    var u = artwork.GetString();
                    if (!string.IsNullOrEmpty(u)) return u;
                }
                if (artistData.TryGetProperty("artworkUrl100", out var art100))
                {
                    var u = art100.GetString();
                    if (!string.IsNullOrEmpty(u))
                        return u.Replace("100x100bb", "600x600bb").Replace("100x100", "600x600");
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryDeezerAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist);
            var apiUrl = $"https://api.deezer.com/search/artist?q={encoded}&limit=3";
            var resp = await _http.GetAsync(apiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.GetArrayLength() == 0) return null;

            foreach (var artistItem in data.EnumerateArray())
            {
                foreach (var picProp in new[] { "picture_xl", "picture_big", "picture_medium" })
                {
                    if (artistItem.TryGetProperty(picProp, out var pic))
                    {
                        var u = pic.GetString();
                        if (!string.IsNullOrEmpty(u) && !u.Contains("default") && !u.Contains("1000000"))
                            return u;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryWikipediaAsync(string artist, CancellationToken ct)
    {
        try
        {
            var pageName = artist.Replace(" ", "_");
            var encoded = Uri.EscapeDataString(pageName);
            var apiUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{encoded}";
            var resp = await _http.GetAsync(apiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("thumbnail", out var thumb) &&
                thumb.TryGetProperty("source", out var src))
            {
                var imgUrl = src.GetString();
                if (!string.IsNullOrEmpty(imgUrl))
                    return imgUrl;
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryWikipediaSearchAsync(string artist, CancellationToken ct)
    {
        try
        {
            var query = Uri.EscapeDataString(artist + " musician");
            var searchUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={query}&format=json&srlimit=1";
            var resp = await _http.GetAsync(searchUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("query", out var q) &&
                q.TryGetProperty("search", out var search) &&
                search.GetArrayLength() > 0)
            {
                var title = search[0].GetProperty("title").GetString();
                if (!string.IsNullOrEmpty(title))
                    return await TryWikipediaAsync(title, ct).ConfigureAwait(false);
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryDuckDuckGoAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist);
            var apiUrl = $"https://api.duckduckgo.com/?q={encoded}&format=json&no_html=1&skip_disambig=1";
            var resp = await _http.GetAsync(apiUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Image", out var img))
            {
                var u = img.GetString();
                if (!string.IsNullOrEmpty(u))
                {
                    if (u.StartsWith("/")) u = "https://duckduckgo.com" + u;
                    return u;
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryWikidataAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist);
            var searchUrl = $"https://www.wikidata.org/w/api.php?action=wbsearchentities&search={encoded}&language=en&format=json&limit=1";
            var resp = await _http.GetAsync(searchUrl, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("search", out var results) &&
                results.GetArrayLength() > 0)
            {
                var entityId = results[0].GetProperty("id").GetString();
                if (string.IsNullOrEmpty(entityId)) return null;

                var entityUrl = $"https://www.wikidata.org/wiki/Special:EntityData/{entityId}.json";
                var entityResp = await _http.GetAsync(entityUrl, ct).ConfigureAwait(false);
                if (!entityResp.IsSuccessStatusCode) return null;

                var entityJson = await entityResp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var entityDoc = JsonDocument.Parse(entityJson);

                if (entityDoc.RootElement.TryGetProperty("entities", out var entities) &&
                    entities.TryGetProperty(entityId, out var entity) &&
                    entity.TryGetProperty("claims", out var claims) &&
                    claims.TryGetProperty("P18", out var p18) &&
                    p18.GetArrayLength() > 0)
                {
                    var imgName = p18[0].GetProperty("mainsnak").GetProperty("datavalue").GetProperty("value").GetString();
                    if (!string.IsNullOrEmpty(imgName))
                    {
                        var encodedImg = Uri.EscapeDataString(imgName.Replace(" ", "_"));
                        return $"https://commons.wikimedia.org/wiki/Special:FilePath/{encodedImg}?width=500";
                    }
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryDiscogsAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist);
            var url = $"https://api.discogs.com/database/search?q={encoded}&type=artist&per_page=1";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("MusicPlayer/1.0");
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("results", out var results) &&
                results.GetArrayLength() > 0)
            {
                var item = results[0];
                if (item.TryGetProperty("cover_image", out var cover))
                {
                    var u = cover.GetString();
                    if (!string.IsNullOrEmpty(u) && !u.Contains("spacer.gif"))
                        return u;
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryYouTubeChannelAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist + " topic");
            var searchUrl = $"https://www.youtube.com/results?search_query={encoded}&sp=EgIQAg%253D%253D";
            using var req = new HttpRequestMessage(HttpMethod.Get, searchUrl);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0");
            var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            var html = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var m = Regex.Match(html, @"yt3\.ggpht\.com/([a-zA-Z0-9_-]+)=s(\d+)");
            if (m.Success)
            {
                return $"https://yt3.ggpht.com/{m.Groups[1].Value}=s600-c-k-c0x00ffffff-no-rj";
            }
        }
        catch { }
        return null;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _cts.Cancel(); } catch { }
        _http.Dispose();
        _cts.Dispose();
    }
}

