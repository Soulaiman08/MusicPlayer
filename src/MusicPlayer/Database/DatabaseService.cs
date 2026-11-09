using SQLite;
using MusicPlayer.Models;
using MusicPlayer.Utilities;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Database;

public class DatabaseService
{
    private readonly SQLiteConnection _db;
    private readonly ILogger<DatabaseService> _logger;

    // Concurrency: sqlite-net-pcl's SQLiteConnection is NOT thread-safe.
    // A single lock serializes all access. Transaction-aware: when a transaction
    // is active on the current thread, nested calls skip re-acquiring the lock.
    private readonly object _sync = new();
    private int _txDepth;
    private int _txOwnerThreadId;
    private volatile bool _disposedDb;

    private bool IsTransactionOwner => _txDepth > 0 && _txOwnerThreadId == Environment.CurrentManagedThreadId;

    private IDisposable LockIfNeeded()
    {
        if (_disposedDb) throw new ObjectDisposedException(nameof(DatabaseService));
        if (IsTransactionOwner) return new NoOpScope();
        Monitor.Enter(_sync);
        return new LockScope(this);
    }

    private void EnterTransaction()
    {
        if (_disposedDb) throw new ObjectDisposedException(nameof(DatabaseService));
        if (_txDepth == 0)
        {
            Monitor.Enter(_sync);
            _txOwnerThreadId = Environment.CurrentManagedThreadId;
            _txDepth = 1;
        }
        else if (_txOwnerThreadId == Environment.CurrentManagedThreadId)
        {
            _txDepth++;
        }
        else
        {
            Monitor.Enter(_sync);
            _txOwnerThreadId = Environment.CurrentManagedThreadId;
            _txDepth = 1;
        }
    }

    private void ExitTransaction()
    {
        if (_txOwnerThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("ExitTransaction called by non-owner thread.");
        if (_txDepth > 0) _txDepth--;
        if (_txDepth <= 0)
        {
            _txDepth = 0;
            _txOwnerThreadId = 0;
            Monitor.Exit(_sync);
        }
    }

    private sealed class LockScope : IDisposable
    {
        private readonly DatabaseService _owner;
        private bool _released;
        public LockScope(DatabaseService owner) { _owner = owner; }
        public void Dispose()
        {
            if (_released) return;
            _released = true;
            try { Monitor.Exit(_owner._sync); } catch { }
        }
    }

    private sealed class NoOpScope : IDisposable
    {
        public void Dispose() { }
    }

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
            using var _ = LockIfNeeded();
            _db.CreateTable<MusicTrack>();
            _db.CreateTable<Playlist>();
            _db.CreateTable<PlaylistTrack>();
            _db.CreateTable<QueueItem>();
            _db.CreateTable<HiddenFolder>();
            _db.CreateTable<Setting>();
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
    public List<MusicTrack> GetAllTracks()
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>().Where(t => !t.IsHidden).ToList();
    }

    public List<MusicTrack> GetManagedFolderTracks()
    {
        using var _ = LockIfNeeded();
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
            managedPaths.Any(mp => PathContainment.IsWithin(t.FilePath, mp)))
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

