using SQLite;
using MusicPlayer.Models;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Database;

public class DatabaseService
{
    private readonly SQLiteConnection _db;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseService(string dbPath, ILogger<DatabaseService> logger)
    {
        _logger = logger;
        _db = new SQLiteConnection(dbPath);
        Initialize();
    }

    private void Initialize()
    {
        try
        {
            _db.CreateTable<MusicTrack>();
            _db.CreateTable<Playlist>();
            _db.CreateTable<PlaylistTrack>();
            _db.CreateTable<QueueItem>();
            _db.CreateTable<HiddenFolder>();
            _db.CreateTable<Setting>();
            _db.CreateTable<AlbumArtCache>();
            _db.CreateTable<LyricBinding>();
            _db.CreateTable<ManagedMusicFolder>();
            
            _logger.LogInformation("Database initialized successfully at {Path}", _db.DatabasePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize database");
            throw;
        }
    }

    // Tracks
    public List<MusicTrack> GetAllTracks() => _db.Table<MusicTrack>().Where(t => !t.IsHidden).ToList();
    
    public List<MusicTrack> GetManagedFolderTracks()
    {
        var managedPaths = _db.Table<ManagedMusicFolder>()
            .Where(f => f.Enabled)
            .Select(f => f.Path)
            .ToList();
        
        if (managedPaths.Count == 0)
            return new List<MusicTrack>();
        
        var allTracks = _db.Table<MusicTrack>().ToList();
        return allTracks.Where(t => 
            !t.IsHidden &&
            !string.IsNullOrEmpty(t.FilePath) &&
            managedPaths.Any(mp => t.FilePath.StartsWith(mp, StringComparison.OrdinalIgnoreCase)))
            .ToList();
    }
    
    public List<MusicTrack> GetManagedFolderTracksSorted(string sortBy = "date_added", bool descending = true)
    {
        var tracks = GetManagedFolderTracks();
        return sortBy switch
        {
            "date_added" => descending ? tracks.OrderByDescending(t => t.DateAdded).ToList() : tracks.OrderBy(t => t.DateAdded).ToList(),
            "duration" => descending ? tracks.OrderByDescending(t => t.Duration).ToList() : tracks.OrderBy(t => t.Duration).ToList(),
            "size" => descending ? tracks.OrderByDescending(t => t.FileSize).ToList() : tracks.OrderBy(t => t.FileSize).ToList(),
            "year" => descending ? tracks.OrderByDescending(t => t.Year).ToList() : tracks.OrderBy(t => t.Year).ToList(),
            _ => descending ? tracks.OrderByDescending(t => t.Title ?? "").ToList() : tracks.OrderBy(t => t.Title ?? "").ToList()
        };
    }
    
    public List<MusicTrack> GetHiddenTracks() => _db.Table<MusicTrack>().Where(t => t.IsHidden).ToList();

    public int GetManagedFolderTrackCount()
    {
        var managedPaths = _db.Table<ManagedMusicFolder>()
            .Where(f => f.Enabled)
            .Select(f => f.Path)
            .ToList();
        
        if (managedPaths.Count == 0) return 0;
        
        return _db.Table<MusicTrack>().Count(t =>
            !t.IsHidden &&
            !string.IsNullOrEmpty(t.FilePath) &&
            managedPaths.Any(mp => t.FilePath.StartsWith(mp)));
    }
    
    public void CleanOrphanTracks()
    {
        var managedPaths = _db.Table<ManagedMusicFolder>()
            .Where(f => f.Enabled)
            .Select(f => f.Path)
            .ToList();
        
        if (managedPaths.Count == 0)
        {
            _db.Execute("DELETE FROM tracks");
            return;
        }
        
        var allTracks = _db.Table<MusicTrack>().ToList();
        foreach (var track in allTracks)
        {
            if (string.IsNullOrEmpty(track.FilePath)) continue;
            var belongsToManaged = managedPaths.Any(mp => 
                track.FilePath.StartsWith(mp, StringComparison.OrdinalIgnoreCase));
            if (!belongsToManaged)
            {
                _db.Delete<MusicTrack>(track.Id);
            }
        }
    }
    
    public MusicTrack? GetTrack(int id) => _db.Table<MusicTrack>().FirstOrDefault(t => t.Id == id);
    
    public MusicTrack? GetTrackByPath(string path) => _db.Table<MusicTrack>().FirstOrDefault(t => t.FilePath == path);

    /// <summary>
    /// Deletes any track whose file no longer exists on disk (deleted/moved),
    /// so the library stays in sync with the actual music folder.
    /// </summary>
    public void RemoveMissingTracks()
    {
        var allTracks = _db.Table<MusicTrack>().ToList();
        int removed = 0;
        foreach (var track in allTracks)
        {
            if (string.IsNullOrEmpty(track.FilePath)) continue;
            if (!System.IO.File.Exists(track.FilePath))
            {
                _db.Delete<MusicTrack>(track.Id);
                removed++;
            }
        }
        if (removed > 0)
            _logger.LogInformation("Removed {Count} tracks whose files no longer exist", removed);
    }
    
    public int InsertTrack(MusicTrack track) => _db.Insert(track);
    
    public void UpdateTrack(MusicTrack track) => _db.Update(track);
    
    public void DeleteTrack(int id) => _db.Delete<MusicTrack>(id);

    public int GetTrackCount() => _db.Table<MusicTrack>().Count(t => !t.IsHidden);
    public int GetFavoriteCount() => _db.Table<MusicTrack>().Count(t => !t.IsHidden && t.IsFavorite);
    public int GetFolderCount() => _db.Table<MusicTrack>().Where(t => !t.IsHidden).Select(t => System.IO.Path.GetDirectoryName(t.FilePath)).Distinct().Count();
    public int GetRecentPlayCount() => _db.Table<MusicTrack>().Count(t => !t.IsHidden && t.DateLastPlayed > 0);
    public int GetRecentAddCount()
    {
        long thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds();
        return _db.Table<MusicTrack>().Count(t => !t.IsHidden && t.DateAdded > thirtyDaysAgo);
    }
    public int GetMostPlayedCount() => _db.Table<MusicTrack>().Count(t => !t.IsHidden && t.PlayCount > 0);

    public (int TrackCount, int FolderCount, int FavoriteCount, int RecentPlayCount, int RecentAddCount, int MostPlayedCount) GetAllCounts()
    {
        var managedPaths = GetManagedFolderPaths();
        if (managedPaths.Count == 0)
            return (0, 0, 0, 0, 0, 0);
        
        var allTracks = _db.Table<MusicTrack>().ToList()
            .Where(t => !t.IsHidden && IsTrackInManagedFolder(t, managedPaths))
            .ToList();
        
        int trackCount = allTracks.Count;
        int folderCount = managedPaths.Count;
        int favoriteCount = allTracks.Count(t => t.IsFavorite);
        int recentPlayCount = allTracks.Count(t => t.DateLastPlayed > 0);
        long thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds();
        int recentAddCount = allTracks.Count(t => t.DateAdded > thirtyDaysAgo);
        int mostPlayedCount = allTracks.Count(t => t.PlayCount > 0);
        return (trackCount, folderCount, favoriteCount, recentPlayCount, recentAddCount, mostPlayedCount);
    }

    public Dictionary<string, int> GetFolderTrackCounts()
    {
        var managedPaths = GetManagedFolderPaths();
        var tracks = _db.Table<MusicTrack>().Select(t => t.FilePath).ToList();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in tracks)
        {
            if (managedPaths.Count > 0 && !managedPaths.Any(mp => p.StartsWith(mp, StringComparison.OrdinalIgnoreCase))) continue;
            var dir = System.IO.Path.GetDirectoryName(p);
            if (string.IsNullOrEmpty(dir)) continue;
            if (counts.ContainsKey(dir)) counts[dir]++;
            else counts[dir] = 1;
        }
        return counts;
    }

