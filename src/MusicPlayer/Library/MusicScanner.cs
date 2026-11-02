using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TagLib;
using MusicPlayer.Models;
using MusicPlayer.Database;
using MusicPlayer.Utilities;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Library;

public enum ScanRootStatus { NotStarted, Completed, Partial, Failed, Cancelled, RootUnavailable, AccessDenied }

public class MusicScanner
{
    private readonly object _scanGate = new();
    private readonly DatabaseService _db;
    private readonly ILogger<MusicScanner> _logger;
    
    private static readonly string[] SupportedExtensions = 
    {
        ".mp3", ".flac", ".wav", ".aac", ".ogg", ".m4a", ".wma", 
        ".opus", ".aiff", ".ape", ".ac3"
    };
    
    public event EventHandler<int>? ProgressChanged;
    public event EventHandler<string>? FileScanned;
    public event EventHandler? ScanCompleted;
    public bool IsScanning { get; private set; }
    public HashSet<string> LastScannedRoots { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> LastFailedRoots { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ScanRootStatus> LastRootStatus { get; } = new(StringComparer.OrdinalIgnoreCase);
    
    public MusicScanner(DatabaseService db, ILogger<MusicScanner> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    public async Task ScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        lock (_scanGate)
        {
            if (IsScanning) { _logger.LogWarning("Scan already in progress, skipping"); return; }
            LastScannedRoots.Clear();
            LastFailedRoots.Clear();
            LastRootStatus.Clear();
            IsScanning = true;
        }

        try
        {
        
        await Task.Run(() =>
        {
            int count = 0;
            var hiddenFolders = _db.GetHiddenFolders()
                .Select(h => h.FolderPath.ToLower())
                .ToHashSet();
            
            int minDuration = _db.GetSettingInt("pref_exclude_music_duration", 60000);
            int minSize = _db.GetSettingInt("pref_exclude_music_size", 100) * 1024;
            bool excludeByDuration = _db.GetSettingBool("pref_ignore_60seconds_music");
            bool excludeBySize = _db.GetSettingBool("pref_exclude_music_by_size", true);
            
            _logger.LogInformation("Scan started. excludeByDuration={ED} excludeBySize={ES} minDuration={MD} minSize={MS}", 
                excludeByDuration, excludeBySize, minDuration, minSize);
            
            foreach (var folder in folders)
            {
                if (cancellationToken.IsCancellationRequested) break;
                
                try
                {
                    if (!Directory.Exists(folder))
                    {
                        _logger.LogWarning("Folder does not exist: {Folder}", folder);
                        LastFailedRoots.Add(folder);
                        LastRootStatus[folder] = ScanRootStatus.RootUnavailable;
                        continue;
                    }

                    var enumComplete = false;
                    IReadOnlyList<string> enumerated;
                    try
                    {
                        enumerated = EnumerateRootFiles(folder, cancellationToken, out enumComplete);
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _logger.LogWarning(ex, "Access denied while enumerating folder: {Folder}", folder);
                        LastFailedRoots.Add(folder);
                        LastRootStatus[folder] = ScanRootStatus.AccessDenied;
                        continue;
                    }
                    catch (OperationCanceledException)
                    {
                        LastFailedRoots.Add(folder);
                        LastRootStatus[folder] = ScanRootStatus.Cancelled;
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to enumerate folder: {Folder}", folder);
                        LastFailedRoots.Add(folder);
                        LastRootStatus[folder] = ScanRootStatus.Failed;
                        continue;
                    }

                    var allFiles = enumerated.ToArray();
                    _logger.LogInformation("Total files in {Folder}: {Count}", folder, allFiles.Length);

                    var files = allFiles
                        .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                        .ToList();

                    _logger.LogInformation("Music files in {Folder}: {Count}", folder, files.Count);

                    // Parse metadata OUTSIDE the SQLite transaction.
                    var parsedTracks = new List<MusicTrack>();
                    foreach (var file in files)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        try
                        {
                            var dir = Path.GetDirectoryName(file)?.ToLower() ?? "";
                            if (hiddenFolders.Any(h => PathContainment.IsWithin(dir, h))) continue;

                            var fileInfo = new FileInfo(file);
                            if (excludeBySize && fileInfo.Length < minSize) continue;

                            using var tagFile = TagLib.File.Create(file);

                            if (excludeByDuration && tagFile.Properties.Duration.TotalMilliseconds < minDuration)
                                continue;

                            var track = new MusicTrack
                            {
                                FilePath = file,
                                Title = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(file),
                                Artist = tagFile.Tag.FirstPerformer ?? "Unknown Artist",
                                Album = tagFile.Tag.Album ?? "Unknown Album",
                                AlbumArtist = tagFile.Tag.FirstAlbumArtist ?? "",
                                Genre = tagFile.Tag.FirstGenre ?? "",
                                Year = (int)tagFile.Tag.Year,
                                TrackNumber = (int)tagFile.Tag.Track,
                                DiscNumber = (int)tagFile.Tag.Disc,
                                Duration = (long)tagFile.Properties.Duration.TotalMilliseconds,
                                FileSize = fileInfo.Length,
                                Bitrate = (int)tagFile.Properties.AudioBitrate,
                                SampleRate = (int)tagFile.Properties.AudioSampleRate,
                                Channels = tagFile.Properties.AudioChannels,
                                DateAdded = new DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds(),
                                DateModified = new DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds(),
                                FolderPath = folder
                            };

                            parsedTracks.Add(track);
                            count++;
                            FileScanned?.Invoke(this, file);
                            ProgressChanged?.Invoke(this, count);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to scan file: {File}", file);
                        }
                    }

                    // Short batched writes.
                    _db.BeginTransaction();
                    try
                    {
                        foreach (var track in parsedTracks)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            _db.InsertOrIgnoreTrack(track);
                        }
                        _db.Commit();
                    }
                    catch
                    {
                        try { _db.Rollback(); } catch { }
                    }

                    if (cancellationToken.IsCancellationRequested)
                    {
                        LastFailedRoots.Add(folder);
                        LastRootStatus[folder] = ScanRootStatus.Cancelled;
                    }
                    else if (!enumComplete)
                    {
                        LastFailedRoots.Add(folder);
                        LastRootStatus[folder] = ScanRootStatus.Partial;
                    }
                    else
                    {
                        LastScannedRoots.Add(folder);
                        LastRootStatus[folder] = ScanRootStatus.Completed;
                    }
                }
                catch (Exception ex)
                {
                    try { _db.Rollback(); } catch { }
                    LastFailedRoots.Add(folder);
                    LastRootStatus[folder] = ScanRootStatus.Failed;
                    _logger.LogWarning(ex, "Failed to scan folder: {Folder}", folder);
                }
            }
            
            _logger.LogInformation("Scan complete. {Count} tracks indexed.", count);
        }, cancellationToken);
        }
        finally
        {
            lock (_scanGate) { IsScanning = false; }
            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
    
    public async Task ScanFolderAsync(string folder, CancellationToken cancellationToken = default)
    {
        await ScanFoldersAsync(new[] { folder }, cancellationToken);
    }
    
    public static bool IsMusicFile(string path)
    {
        var ext = Path.GetExtension(path).ToLower();
        return SupportedExtensions.Contains(ext);
    }
    
    public static List<string> GetMusicFolders()
    {
        var folders = new List<string>();
        
        string[] defaultPaths = {
            Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + "\\Music",
            "C:\\Music",
            "D:\\Music"
        };
        
        foreach (var path in defaultPaths)
        {
            if (Directory.Exists(path) && !folders.Contains(path))
                folders.Add(path);
        }
        
        return folders;
    }
    
    public static List<string> ScanDirectoryForMusic(string directory)
    {
        var files = new List<string>();
        if (!Directory.Exists(directory)) return files;
        
        try
        {
            files.AddRange(Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories)
                .Where(f => IsMusicFile(f)));
        }
        catch { }
        
        return files;
    }

    public async Task<int> RefreshMetadataAsync(CancellationToken cancellationToken = default)
    {
        var (added, updated, removed) = await RefreshAndSyncLibraryAsync(cancellationToken);
        return added + updated + removed;
    }

    public async Task<(int Added, int Updated, int Removed)> RefreshAndSyncLibraryAsync(CancellationToken cancellationToken = default)
    {
        lock (_scanGate)
        {
            if (IsScanning) return (0, 0, 0);
            LastScannedRoots.Clear();
            LastFailedRoots.Clear();
            LastRootStatus.Clear();
            IsScanning = true;
        }
        int added = 0;
        int updated = 0;
        int removed = 0;

        try
        {
            await Task.Run(() =>
            {
                // 1. Gather all candidate folders to scan
                var foldersToScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var managedFolders = _db.GetManagedFolders();
                foreach (var mf in managedFolders)
                {
                    if (Directory.Exists(mf.Path))
                        foldersToScan.Add(mf.Path);
                }

                var existingFolders = _db.GetAllFolders();
                foreach (var ef in existingFolders)
                {
                    if (Directory.Exists(ef))
                        foldersToScan.Add(ef);
                }

                if (foldersToScan.Count == 0)
                {
                    foreach (var df in GetMusicFolders())
                    {
                        if (Directory.Exists(df))
                            foldersToScan.Add(df);
                    }
                }

                // Optimize root folders to prevent duplicate nested scans
                var rootFolders = new List<string>();
                foreach (var f in foldersToScan.OrderBy(p => p.Length))
                {
                    var normalized = Path.GetFullPath(f).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (!rootFolders.Any(r => PathContainment.IsWithin(normalized, r)))
                    {
                        rootFolders.Add(normalized);
                    }
                }

                // If managed folders are in use, ensure all scanned root folders are registered in ManagedMusicFolder
                if (managedFolders.Count > 0)
                {
                    foreach (var rf in rootFolders)
                    {
                        if (!managedFolders.Any(mf => PathContainment.IsWithin(rf, mf.Path)))
                        {
                            var folderName = Path.GetFileName(rf);
                            if (string.IsNullOrEmpty(folderName)) folderName = rf;
                            _db.AddManagedFolder(new ManagedMusicFolder
                            {
                                Path = rf,
                                Name = folderName,
                                Enabled = true,
                                AddedDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                                LastScanDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                            });
                        }
                    }
                }

                // Filter settings
                var hiddenFolders = _db.GetHiddenFolders()
                    .Select(h => h.FolderPath.ToLowerInvariant())
                    .ToHashSet();

                int minDuration = _db.GetSettingInt("pref_exclude_music_duration", 60000);
                int minSize = _db.GetSettingInt("pref_exclude_music_size", 100) * 1024;
                bool excludeByDuration = _db.GetSettingBool("pref_ignore_60seconds_music");
                bool excludeBySize = _db.GetSettingBool("pref_exclude_music_by_size", true);

                // 2. Enumerate every candidate root fully BEFORE any deletion decision.
                // A root is deletable only when this recursive pass completed without
                // access errors or cancellation.
                var folderFiles = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                var fullyEnumeratedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var rootEnumStatus = new Dictionary<string, ScanRootStatus>(StringComparer.OrdinalIgnoreCase);

                foreach (var folder in rootFolders)
                {
                    folderFiles[folder] = Array.Empty<string>();
                    ScanRootStatus status;

                    if (cancellationToken.IsCancellationRequested)
                    {
                        status = ScanRootStatus.Cancelled;
                    }
                    else if (!Directory.Exists(folder))
                    {
                        status = ScanRootStatus.RootUnavailable;
                    }
                    else
                    {
                        try
                        {
                            var files = EnumerateRootFiles(folder, cancellationToken, out var complete);
                            folderFiles[folder] = files.ToArray();
                            if (cancellationToken.IsCancellationRequested)
                            {
                                status = ScanRootStatus.Cancelled;
                            }
                            else if (!complete)
                            {
                                status = ScanRootStatus.Partial;
                            }
                            else
                            {
                                status = ScanRootStatus.Completed;
                                fullyEnumeratedRoots.Add(folder);
                            }
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            _logger.LogWarning(ex, "Access denied while enumerating folder: {Folder}", folder);
                            status = ScanRootStatus.AccessDenied;
                        }
                        catch (OperationCanceledException)
                        {
                            status = ScanRootStatus.Cancelled;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to enumerate folder: {Folder}", folder);
                            status = ScanRootStatus.Failed;
                        }
                    }

                    rootEnumStatus[folder] = status;
                    LastRootStatus[folder] = status;
                    if (status != ScanRootStatus.Completed)
                    {
                        LastFailedRoots.Add(folder);
                    }
                }

                // 3. Remove missing tracks. Deletion is restricted to files that
                // belong to a root whose recursive enumeration completed above.
                var allDbTracks = _db.GetAllTracks();
                var dbTracksByPath = new Dictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);

                _db.BeginTransaction();
                try
                {
                    int removedInTransaction = 0;
                    foreach (var t in allDbTracks)
                    {
                        if (string.IsNullOrEmpty(t.FilePath)) continue;
                        if (System.IO.File.Exists(t.FilePath))
                        {
                            dbTracksByPath[t.FilePath] = t;
                            continue;
                        }

                        // Cancellation or any ambiguous state means keep the record.
                        if (cancellationToken.IsCancellationRequested) continue;

                        var trackRoot = fullyEnumeratedRoots.FirstOrDefault(r =>
                            PathContainment.IsWithin(t.FilePath, r));
                        if (trackRoot == null) continue; // unknown/unavailable root -> keep

                        if (!rootEnumStatus.TryGetValue(trackRoot, out var status) ||
                            status != ScanRootStatus.Completed)
                        {
                            continue; // partial/failed/cancelled -> keep
                        }

                        if (!folderFiles.TryGetValue(trackRoot, out var enumeratedFiles))
                        {
                            continue; // no trusted enumeration result -> keep
                        }

                        if (enumeratedFiles.Contains(t.FilePath, StringComparer.OrdinalIgnoreCase))
                        {
                            continue; // defensive: listed as present -> keep
                        }

                        if (!Directory.Exists(trackRoot)) continue; // root vanished -> keep

                        _db.DeleteTrack(t.Id);
                        removedInTransaction++;
                    }
                    _db.Commit();
                    removed += removedInTransaction;
                }
                catch (Exception ex)
                {
                    try { _db.Rollback(); } catch { }
                    _logger.LogError(ex, "Error while removing missing tracks");
                }

                // 4. Index files using the same enumeration results (no second pass).
                _db.BeginTransaction();
                int batchCount = 0;
                try
                {
                    foreach (var folder in rootFolders)
                    {
                        var status = rootEnumStatus.TryGetValue(folder, out var rootStatus)
                            ? rootStatus
                            : ScanRootStatus.Failed;

                        if (status == ScanRootStatus.Completed || status == ScanRootStatus.Partial)
                        {
                            if (!Directory.Exists(folder))
                            {
                                status = ScanRootStatus.RootUnavailable;
                            }
                            else if (folderFiles.TryGetValue(folder, out var allFiles))
                            {
                                foreach (var file in allFiles)
                                {
                                    if (cancellationToken.IsCancellationRequested)
                                    {
                                        status = ScanRootStatus.Cancelled;
                                        break;
                                    }
                                    if (!IsMusicFile(file)) continue;

                                    var dir = Path.GetDirectoryName(file)?.ToLowerInvariant() ?? "";
                                    if (hiddenFolders.Any(h => PathContainment.IsWithin(dir, h))) continue;

                                    FileInfo fileInfo;
                                    try
                                    {
                                        fileInfo = new FileInfo(file);
                                    }
                                    catch
                                    {
                                        continue;
                                    }

                                    if (excludeBySize && fileInfo.Length < minSize) continue;

                                    if (dbTracksByPath.TryGetValue(file, out var existingTrack))
                                    {
                                        // Existing track: check if file was modified or refresh metadata
                                        try
                                        {
                                            long fileModified = new DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds();
                                            if (existingTrack.DateModified != fileModified || existingTrack.FileSize != fileInfo.Length)
                                            {
                                                using var tagFile = TagLib.File.Create(file);
                                                if (excludeByDuration && tagFile.Properties.Duration.TotalMilliseconds < minDuration)
                                                    continue;

                                                var newTitle = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(file);
                                                var newArtist = tagFile.Tag.FirstPerformer ?? "Unknown Artist";
                                                var newAlbum = tagFile.Tag.Album ?? "Unknown Album";
                                                var newGenre = tagFile.Tag.FirstGenre ?? "";
                                                var newDuration = (long)tagFile.Properties.Duration.TotalMilliseconds;

                                                existingTrack.Title = newTitle;
                                                existingTrack.Artist = newArtist;
                                                existingTrack.Album = newAlbum;
                                                existingTrack.AlbumArtist = tagFile.Tag.FirstAlbumArtist ?? "";
                                                existingTrack.Genre = newGenre;
                                                existingTrack.Year = (int)tagFile.Tag.Year;
                                                existingTrack.TrackNumber = (int)tagFile.Tag.Track;
                                                existingTrack.DiscNumber = (int)tagFile.Tag.Disc;
                                                existingTrack.Duration = newDuration;
                                                existingTrack.FileSize = fileInfo.Length;
                                                existingTrack.Bitrate = (int)tagFile.Properties.AudioBitrate;
                                                existingTrack.SampleRate = (int)tagFile.Properties.AudioSampleRate;
                                                existingTrack.Channels = tagFile.Properties.AudioChannels;
                                                existingTrack.DateModified = fileModified;

                                                _db.UpdateTrack(existingTrack);
                                                updated++;
                                                batchCount++;
                                            }
                                        }
                                        catch { }
                                    }
                                    else
                                    {
                                        // New track added to the folder!
                                        try
                                        {
                                            using var tagFile = TagLib.File.Create(file);
                                            if (excludeByDuration && tagFile.Properties.Duration.TotalMilliseconds < minDuration)
                                                continue;

                                            var newTrack = new MusicTrack
                                            {
                                                FilePath = file,
                                                Title = tagFile.Tag.Title ?? Path.GetFileNameWithoutExtension(file),
                                                Artist = tagFile.Tag.FirstPerformer ?? "Unknown Artist",
                                                Album = tagFile.Tag.Album ?? "Unknown Album",
                                                AlbumArtist = tagFile.Tag.FirstAlbumArtist ?? "",
                                                Genre = tagFile.Tag.FirstGenre ?? "",
                                                Year = (int)tagFile.Tag.Year,
                                                TrackNumber = (int)tagFile.Tag.Track,
                                                DiscNumber = (int)tagFile.Tag.Disc,
                                                Duration = (long)tagFile.Properties.Duration.TotalMilliseconds,
                                                FileSize = fileInfo.Length,
                                                Bitrate = (int)tagFile.Properties.AudioBitrate,
                                                SampleRate = (int)tagFile.Properties.AudioSampleRate,
                                                Channels = tagFile.Properties.AudioChannels,
                                                DateAdded = new DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds(),
                                                DateModified = new DateTimeOffset(fileInfo.LastWriteTime).ToUnixTimeSeconds(),
                                                FolderPath = folder
                                            };

                                            _db.InsertTrack(newTrack);
                                            dbTracksByPath[file] = newTrack;
                                            added++;
                                            batchCount++;
                                        }
                                        catch (Exception ex)
                                        {
                                            _logger.LogWarning(ex, "Failed to parse new music file: {File}", file);
                                        }
                                    }

                                    if (batchCount >= 50)
                                    {
                                        _db.Commit();
                                        _db.BeginTransaction();
                                        batchCount = 0;
                                    }
                                }
                            }

                            if (cancellationToken.IsCancellationRequested)
                            {
                                status = ScanRootStatus.Cancelled;
                            }
                        }

                        // Completed is assigned only after enumeration AND indexing
                        // finished. A Partial root can never be promoted here.
                        if (status == ScanRootStatus.Completed)
                        {
                            LastScannedRoots.Add(folder);
                            LastRootStatus[folder] = ScanRootStatus.Completed;
                        }
                        else
                        {
                            LastScannedRoots.Remove(folder);
                            LastFailedRoots.Add(folder);
                            LastRootStatus[folder] = status;
                        }
                    }
                    _db.Commit();
                }
                catch (Exception ex)
                {
                    try { _db.Rollback(); } catch { }
                    _logger.LogError(ex, "Error during refresh and sync");
                    // No root may be reported as completed if indexing failed.
                    foreach (var folder in rootFolders)
                    {
                        LastScannedRoots.Remove(folder);
                        if (rootEnumStatus.TryGetValue(folder, out var s) && s == ScanRootStatus.Completed)
                        {
                            LastRootStatus[folder] = ScanRootStatus.Failed;
                            LastFailedRoots.Add(folder);
                        }
                    }
                }

                // Update managed folder scan dates
                foreach (var mf in managedFolders)
                {
                    try { _db.UpdateManagedFolderLastScan(mf.Id); } catch { }
                }
            }, cancellationToken);
        }
        finally
        {
            lock (_scanGate) { IsScanning = false; }
            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }

        return (added, updated, removed);
    }