    public List<MusicTrack> GetHiddenTracks()
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>().Where(t => t.IsHidden).ToList();
    }

    public int GetManagedFolderTrackCount()
    {
        using var _ = LockIfNeeded();
        var managedPaths = _db.Table<ManagedMusicFolder>()
            .Where(f => f.Enabled)
            .Select(f => f.Path)
            .ToList();

        if (managedPaths.Count == 0) return 0;

        return _db.Table<MusicTrack>().Count(t =>
            !t.IsHidden &&
            !string.IsNullOrEmpty(t.FilePath) &&
            managedPaths.Any(mp => PathContainment.IsWithin(t.FilePath, mp)));
    }

    public void CleanOrphanTracks()
    {
        using var _ = LockIfNeeded();
        var managedPaths = _db.Table<ManagedMusicFolder>()
            .Where(f => f.Enabled)
            .Select(f => f.Path)
            .ToList();

        // Safety policy: never wipe the whole library just because no managed
        // folders are currently enabled. Tracks are preserved; membership is
        // re-evaluated when folders are (re)added.
        if (managedPaths.Count == 0)
        {
            _logger.LogWarning("CleanOrphanTracks: no enabled managed folders; skipping destructive cleanup to protect library data.");
            return;
        }

        var allTracks = _db.Table<MusicTrack>().ToList();
        foreach (var track in allTracks)
        {
            if (string.IsNullOrEmpty(track.FilePath)) continue;
            var belongsToManaged = managedPaths.Any(mp =>
                PathContainment.IsWithin(track.FilePath, mp));
            if (!belongsToManaged)
            {
                _db.Delete<MusicTrack>(track.Id);
            }
        }
    }

    public MusicTrack? GetTrack(int id)
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>().FirstOrDefault(t => t.Id == id);
    }

    public MusicTrack? GetTrackByPath(string path)
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>().FirstOrDefault(t => t.FilePath == path);
    }

    public void RemoveMissingTracks(IEnumerable<string>? scannedRoots = null)
    {
        if (scannedRoots == null || !scannedRoots.Any())
        {
            _logger.LogWarning("RemoveMissingTracks: no scanned roots provided; skipping to protect library data.");
            return;
        }

        var normalizedRoots = scannedRoots
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r =>
            {
                try { return System.IO.Path.GetFullPath(r.Trim()).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar); }
                catch { return r.Trim(); }
            })
            .ToList();

        using var _ = LockIfNeeded();
        var allTracks = _db.Table<MusicTrack>().ToList();
        int removed = 0;
        foreach (var track in allTracks)
        {
            if (string.IsNullOrEmpty(track.FilePath)) continue;
            if (System.IO.File.Exists(track.FilePath)) continue;
            // Only consider tracks that belong to a successfully scanned root.
            if (!normalizedRoots.Any(root => PathContainment.IsWithin(track.FilePath, root))) continue;
            _db.Delete<MusicTrack>(track.Id);
            removed++;
        }
        if (removed > 0)
            _logger.LogInformation("Removed {Count} tracks whose files no longer exist (within scanned roots)", removed);
    }

    public int InsertTrack(MusicTrack track)
    {
        using var _ = LockIfNeeded();
        return _db.Insert(track);
    }

    public void UpdateTrack(MusicTrack track)
    {
        using var _ = LockIfNeeded();
        _db.Update(track);
    }

    public void DeleteTrack(int id)
    {
        using var _ = LockIfNeeded();
        _db.Delete<MusicTrack>(id);
    }

    public int GetTrackCount()
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>().Count(t => !t.IsHidden);
    }

    public int GetFavoriteCount()
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>().Count(t => !t.IsHidden && t.IsFavorite);
    }

    public int GetFolderCount()
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>().Where(t => !t.IsHidden).Select(t => System.IO.Path.GetDirectoryName(t.FilePath)).Distinct().Count();
    }

    public int GetRecentPlayCount()
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>().Count(t => !t.IsHidden && t.DateLastPlayed > 0);
    }

    public int GetRecentAddCount()
    {
        using var _ = LockIfNeeded();
        long thirtyDaysAgo = DateTimeOffset.UtcNow.AddDays(-30).ToUnixTimeSeconds();
        return _db.Table<MusicTrack>().Count(t => !t.IsHidden && t.DateAdded > thirtyDaysAgo);
    }

    public int GetMostPlayedCount()
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>().Count(t => !t.IsHidden && t.PlayCount > 0);
    }

    public (int TrackCount, int FolderCount, int FavoriteCount, int RecentPlayCount, int RecentAddCount, int MostPlayedCount) GetAllCounts()
    {
        using var _ = LockIfNeeded();
        var managedPaths = GetManagedFolderPathsLocked();
        if (managedPaths.Count == 0)
            return (0, 0, 0, 0, 0, 0);

        var allTracks = _db.Table<MusicTrack>().ToList()
            .Where(t => !t.IsHidden && IsTrackInManagedFolderLocked(t, managedPaths))
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
        using var _ = LockIfNeeded();
        var managedPaths = GetManagedFolderPathsLocked();
        var tracks = _db.Table<MusicTrack>().Select(t => t.FilePath).ToList();
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in tracks)
        {
            if (managedPaths.Count > 0 && !managedPaths.Any(mp => PathContainment.IsWithin(p, mp))) continue;
            var dir = System.IO.Path.GetDirectoryName(p);
            if (string.IsNullOrEmpty(dir)) continue;
            if (counts.ContainsKey(dir)) counts[dir]++;
            else counts[dir] = 1;
        }
        return counts;
    }

    public Dictionary<int, int> GetAllPlaylistTrackCounts()
    {
        using var _ = LockIfNeeded();
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
        using var _ = LockIfNeeded();
        var existing = GetTrackByPathLocked(track.FilePath);
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

    public void BeginTransaction()
    {
        EnterTransaction();
        try { _db.BeginTransaction(); }
        catch { ExitTransaction(); throw; }
    }

    public void Commit()
    {
        if (_txOwnerThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Commit called by non-owner thread.");
        try { _db.Commit(); }
        finally { ExitTransaction(); }
    }

    public void Rollback()
    {
        if (_txOwnerThreadId != Environment.CurrentManagedThreadId)
            throw new InvalidOperationException("Rollback called by non-owner thread.");
        try { _db.Rollback(); } catch { }
        finally { ExitTransaction(); }
    }

    public void ExecuteInTransaction(Action action)
    {
        BeginTransaction();
        try
        {
            action();
            Commit();
        }
        catch
        {
            try { Rollback(); } catch { }
            throw;
        }
    }

    public T ExecuteInTransaction<T>(Func<T> func)
    {
        BeginTransaction();
        try
        {
            var result = func();
            Commit();
            return result;
        }
        catch
        {
            try { Rollback(); } catch { }
            throw;
        }
    }

    // Playlists
    public List<Playlist> GetAllPlaylists()
    {
        using var _ = LockIfNeeded();
        return _db.Table<Playlist>().OrderBy(p => p.SortOrder).ToList();
    }

    public Playlist? GetPlaylist(int id)
    {
        using var _ = LockIfNeeded();
        return _db.Table<Playlist>().FirstOrDefault(p => p.Id == id);
    }

    public int InsertPlaylist(Playlist playlist)
    {
        using var _ = LockIfNeeded();
        return _db.Insert(playlist);
    }

    public void UpdatePlaylist(Playlist playlist)
    {
        using var _ = LockIfNeeded();
        _db.Update(playlist);
    }

    public void DeletePlaylist(int id)
    {
        using var _ = LockIfNeeded();
        _db.Table<PlaylistTrack>().Where(pt => pt.PlaylistId == id).Delete();
        _db.Delete<Playlist>(id);
    }

    public List<MusicTrack> GetPlaylistTracks(int playlistId)
    {
        using var _ = LockIfNeeded();
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
        using var _ = LockIfNeeded();
        _db.Insert(new PlaylistTrack
        {
            PlaylistId = playlistId,
            TrackId = trackId,
            Position = position
        });
    }

    public void RemoveTrackFromPlaylist(int playlistId, int trackId)
    {
        using var _ = LockIfNeeded();
        _db.Table<PlaylistTrack>()
            .Where(pt => pt.PlaylistId == playlistId && pt.TrackId == trackId)
            .Delete();
    }

    // Managed Folders
    public List<ManagedMusicFolder> GetManagedFolders()
    {
        using var _ = LockIfNeeded();
        return _db.Table<ManagedMusicFolder>().Where(f => f.Enabled).OrderBy(f => f.Name).ToList();
    }

    public List<ManagedMusicFolder> GetAllManagedFolders()
    {
        using var _ = LockIfNeeded();
        return _db.Table<ManagedMusicFolder>().OrderBy(f => f.Name).ToList();
    }

    public ManagedMusicFolder? GetManagedFolder(string path)
    {
        using var _ = LockIfNeeded();
        return _db.Table<ManagedMusicFolder>().FirstOrDefault(f => f.Path == path);
    }

    public int AddManagedFolder(ManagedMusicFolder folder)
    {
        using var _ = LockIfNeeded();
        var existing = _db.Table<ManagedMusicFolder>().FirstOrDefault(f => f.Path == folder.Path);
        if (existing != null) return existing.Id;
        return _db.Insert(folder);
    }

    public void RemoveManagedFolder(int id)
    {
        using var _ = LockIfNeeded();
        _db.Delete<ManagedMusicFolder>(id);
    }

    public void UpdateManagedFolderLastScan(int id)
    {
        using var _ = LockIfNeeded();
        _db.Execute("UPDATE managed_folders SET LastScanDate = ? WHERE Id = ?",
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(), id);
    }

    // Queue
    public List<MusicTrack> GetQueue()
    {
        using var _ = LockIfNeeded();
        var items = _db.Table<QueueItem>().OrderBy(q => q.Position).ToList();
        var managedPaths = GetManagedFolderPathsLocked();
        return items.Select(i => _db.Table<MusicTrack>().FirstOrDefault(t => t.Id == i.TrackId))
            .Where(t => t != null && IsTrackInManagedFolderLocked(t!, managedPaths))
            .Cast<MusicTrack>()
            .ToList();
    }

    public void SaveQueue(List<QueueItem> items)
    {
        using var _ = LockIfNeeded();
        _db.BeginTransaction();
        try
        {
            _db.DeleteAll<QueueItem>();
            _db.InsertAll(items);
            _db.Commit();
        }
        catch
        {
            try { _db.Rollback(); } catch { }
            throw;
        }
    }

    // Favorites
    public List<MusicTrack> GetFavoriteTracks()
    {
        using var _ = LockIfNeeded();
        var managedPaths = GetManagedFolderPathsLocked();
        if (managedPaths.Count == 0) return new List<MusicTrack>();
        return _db.Table<MusicTrack>().Where(t => !t.IsHidden && t.IsFavorite).ToList()
            .Where(t => IsTrackInManagedFolderLocked(t, managedPaths))
            .OrderBy(t => t.Title).ToList();
    }

    public void ToggleFavorite(int trackId)
    {
        using var _ = LockIfNeeded();
        var track = _db.Table<MusicTrack>().FirstOrDefault(t => t.Id == trackId);
        if (track != null)
        {
            track.IsFavorite = !track.IsFavorite;
            _db.Update(track);
        }
    }

    // Recent/Most played
    public List<MusicTrack> GetRecentPlay(int limit = 50)
    {
        using var _ = LockIfNeeded();
        var managedPaths = GetManagedFolderPathsLocked();
        if (managedPaths.Count == 0) return new List<MusicTrack>();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && t.DateLastPlayed > 0)
            .ToList()
            .Where(t => IsTrackInManagedFolderLocked(t, managedPaths))
            .OrderByDescending(t => t.DateLastPlayed)
            .Take(limit)
            .ToList();
    }

    public List<MusicTrack> GetMostPlay(int limit = 50)
    {
        using var _ = LockIfNeeded();
        var managedPaths = GetManagedFolderPathsLocked();
        if (managedPaths.Count == 0) return new List<MusicTrack>();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && t.PlayCount > 0)
            .ToList()
            .Where(t => IsTrackInManagedFolderLocked(t, managedPaths))
            .OrderByDescending(t => t.PlayCount)
            .Take(limit)
            .ToList();
    }

    public List<MusicTrack> GetRecentAdd(int limit = 50)
    {
        using var _ = LockIfNeeded();
        var managedPaths = GetManagedFolderPathsLocked();
        if (managedPaths.Count == 0) return new List<MusicTrack>();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden)
            .OrderByDescending(t => t.DateAdded)
            .ToList()
            .Where(t => IsTrackInManagedFolderLocked(t, managedPaths))
            .Take(limit)
            .ToList();
    }

    // Helper: get managed folder paths (lock-free internal; caller must hold lock)
    private List<string> GetManagedFolderPathsLocked()
    {
        return _db.Table<ManagedMusicFolder>()
            .Where(f => f.Enabled)
            .Select(f => f.Path)
            .ToList();
    }

    // Helper: check if track belongs to a managed folder
    private static bool IsTrackInManagedFolderLocked(MusicTrack track, List<string> managedPaths)
    {
        if (string.IsNullOrEmpty(track.FilePath)) return false;
        return managedPaths.Any(mp => PathContainment.IsWithin(track.FilePath, mp));
    }

    // Public helper used by external callers (locks internally)
    public List<string> GetManagedFolderPaths()
    {
        using var _ = LockIfNeeded();
        return GetManagedFolderPathsLocked();
    }

    public bool IsTrackInManagedFolder(MusicTrack track, List<string> managedPaths)
    {
        return IsTrackInManagedFolderLocked(track, managedPaths);
    }

    // Hidden folders
    public List<HiddenFolder> GetHiddenFolders()
    {
        using var _ = LockIfNeeded();
        return _db.Table<HiddenFolder>().ToList();
    }

    public void AddHiddenFolder(string path)
    {
        using var _ = LockIfNeeded();
        _db.Insert(new HiddenFolder { FolderPath = path });
    }

    public void RemoveHiddenFolder(string path)
    {
        using var _ = LockIfNeeded();
        _db.Table<HiddenFolder>().Where(h => h.FolderPath == path).Delete();
    }

    // Settings
    public string GetSetting(string key, string defaultValue = "")
    {
        using var _ = LockIfNeeded();
        var setting = _db.Table<Setting>().FirstOrDefault(s => s.Key == key);
        return setting?.Value ?? defaultValue;
    }

    public void SetSetting(string key, string value)
    {
        using var _ = LockIfNeeded();
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
        using var _ = LockIfNeeded();
        var value = GetSettingLocked(key, defaultValue.ToString());
        return int.TryParse(value, out int result) ? result : defaultValue;
    }

    public bool GetSettingBool(string key, bool defaultValue = false)
    {
        using var _ = LockIfNeeded();
        var value = GetSettingLocked(key, defaultValue.ToString());
        return bool.TryParse(value, out bool result) ? result : defaultValue;
    }

    public float GetSettingFloat(string key, float defaultValue = 0f)
    {
        using var _ = LockIfNeeded();
        var value = GetSettingLocked(key, defaultValue.ToString());
        return float.TryParse(value, out float result) ? result : defaultValue;
    }

    private string GetSettingLocked(string key, string defaultValue)
    {
        var setting = _db.Table<Setting>().FirstOrDefault(s => s.Key == key);
        return setting?.Value ?? defaultValue;
    }

    // Lyrics
    public LyricBinding? GetLyricBinding(string trackPath)
    {
        using var _ = LockIfNeeded();
        return _db.Table<LyricBinding>().FirstOrDefault(l => l.TrackPath == trackPath);
    }

    public void SaveLyricBinding(LyricBinding binding)
    {
        using var _ = LockIfNeeded();
        _db.InsertOrReplace(binding);
    }

    // Search
    public List<MusicTrack> SearchTracks(string query)
    {
        using var _ = LockIfNeeded();
        var lowerQuery = query.ToLower();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && (t.Title.ToLower().Contains(lowerQuery) ||
                        t.Artist.ToLower().Contains(lowerQuery) ||
                        t.Album.ToLower().Contains(lowerQuery)))
            .OrderBy(t => t.Title)
            .ToList();
    }

    // Albums
    public List<string> GetAllAlbums()
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && !string.IsNullOrEmpty(t.Album))
            .Select(t => t.Album)
            .Distinct()
            .OrderBy(a => a)
            .ToList();
    }

    public List<MusicTrack> GetAlbumTracks(string album)
    {
        using var _ = LockIfNeeded();
        var key = album.Trim();
        return _db.Table<MusicTrack>()
            .ToList()
            .Where(t => !t.IsHidden && string.Equals(t.Album?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.TrackNumber)
            .ToList();
    }

    public string GetAlbumArtist(string album)
    {
        using var _ = LockIfNeeded();
        var key = album.Trim();
        return _db.Table<MusicTrack>()
            .ToList()
            .Where(t => !t.IsHidden && string.Equals(t.Album?.Trim(), key, StringComparison.OrdinalIgnoreCase)
                     && !string.IsNullOrEmpty(t.Artist))
            .GroupBy(t => t.Artist)
            .OrderByDescending(g => g.Count())
            .FirstOrDefault()?.Key ?? "Unknown Artist";
    }

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
        using var _ = LockIfNeeded();
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
        using var _ = LockIfNeeded();
        var all = _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && !string.IsNullOrEmpty(t.Artist))
            .Select(t => t.Artist)
            .ToList();

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
        using var _ = LockIfNeeded();
        var key = artist.Trim();
        return _db.Table<MusicTrack>()
            .ToList()
            .Where(t => !t.IsHidden && string.Equals(t.Artist?.Trim(), key, StringComparison.OrdinalIgnoreCase))
            .OrderBy(t => t.Album)
            .ThenBy(t => t.TrackNumber)
            .ToList();
    }

    public List<MusicTrack> GetArtistDetailTracks(string artistName)
    {
        using var _ = LockIfNeeded();
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

        var tokens = field.Split(new[] { ',', '&', ';', '/', '+', '\\' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(t => t.Trim().Trim(',', ' '));
        return tokens.Any(tok =>
            tok.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            tok.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase));
    }

    // Genres
    public List<string> GetAllGenres()
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && !string.IsNullOrEmpty(t.Genre))
            .Select(t => t.Genre)
            .Distinct()
            .OrderBy(g => g)
            .ToList();
    }

    public List<MusicTrack> GetGenreTracks(string genre)
    {
        using var _ = LockIfNeeded();
        return _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && t.Genre == genre)
            .OrderBy(t => t.Artist)
            .ThenBy(t => t.Album)
            .ToList();
    }

    // Folders
    public List<string> GetAllFolders()
    {
        using var _ = LockIfNeeded();
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
        using var _ = LockIfNeeded();
        var allInFolder = _db.Table<MusicTrack>()
            .Where(t => !t.IsHidden && PathContainment.IsWithin(t.FilePath, folder))
            .OrderBy(t => t.Title)
            .ToList();
        return allInFolder.Where(t =>
        {
            var dir = System.IO.Path.GetDirectoryName(t.FilePath);
            return string.Equals(dir, folder, System.StringComparison.OrdinalIgnoreCase);
        }).ToList();
    }

    // Internal locked helpers used by transactional callers
    private MusicTrack? GetTrackByPathLocked(string path)
    {
        return _db.Table<MusicTrack>().FirstOrDefault(t => t.FilePath == path);
    }

    public void Dispose()
    {
        if (_disposedDb) return;
        _disposedDb = true;
        try
        {
            Monitor.Enter(_sync);
            try { _db?.Close(); } catch (Exception ex) { try { _logger.LogError(ex, "Database close failed"); } catch { } }
            try { _db?.Dispose(); } catch (Exception ex) { try { _logger.LogError(ex, "Database dispose failed"); } catch { } }
        }
        finally
        {
            try { Monitor.Exit(_sync); } catch { }
        }
    }
}
