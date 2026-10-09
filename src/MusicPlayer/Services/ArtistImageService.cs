using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Services;

public sealed class ArtistImageService : IDisposable
{
    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly string _cacheDir;
    private readonly ConcurrentDictionary<string, string> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private bool _disposed;

    // Music-related keywords used to validate Wikipedia results
    private static readonly string[] MusicKeywords =
    [
        "singer", "musician", "rapper", "band", "artist", "album", "song", "record",
        "discography", "music", "vocal", "guitarist", "drummer", "producer",
        "hip-hop", "hip hop", "pop", "rock", "jazz", "r&b", "rnb", "soul",
        "born", "american", "british", "french", "german", "canadian", "australian"
    ];

    public ArtistImageService(ILogger<ArtistImageService> logger)
    {
        _logger = logger;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("MusicPlayer/1.0 (compatible; MusicBrainz-friendly)");
        _cacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MusicPlayer", "ArtistImages");
        Directory.CreateDirectory(_cacheDir);
    }

    public string? GetCachedPath(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return null;

        var lower = artist.Trim().ToLowerInvariant();
        if (lower is "unknown" or "<unknown>" or "unknown artist" or "various artists" or
            "various" or "none" or "n/a" or ".misc" or "instrumental" or "soundtrack")
            return null;

        var key = NormalizeKey(artist);

        if (_memoryCache.TryGetValue(key, out var cached) && File.Exists(cached))
            return cached;

        var path = Path.Combine(_cacheDir, key + ".jpg");
        if (File.Exists(path))
        {
            _memoryCache[key] = path;
            return path;
        }
        return null;
    }

    public async Task<string?> GetArtistImageAsync(string artist, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(artist)) return null;

        // Skip unknown/placeholder artist names
        var lower = artist.Trim().ToLowerInvariant();
        if (lower is "unknown" or "<unknown>" or "unknown artist" or "various artists" or
            "various" or "none" or "n/a" or ".misc" or "instrumental" or "soundtrack")
            return null;

        // Skip very short names (likely not real artists)
        if (artist.Trim().Length <= 2) return null;

        // Skip numeric-only names
        if (System.Text.RegularExpressions.Regex.IsMatch(artist.Trim(), @"^\d+$")) return null;

        var key = NormalizeKey(artist);

        if (_memoryCache.TryGetValue(key, out var cached) && File.Exists(cached))
            return cached;

        var path = Path.Combine(_cacheDir, key + ".jpg");
        if (File.Exists(path))
        {
            _memoryCache[key] = path;
            return path;
        }