    public Dictionary<int, int> GetAllPlaylistTrackCounts()
    {
        var all = _db.Table<PlaylistTrack>().Select(pt => pt.PlaylistId).ToList();
        var counts = new Dictionary<int, int>();
        foreach (var playlistId in all)
        {
            if (counts.ContainsKey(playlistId)) counts[playlistId]++;
            else counts[playlistId] = 1;
        }
        return counts;
    }
    
    public int InsertOrIgnoreTrack(MusicTrack track)
    {
        var existing = GetTrackByPath(track.FilePath);
        if (existing == null) return _db.Insert(track);

        existing.Title = track.Title;
        existing.Artist = track.Artist;
        existing.Album = track.Album;
        existing.AlbumArtist = track.AlbumArtist;
        existing.Genre = track.Genre;
        existing.Year = track.Year;
        existing.TrackNumber = track.TrackNumber;
        existing.DiscNumber = track.DiscNumber;
        existing.Duration = track.Duration;
        existing.FileSize = track.FileSize;
        existing.Bitrate = track.Bitrate;
        existing.SampleRate = track.SampleRate;
        existing.Channels = track.Channels;
        existing.FolderPath = track.FolderPath;
        existing.DateModified = track.DateModified;
        if (!string.IsNullOrEmpty(track.AlbumArtPath))
            existing.AlbumArtPath = track.AlbumArtPath;
        _db.Update(existing);
        return existing.Id;
    }