    /// <summary>
    /// Enumerates a managed root recursively and reports whether the whole tree
    /// was readable. Test seams override this to simulate access failures
    /// deterministically; production uses <see cref="EnumerateFilesSafe"/>.
    /// </summary>
    protected virtual IReadOnlyList<string> EnumerateRootFiles(string root, CancellationToken cancellationToken, out bool complete)
        => EnumerateFilesSafe(root, out complete).ToArray();

    private static IEnumerable<string> EnumerateFilesSafe(string root, out bool complete)
    {
        complete = true;
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(root)) { complete = false; return result; }
        string fullRoot;
        try { fullRoot = Path.GetFullPath(root.Trim()); }
        catch { complete = false; return result; }
        if (!Directory.Exists(fullRoot)) { complete = false; return result; }

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(fullRoot);
        while (stack.Count > 0)
        {
            var dir = stack.Pop();
            string fullDir;
            try { fullDir = Path.GetFullPath(dir); }
            catch { continue; }
            if (!visited.Add(fullDir)) continue; // avoid cycles
            if (!Directory.Exists(fullDir)) continue;

            // Do not follow reparse points (junctions/symlinks) by default.
            try
            {
                var di = new DirectoryInfo(fullDir);
                if ((di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                {
                    if (!string.Equals(fullDir, fullRoot, StringComparison.OrdinalIgnoreCase))
                        continue; // skip nested reparse points
                }
            }
            catch { }

            // Containment: do not escape root.
            if (!PathContainment.IsWithin(fullDir, fullRoot)) continue;

            try
            {
                foreach (var f in Directory.GetFiles(fullDir))
                    result.Add(f);
            }
            catch { complete = false; }

            try
            {
                foreach (var d in Directory.GetDirectories(fullDir))
                {
                    try
                    {
                        var sub = new DirectoryInfo(d);
                        if ((sub.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint)
                            continue; // skip junctions/symlinks
                    }
                    catch { continue; }
                    stack.Push(d);
                }
            }
            catch { complete = false; }
        }
        return result;
    }

    public int BackfillDateAdded()
    {
        int fixedCount = 0;
        try
        {
            var tracks = _db.GetAllTracks();
            _db.BeginTransaction();
            foreach (var track in tracks)
            {
                if (string.IsNullOrEmpty(track.FilePath) || !System.IO.File.Exists(track.FilePath)) continue;
                try
                {
                    var fi = new FileInfo(track.FilePath);
                    long dateAdded = new DateTimeOffset(fi.LastWriteTime).ToUnixTimeSeconds();
                    if (track.DateAdded != dateAdded)
                    {
                        track.DateAdded = dateAdded;
                        _db.UpdateTrack(track);
                        fixedCount++;
                    }
                }
                catch { }
            }
            _db.Commit();
            if (fixedCount > 0)
                _logger.LogInformation("Date added backfill updated {Count} tracks from file creation time", fixedCount);
        }
        catch (Exception ex)
        {
            try { _db.Rollback(); } catch { }
            _logger.LogWarning(ex, "Date added backfill failed");
        }
        return fixedCount;
    }
}