        try
        {
            var imageUrl = await SearchArtistImageAsync(artist, ct);
            if (string.IsNullOrEmpty(imageUrl)) return null;

            var data = await _http.GetByteArrayAsync(imageUrl, ct);
            if (data.Length < 2000) return null; // reject tiny/blank responses

            // ✅ Process through FaceCropHelper: smart square crop centered on face
            data = await FaceCropHelper.ProcessArtistImageAsync(data);
            if (data.Length < 100) return null;

            await File.WriteAllBytesAsync(path, data, ct);
            _memoryCache[key] = path;
            _logger.LogInformation("Cached artist image: {Artist} -> {Path}", artist, path);
            return path;
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch image for artist: {Artist}", artist);
            return null;
        }
    }

    /// <summary>
    /// Re-crops all existing cached images using FaceCropHelper so old bad caches are fixed.
    /// Deletes and clears memory cache for any artist that has a bad/corrupt cached image.
    /// Call this once on startup (fire-and-forget is fine).
    /// </summary>
    public async Task UpgradeExistingCacheAsync(CancellationToken ct = default)
    {
        try
        {
            var files = Directory.GetFiles(_cacheDir, "*.jpg");
            _logger.LogInformation("UpgradeExistingCache: upgrading {Count} cached images with face-crop", files.Length);

            var sem = new SemaphoreSlim(3);
            var tasks = files.Select(async file =>
            {
                await sem.WaitAsync(ct);
                try
                {
                    var info = new FileInfo(file);
                    // Skip very small files (probably already square/small or corrupt)
                    if (info.Length < 2000) return;

                    var original = await File.ReadAllBytesAsync(file, ct);

                    // Quick check: if already square-ish, skip to avoid re-processing
                    // (FaceCropHelper handles square input gracefully but wastes time)
                    var isAlreadySquare = await IsImageSquareAsync(original);
                    if (isAlreadySquare) return;

                    var processed = await FaceCropHelper.ProcessArtistImageAsync(original);
                    if (processed.Length > 1000)
                    {
                        await File.WriteAllBytesAsync(file, processed, ct);

                        // Clear memory cache so the next read picks up the new file
                        var key = Path.GetFileNameWithoutExtension(file);
                        _memoryCache.TryRemove(key, out _);

                        _logger.LogDebug("UpgradeExistingCache: re-cropped {File}", Path.GetFileName(file));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "UpgradeExistingCache: failed for {File}", Path.GetFileName(file));
                }
                finally
                {
                    sem.Release();
                }
            });

            await Task.WhenAll(tasks);
            _logger.LogInformation("UpgradeExistingCache: done");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "UpgradeExistingCache failed");
        }
    }

    private static Task<bool> IsImageSquareAsync(byte[] data)
    {
        try
        {
            using var ms = new MemoryStream(data);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                ms,
                System.Windows.Media.Imaging.BitmapCreateOptions.DelayCreation,
                System.Windows.Media.Imaging.BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var w = frame.PixelWidth;
            var h = frame.PixelHeight;
            // Consider it "square" if aspect ratio is within 10%
            return Task.FromResult(Math.Abs(w - h) <= Math.Min(w, h) * 0.10);
        }
        catch
        {
            return Task.FromResult(false);
        }
    }

    private async Task<string?> SearchArtistImageAsync(string artist, CancellationToken ct)
    {
        // Clean the artist name first
        var cleanName = CleanArtistName(artist);

        // 1. TheAudioDB (dedicated music artist photos, best quality, free tier)
        var url = await TryTheAudioDBAsync(cleanName, ct);
        if (url != null) return url;

        // 2. iTunes Search API
        url = await TryITunesAsync(cleanName, ct);
        if (url != null) return url;

        // 3. Deezer API
        url = await TryDeezerAsync(cleanName, ct);
        if (url != null) return url;

        // 4. Wikipedia direct page (with music keyword check)
        url = await TryWikipediaAsync(cleanName, ct);
        if (url != null) return url;

        // 5. Wikipedia search (with music keyword filtering)
        url = await TryWikipediaSearchAsync(cleanName, ct);
        if (url != null) return url;

        // If cleaned name differs from original, retry top sources with raw name
        if (!string.Equals(cleanName, artist, StringComparison.OrdinalIgnoreCase))
        {
            url = await TryTheAudioDBAsync(artist, ct);
            if (url != null) return url;

            url = await TryITunesAsync(artist, ct);
            if (url != null) return url;

            url = await TryDeezerAsync(artist, ct);
            if (url != null) return url;

            url = await TryWikipediaAsync(artist, ct);
            if (url != null) return url;
        }

        // 6. DuckDuckGo
        url = await TryDuckDuckGoAsync(cleanName, ct);
        if (url != null) return url;

        // 7. Wikidata P18 image property
        url = await TryWikidataAsync(cleanName, ct);
        if (url != null) return url;

        // 8. Discogs — band/artist logo database (free, no key)
        url = await TryDiscogsAsync(cleanName, ct);
        if (url != null) return url;

        // 9. YouTube official channel avatar (last resort)
        return await TryYouTubeChannelAsync(cleanName, ct);
    }

    // ────────────────────────────────────────────────────────────────────────
    //  SOURCE IMPLEMENTATIONS
    // ────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// TheAudioDB — free music artist photo database with dedicated artist thumbnails.
    /// Returns high-resolution portrait URL if found.
    /// </summary>
    private async Task<string?> TryTheAudioDBAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist);
            // Free API key "2" is the public demo key
            var apiUrl = $"https://www.theaudiodb.com/api/v1/json/2/search.php?s={encoded}";
            var resp = await _http.GetAsync(apiUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("artists", out var artists) ||
                artists.ValueKind == JsonValueKind.Null ||
                artists.GetArrayLength() == 0) return null;

            var a = artists[0];

            // Prefer strArtistThumb (portrait, best for circles) then strArtistFanart
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
            // Search both artist entity AND album entity to get artwork URLs
            var apiUrl = $"https://itunes.apple.com/search?term={encoded}&entity=musicArtist&limit=1&country=US";
            var resp = await _http.GetAsync(apiUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("results", out var results) &&
                results.GetArrayLength() > 0)
            {
                var artistData = results[0];
                // Try artistArtworkUrl first (large, 600x600)
                if (artistData.TryGetProperty("artistArtworkUrl", out var artwork))
                {
                    var u = artwork.GetString();
                    if (!string.IsNullOrEmpty(u)) return u;
                }
                // Try artworkUrl100 and upscale it
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
            var resp = await _http.GetAsync(apiUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.GetArrayLength() == 0) return null;

            // Pick the first result that has a real artist photo (not default Deezer placeholder)
            foreach (var artistItem in data.EnumerateArray())
            {
                // Validate artist name roughly matches
                var deezerName = "";
                if (artistItem.TryGetProperty("name", out var nameEl))
                    deezerName = nameEl.GetString() ?? "";

                if (!NamesRoughlyMatch(artist, deezerName)) continue;

                // Try picture_xl first, then picture_big
                foreach (var picProp in new[] { "picture_xl", "picture_big", "picture_medium" })
                {
                    if (artistItem.TryGetProperty(picProp, out var pic))
                    {
                        var u = pic.GetString();
                        // Skip default Deezer placeholder image
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
            var resp = await _http.GetAsync(apiUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            // Validate the article is music-related
            if (!IsMusicRelatedWikipediaResult(doc.RootElement)) return null;

            if (doc.RootElement.TryGetProperty("thumbnail", out var thumb) &&
                thumb.TryGetProperty("source", out var src))
            {
                var imgUrl = src.GetString();
                if (!string.IsNullOrEmpty(imgUrl))
                    return UpscaleWikipediaImage(imgUrl);
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryWikipediaSearchAsync(string artist, CancellationToken ct)
    {
        try
        {
            // Try multiple search queries with music qualifiers for better results
            var queries = new[]
            {
                $"{artist} musician",
                $"{artist} singer",
                $"{artist} rapper",
                $"{artist} band",
                artist
            };

            foreach (var query in queries)
            {
                var encoded = Uri.EscapeDataString(query);
                var apiUrl = $"https://en.wikipedia.org/w/api.php?action=query&list=search&srsearch={encoded}&srnamespace=0&srlimit=3&format=json";
                var resp = await _http.GetAsync(apiUrl, ct);
                if (!resp.IsSuccessStatusCode) continue;

                var json = await resp.Content.ReadAsStringAsync(ct);
                using var doc = JsonDocument.Parse(json);

                if (!doc.RootElement.TryGetProperty("query", out var queryEl) ||
                    !queryEl.TryGetProperty("search", out var search) ||
                    search.GetArrayLength() == 0) continue;

                foreach (var item in search.EnumerateArray())
                {
                    var title = item.GetProperty("title").GetString();
                    if (string.IsNullOrEmpty(title)) continue;
                    if (title.Contains("(disambiguation)", StringComparison.OrdinalIgnoreCase)) continue;

                    // Check snippet for music keywords before fetching the full page
                    var snippet = "";
                    if (item.TryGetProperty("snippet", out var snipEl))
                        snippet = snipEl.GetString() ?? "";

                    if (!ContainsMusicKeyword(snippet) && !ContainsMusicKeyword(title)) continue;

                    var imageUrl = await GetWikipediaImageWithValidationAsync(title, ct);
                    if (imageUrl != null) return imageUrl;
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> GetWikipediaImageWithValidationAsync(string pageTitle, CancellationToken ct)
    {
        try
        {
            var summaryUrl = $"https://en.wikipedia.org/api/rest_v1/page/summary/{Uri.EscapeDataString(pageTitle.Replace(" ", "_"))}";
            var summaryResp = await _http.GetAsync(summaryUrl, ct);
            if (!summaryResp.IsSuccessStatusCode) return null;

            var summaryJson = await summaryResp.Content.ReadAsStringAsync(ct);
            using var summaryDoc = JsonDocument.Parse(summaryJson);

            // Validate page is music-related
            if (!IsMusicRelatedWikipediaResult(summaryDoc.RootElement)) return null;

            if (summaryDoc.RootElement.TryGetProperty("thumbnail", out var thumb) &&
                thumb.TryGetProperty("source", out var src))
            {
                var imgUrl = src.GetString();
                if (!string.IsNullOrEmpty(imgUrl))
                    return UpscaleWikipediaImage(imgUrl);
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> TryDuckDuckGoAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist + " musician");
            var apiUrl = $"https://api.duckduckgo.com/?q={encoded}&format=json&no_html=1&skip_disambig=1";
            var resp = await _http.GetAsync(apiUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (doc.RootElement.TryGetProperty("Image", out var img))
            {
                var imgUrl = img.GetString();
                if (!string.IsNullOrEmpty(imgUrl) && imgUrl.StartsWith("http"))
                    return imgUrl;
            }

            if (doc.RootElement.TryGetProperty("Heading", out var heading))
            {
                var headingText = heading.GetString();
                if (!string.IsNullOrEmpty(headingText) &&
                    !string.Equals(headingText, artist, StringComparison.OrdinalIgnoreCase))
                {
                    return await TryWikipediaAsync(headingText, ct);
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
            var searchUrl = $"https://www.wikidata.org/w/api.php?action=wbsearchentities&search={encoded}&language=en&format=json&limit=3&type=item";
            var resp = await _http.GetAsync(searchUrl, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("search", out var results) ||
                results.GetArrayLength() == 0) return null;

            // Pick first result that has music-related description
            string? bestId = null;
            foreach (var result in results.EnumerateArray())
            {
                var desc = "";
                if (result.TryGetProperty("description", out var dEl))
                    desc = dEl.GetString() ?? "";

                if (ContainsMusicKeyword(desc))
                {
                    bestId = result.GetProperty("id").GetString();
                    break;
                }
                bestId ??= result.GetProperty("id").GetString();
            }

            if (string.IsNullOrEmpty(bestId)) return null;

            // Get the image property (P18)
            var entityUrl = $"https://www.wikidata.org/w/api.php?action=wbgetclaims&entity={bestId}&property=P18&format=json";
            var entityResp = await _http.GetAsync(entityUrl, ct);
            if (!entityResp.IsSuccessStatusCode) return null;

            var entityJson = await entityResp.Content.ReadAsStringAsync(ct);
            using var entityDoc = JsonDocument.Parse(entityJson);

            if (entityDoc.RootElement.TryGetProperty("claims", out var claims) &&
                claims.TryGetProperty("P18", out var p18) &&
                p18.GetArrayLength() > 0)
            {
                var mainsnak = p18[0].GetProperty("mainsnak");
                if (mainsnak.TryGetProperty("datavalue", out var dv) &&
                    dv.TryGetProperty("value", out var val))
                {
                    string? imageName = null;

                    // P18 value can be a string or an object with "value" key
                    if (val.ValueKind == JsonValueKind.String)
                        imageName = val.GetString();
                    else if (val.TryGetProperty("value", out var inner))
                        imageName = inner.GetString();

                    if (!string.IsNullOrEmpty(imageName))
                    {
                        var cleanFile = imageName.Replace(" ", "_");
                        var encoded2 = Uri.EscapeDataString(cleanFile);
                        var hash = GetCommonsHash(cleanFile);
                        return $"https://upload.wikimedia.org/wikipedia/commons/thumb/{hash}/{encoded2}/400px-{encoded2}";
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Discogs \u2014 music artist database with cover/logo images. Free API, no key required.
    /// Returns artist thumbnail or cover image URL if found.
    /// </summary>
    private async Task<string?> TryDiscogsAsync(string artist, CancellationToken ct)
    {
        try
        {
            var encoded = Uri.EscapeDataString(artist);
            // Search for artist type specifically
            var apiUrl = $"https://api.discogs.com/database/search?q={encoded}&type=artist&per_page=3";
            using var req = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            req.Headers.UserAgent.ParseAdd("MusicPlayer/1.0 +https://github.com/musicplayer");
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                results.GetArrayLength() == 0) return null;

            foreach (var item in results.EnumerateArray())
            {
                // Validate name matches
                var title = "";
                if (item.TryGetProperty("title", out var titleEl))
                    title = titleEl.GetString() ?? "";

                if (!NamesRoughlyMatch(artist, title)) continue;

                // Prefer cover_image (high-res) then thumb
                foreach (var prop in new[] { "cover_image", "thumb" })
                {
                    if (item.TryGetProperty(prop, out var imgEl))
                    {
                        var u = imgEl.GetString();
                        if (!string.IsNullOrEmpty(u) &&
                            u.StartsWith("http") &&
                            !u.Contains("spacer") &&
                            !u.Contains("no_image") &&
                            !u.Contains("anon_user"))
                            return u;
                    }
                }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// YouTube \u2014 scrapes the official channel page to extract the channel avatar image.
    /// Tries @handle, /c/, and /user/ URL patterns. Parses ytInitialData JSON from page HTML.
    /// Used as a last resort when all other music databases have failed.
    /// </summary>
    private async Task<string?> TryYouTubeChannelAsync(string artist, CancellationToken ct)
    {
        try
        {
            // Build candidate channel URL slugs
            var slug = System.Text.RegularExpressions.Regex.Replace(artist.ToLowerInvariant(), @"[^a-z0-9]", "");
            var slugSpaced = artist.Replace(" ", "").Replace("-", "").Replace("_", "");

            var candidates = new[]
            {
                $"https://www.youtube.com/@{Uri.EscapeDataString(artist.Replace(" ", ""))}",
                $"https://www.youtube.com/@{Uri.EscapeDataString(slugSpaced)}",
                $"https://www.youtube.com/c/{Uri.EscapeDataString(artist.Replace(" ", ""))}",
                $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(artist + " official music")}"
            };

            foreach (var channelUrl in candidates)
            {
                var avatarUrl = await FetchYouTubeAvatarFromPageAsync(channelUrl, ct);
                if (avatarUrl != null) return avatarUrl;
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> FetchYouTubeAvatarFromPageAsync(string pageUrl, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, pageUrl);
            req.Headers.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            req.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.9");
            req.Headers.Accept.ParseAdd("text/html,application/xhtml+xml");

            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var html = await resp.Content.ReadAsStringAsync(ct);

            // Extract avatar/channel thumbnail from ytInitialData or og:image
            // Pattern 1: yt3.googleusercontent.com avatar URLs in the page
            var avatarPattern = new System.Text.RegularExpressions.Regex(
                @"""url""\s*:\s*""(https://yt3\.googleusercontent\.com/[^""]+)""",
                System.Text.RegularExpressions.RegexOptions.None);

            var matches = avatarPattern.Matches(html);
            foreach (System.Text.RegularExpressions.Match m in matches)
            {
                var url = m.Groups[1].Value;
                // Skip very small thumbnails (s48, s68, etc.)
                if (url.Contains("=s48") || url.Contains("=s68") || url.Contains("=s88")) continue;

                // Prefer larger sizes; upgrade to =s400 if possible
                url = System.Text.RegularExpressions.Regex.Replace(url, @"=s\d+", "=s400");
                if (!url.Contains("=s")) url += "=s400";
                return url;
            }

            // Pattern 2: og:image meta tag (works on channel pages)
            var ogMatch = System.Text.RegularExpressions.Regex.Match(html,
                @"<meta property=""og:image"" content=""([^""]+)""");
            if (ogMatch.Success)
            {
                var u = ogMatch.Groups[1].Value;
                if (u.StartsWith("http") && !u.Contains("default_banner") && !u.Contains("no_thumbnail"))
                    return u;
            }
        }
        catch { }
        return null;
    }

    // ────────────────────────────────────────────────────────────────────────
    //  HELPER METHODS
    // ────────────────────────────────────────────────────────────────────────


    private static string CleanArtistName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return name;
        var cleaned = name.Trim();
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*-\s*Topic\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*-\s*Official\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*-\s*VEVO\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*-\s*Music\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*[\(\[].*?[\)\]]\s*$", "");
        cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s*[=:;,.!]+$", "");
        return cleaned.Trim();
    }

    /// <summary>
    /// Checks if a Wikipedia page summary is music-related by looking at the extract text.
    /// </summary>
    private static bool IsMusicRelatedWikipediaResult(JsonElement root)
    {
        var text = "";
        if (root.TryGetProperty("extract", out var extract))
            text += extract.GetString() ?? "";
        if (root.TryGetProperty("description", out var desc))
            text += " " + (desc.GetString() ?? "");
        if (root.TryGetProperty("title", out var title))
            text += " " + (title.GetString() ?? "");

        return ContainsMusicKeyword(text);
    }

    private static bool ContainsMusicKeyword(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        var lower = text.ToLowerInvariant();
        foreach (var kw in MusicKeywords)
        {
            if (lower.Contains(kw)) return true;
        }
        return false;
    }

    /// <summary>
    /// Rough name matching: both normalized to alphanumeric lowercase.
    /// Allows for minor differences like "feat.", "official", extra spaces.
    /// </summary>
    private static bool NamesRoughlyMatch(string a, string b)
    {
        if (string.IsNullOrEmpty(b)) return false;
        var na = NormalizeKey(a);
        var nb = NormalizeKey(b);
        // Accept if one starts with or contains the other (handles short abbreviations)
        return na == nb || na.StartsWith(nb) || nb.StartsWith(na) ||
               (na.Length > 4 && nb.Contains(na)) || (nb.Length > 4 && na.Contains(nb));
    }

    /// <summary>
    /// Upscale Wikipedia thumbnail URL from 220px to 500px for better quality.
    /// </summary>
    private static string UpscaleWikipediaImage(string url)
    {
        // Wikipedia thumbnails end with /NNNpx-filename
        url = System.Text.RegularExpressions.Regex.Replace(url, @"/\d+px-", "/500px-");
        return url;
    }

    private static string GetCommonsHash(string filename)
    {
        var clean = filename.Replace(" ", "_");
        var md5 = System.Security.Cryptography.MD5.HashData(System.Text.Encoding.UTF8.GetBytes(clean));
        return $"{md5[0]:x2}/{md5[0]:x2}{md5[1]:x2}";
    }

    public void RemoveCache(string artist)
    {
        var key = NormalizeKey(artist);
        _memoryCache.TryRemove(key, out _);
        var path = Path.Combine(_cacheDir, key + ".jpg");
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string NormalizeKey(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name.Trim().ToLowerInvariant(), @"[^a-z0-9]", "");

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _http.Dispose();
    }
}