    public void BeginTransaction() => _db.BeginTransaction();

    public void Commit() => _db.Commit();

    public void Rollback() => _db.Rollback();

    // Playlists
    public List<Playlist> GetAllPlaylists() => _db.Table<Playlist>().OrderBy(p => p.SortOrder).ToList();
    
    public Playlist? GetPlaylist(int id) => _db.Table<Playlist>().FirstOrDefault(p => p.Id == id);
    
    public int InsertPlaylist(Playlist playlist) => _db.Insert(playlist);
    
    public void UpdatePlaylist(Playlist playlist) => _db.Update(playlist);
    
    public void DeletePlaylist(int id)
    {
        _db.Table<PlaylistTrack>().Where(pt => pt.PlaylistId == id).Delete();
        _db.Delete<Playlist>(id);
    }

    public List<MusicTrack> GetPlaylistTracks(int playlistId)
    {
        var trackIds = _db.Table<PlaylistTrack>()
            .Where(pt => pt.PlaylistId == playlistId)
            .OrderBy(pt => pt.Position)
            .Select(pt => pt.TrackId)
            .ToList();
        
        return trackIds.Select(id => _db.Table<MusicTrack>().FirstOrDefault(t => t.Id == id))
            .Where(t => t != null)
            .Cast<MusicTrack>()
            .ToList();
    }

    public void AddTrackToPlaylist(int playlistId, int trackId, int position)
    {
        _db.Insert(new PlaylistTrack
        {
            PlaylistId = playlistId,
            TrackId = trackId,
            Position = position
        });
    }

    public void RemoveTrackFromPlaylist(int playlistId, int trackId)
    {
        _db.Table<PlaylistTrack>()
            .Where(pt => pt.PlaylistId == playlistId && pt.TrackId == trackId)
            .Delete();
    }

    // Managed Folders
    public List<ManagedMusicFolder> GetManagedFolders() =>
        _db.Table<ManagedMusicFolder>().Where(f => f.Enabled).OrderBy(f => f.Name).ToList();

    public List<ManagedMusicFolder> GetAllManagedFolders() =>
        _db.Table<ManagedMusicFolder>().OrderBy(f => f.Name).ToList();

    public ManagedMusicFolder? GetManagedFolder(string path) =>
        _db.Table<ManagedMusicFolder>().FirstOrDefault(f => f.Path == path);

    public int AddManagedFolder(ManagedMusicFolder folder)
    {
        var existing = GetManagedFolder(folder.Path);
        if (existing != null) return existing.Id;
        return _db.Insert(folder);
    }

    public void RemoveManagedFolder(int id) =>
        _db.Delete<ManagedMusicFolder>(id);

    public void UpdateManagedFolderLastScan(int id) =>
        _db.Execute("UPDATE managed_folders SET LastScanDate = ? WHERE Id = ?",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), id);

    // Queue
    public List<MusicTrack> GetQueue()
    {
        var items = _db.Table<QueueItem>().OrderBy(q => q.Position).ToList();
        var managedPaths = GetManagedFolderPaths();
        return items.Select(i => _db.Table<MusicTrack>().FirstOrDefault(t => t.Id == i.TrackId))
            .Where(t => t != null && IsTrackInManagedFolder(t!, managedPaths))
            .Cast<MusicTrack>()
            .ToList();
    }

    public void SaveQueue(List<QueueItem> items)
    {
        _db.DeleteAll<QueueItem>();
        _db.InsertAll(items);
    }

    // Favorites
    public List<MusicTrack> GetFavoriteTracks()
    {
        var managedPaths = GetManagedFolderPaths();
        if (managedPaths.Count == 0) return new List<MusicTrack>();
        return _db.Table<MusicTrack>().Where(t => !t.IsHidden && t.IsFavorite).ToList()
            .Where(t => IsTrackInManagedFolder(t, managedPaths))
            .OrderBy(t => t.Title).ToList();
    }

    public void ToggleFavorite(int trackId)
    {
        var track = GetTrack(trackId);
        if (track != null)
        {
            track.IsFavorite = !track.IsFavorite;
            UpdateTrack(track);
        }
    }

    // Recent/Most played
    public List<MusicTrack> GetRecentPlay(int limit = 50)
    {
        var managedPaths = GetManagedFolderPaths();
        if (managedPaths.Count == 0) return new List<MusicTrack>();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && t.DateLastPlayed > 0)
            .ToList()
            .Where(t => IsTrackInManagedFolder(t, managedPaths))
            .OrderByDescending(t => t.DateLastPlayed)
            .Take(limit)
            .ToList();
    }

    public List<MusicTrack> GetMostPlay(int limit = 50)
    {
        var managedPaths = GetManagedFolderPaths();
        if (managedPaths.Count == 0) return new List<MusicTrack>();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && t.PlayCount > 0)
            .ToList()
            .Where(t => IsTrackInManagedFolder(t, managedPaths))
            .OrderByDescending(t => t.PlayCount)
            .Take(limit)
            .ToList();
    }

    public List<MusicTrack> GetRecentAdd(int limit = 50)
    {
        var managedPaths = GetManagedFolderPaths();
        if (managedPaths.Count == 0) return new List<MusicTrack>();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden)
            .OrderByDescending(t => t.DateAdded)
            .ToList()
            .Where(t => IsTrackInManagedFolder(t, managedPaths))
            .Take(limit)
            .ToList();
    }

    // Helper: get managed folder paths
    private List<string> GetManagedFolderPaths()
    {
        return _db.Table<ManagedMusicFolder>()
            .Where(f => f.Enabled)
            .Select(f => f.Path)
            .ToList();
    }

    // Helper: check if track belongs to a managed folder
    private static bool IsTrackInManagedFolder(MusicTrack track, List<string> managedPaths)
    {
        if (string.IsNullOrEmpty(track.FilePath)) return false;
        return managedPaths.Any(mp => track.FilePath.StartsWith(mp, StringComparison.OrdinalIgnoreCase));
    }

    // Hidden folders
    public List<HiddenFolder> GetHiddenFolders() => _db.Table<HiddenFolder>().ToList();
    
    public void AddHiddenFolder(string path) =>
        _db.Insert(new HiddenFolder { FolderPath = path });
    
    public void RemoveHiddenFolder(string path) =>
        _db.Table<HiddenFolder>().Where(h => h.FolderPath == path).Delete();

    // Settings
    public string GetSetting(string key, string defaultValue = "")
    {
        var setting = _db.Table<Setting>().FirstOrDefault(s => s.Key == key);
        return setting?.Value ?? defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        var existing = _db.Table<Setting>().FirstOrDefault(s => s.Key == key);
        if (existing != null)
        {
            existing.Value = value;
            _db.Update(existing);
        }
        else
        {
            _db.Insert(new Setting { Key = key, Value = value });
        }
    }

    public int GetSettingInt(string key, int defaultValue = 0)
    {
        var value = GetSetting(key, defaultValue.ToString());
        return int.TryParse(value, out int result) ? result : defaultValue;
    }

    public bool GetSettingBool(string key, bool defaultValue = false)
    {
        var value = GetSetting(key, defaultValue.ToString());
        return bool.TryParse(value, out bool result) ? result : defaultValue;
    }

    public float GetSettingFloat(string key, float defaultValue = 0f)
    {
        var value = GetSetting(key, defaultValue.ToString());
        return float.TryParse(value, out float result) ? result : defaultValue;
    }

    // Album art cache
    public AlbumArtCache? GetAlbumArtCache(string trackPath) =>
        _db.Table<AlbumArtCache>().FirstOrDefault(a => a.TrackPath == trackPath);

    public void SaveAlbumArtCache(AlbumArtCache cache) => _db.InsertOrReplace(cache);

    // Lyrics
    public LyricBinding? GetLyricBinding(string trackPath) =>
        _db.Table<LyricBinding>().FirstOrDefault(l => l.TrackPath == trackPath);

    public void SaveLyricBinding(LyricBinding binding) => _db.InsertOrReplace(binding);

    // Search
    public List<MusicTrack> SearchTracks(string query)
    {
        var lowerQuery = query.ToLower();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && (t.Title.ToLower().Contains(lowerQuery) ||
                        t.Artist.ToLower().Contains(lowerQuery) ||
                        t.Album.ToLower().Contains(lowerQuery)))
            .OrderBy(t => t.Title)
            .ToList();
    }

    // Albums
    public List<string> GetAllAlbums() =>
        _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && !string.IsNullOrEmpty(t.Album))
            .Select(t => t.Album)
            .Distinct()
            .OrderBy(a => a)
            .ToList();

    public List<MusicTrack> GetAlbumTracks(string album)
    {
        var key = album.Trim();
        return _db.Table<MusicTrack>()
            .ToList()
            .Where(t => !t.IsHidden && string.Equals(t.Album?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.TrackNumber)
            .ToList();
    }

    public string GetAlbumArtist(string album)
    {
        var key = album.Trim();
        return _db.Table<MusicTrack>()
            .ToList()
            .Where(t => !t.IsHidden && string.Equals(t.Album?.Trim(), key, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrEmpty(t.Artist))
            .GroupBy(t => t.Artist)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key ?? "Unknown Artist";
    }

    // Canonical album-cover rule — THE single definition of "which track's art
    // represents an album" used by every surface (artist cards, albums cards,
    // album detail, hero overlays). Lowest track number wins; Title then FilePath
    // break ties deterministically — untagged files all share TrackNumber 0, so
    // TrackNumber alone would leave the pick to raw scan order (covers flipping
    // after rescans). Null-guards: tags are often missing at runtime.
    public static MusicTrack? PickCanonicalTrack(IEnumerable<MusicTrack> tracks)
    {
        return tracks
            .OrderBy(t => t.TrackNumber)
            .ThenBy(t => t.Title ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public (List<MusicTrack> Tracks, string Artist) GetAlbumDetail(string album)
    {
        var key = album.Trim();
        var all = _db.Table<MusicTrack>().ToList();
        var tracks = all
            .Where(t => !t.IsHidden && string.Equals(t.Album?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.TrackNumber)
            .ThenBy(t => t.Title ?? string.Empty, StringComparer.Ordinal)
            .ThenBy(t => t.FilePath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var artist = tracks
            .Where(t => !string.IsNullOrWhiteSpace(t.Artist))
            .GroupBy(t => t.Artist.Trim(), StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key ?? "Unknown Artist";
        return (tracks, artist);
    }

    // Artists
    public List<string> GetAllArtists()
    {
        var all = _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && !string.IsNullOrEmpty(t.Artist))
            .Select(t => t.Artist)
            .ToList();

        // Case-insensitive merge so "TAME IMPALA" and "Tame Impala" count as ONE
        // artist. The most common spelling is kept as the display/canonical name,
        // with whitespace trimmed.
        return all
            .GroupBy(a => a.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .GroupBy(x => x)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.Ordinal)
                .First().Key)
            .OrderBy(a => a)
            .ToList();
    }

    public List<MusicTrack> GetArtistTracks(string artist)
    {
        var key = artist.Trim();
        return _db.Table<MusicTrack>()
            .ToList()
            .Where(t => !t.IsHidden && string.Equals(t.Artist?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Album)
            .ThenBy(t => t.TrackNumber)
            .ToList();
    }

    // Broad match for the artist detail page: includes shared/collaboration
    // albums where the artist name appears as a collaborator (feat., &, feat.,
    // "," etc.) in Artist or AlbumArtist.
    public List<MusicTrack> GetArtistDetailTracks(string artistName)
    {
        var key = NormalizeArtistName(artistName);
        if (string.IsNullOrEmpty(key)) return new List<MusicTrack>();

        return _db.Table<MusicTrack>()
            .ToList()
            .Where(t => !t.IsHidden && (ArtistFieldMatches(t.Artist, key) || ArtistFieldMatches(t.AlbumArtist, key)))
            .OrderBy(t => t.Album)
            .ThenBy(t => t.TrackNumber)
            .ToList();
    }

    private static string NormalizeArtistName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        return name.Trim().ToLowerInvariant()
            .Replace(" feat.", ",").Replace("feat.", ",")
            .Replace(" feat ", ",").Replace(" ft.", ",")
            .Replace("ft. ", ",").Replace(" ft ", ",")
            .Replace("featuring", ",")
            .Replace(" feats", ",")
            .Trim(',', ' ');
    }

    private static bool ArtistFieldMatches(string field, string key)
    {
        if (string.IsNullOrWhiteSpace(field)) return false;
        if (field.Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return true;

        // Match a white-space/separator-delimited token (covers "A, B", "A & B",
        // "A feat. B", "A/B" and the raw combined tag field, e.g. "A, B").
        var tokens = field.Split(new[] { ',', '&', ';', '/', '+', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(t => t.Trim().Trim(',', ' '));
        return tokens.Any(tok =>
            tok.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            tok.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase));
    }

    // Genres
    public List<string> GetAllGenres() =>
        _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && !string.IsNullOrEmpty(t.Genre))
            .Select(t => t.Genre)
            .Distinct()
            .OrderBy(g => g)
            .ToList();

    public List<MusicTrack> GetGenreTracks(string genre) =>
        _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && t.Genre == genre)
            .OrderBy(t => t.Artist)
            .ThenBy(t => t.Album)
            .ToList();

    // Folders
    public List<string> GetAllFolders()
    {
        var paths = _db.Table<MusicTrack>().Where(t => !t.IsHidden).Select(t => t.FilePath).ToList();
        var dirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in paths)
        {
            var dir = System.IO.Path.GetDirectoryName(p);
            if (!string.IsNullOrEmpty(dir)) dirs.Add(dir);
        }
        return dirs.OrderBy(d => d).ToList();
    }

    public List<MusicTrack> GetFolderTracks(string folder)
    {
        var allInFolder = _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && t.FilePath.StartsWith(folder))
            .OrderBy(t => t.Title)
            .ToList();
        return allInFolder.Where(t =>
        {
            var dir = System.IO.Path.GetDirectoryName(t.FilePath);
            return string.Equals(dir, folder, System.StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    public void Dispose()
    {
        _db?.Close();
        _db?.Dispose();
    }
}
