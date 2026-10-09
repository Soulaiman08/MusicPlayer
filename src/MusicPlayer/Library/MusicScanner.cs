using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TagLib;
using MusicPlayer.Models;
using MusicPlayer.Database;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Library;

public class MusicScanner
{
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
    
    public void ForceResetScanning() => IsScanning = false;
    
    public MusicScanner(DatabaseService db, ILogger<MusicScanner> logger)
    {
        _db = db;
        _logger = logger;
    }
    
    public async Task ScanFoldersAsync(IEnumerable<string> folders, CancellationToken cancellationToken = default)
    {
        if (IsScanning) { _logger.LogWarning("Scan already in progress, skipping"); return; }
        IsScanning = true;
        
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
                        continue;
                    }

                    var allFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
                    _logger.LogInformation("Total files in {Folder}: {Count}", folder, allFiles.Length);
                    
                    var files = allFiles
                        .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLower()))
                        .ToList();
                    
                    _logger.LogInformation("Music files in {Folder}: {Count}", folder, files.Count);

                    _db.BeginTransaction();
                    foreach (var file in files)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        
                        try
                        {
                            var dir = Path.GetDirectoryName(file)?.ToLower() ?? "";
                            if (hiddenFolders.Any(h => dir.StartsWith(h))) continue;
                            
                            var fileInfo = new FileInfo(file);
                            if (excludeBySize && fileInfo.Length < minSize) continue;
                            
                            var tagFile = TagLib.File.Create(file);
                            
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
                            
                            _db.InsertOrIgnoreTrack(track);
                            count++;
                            FileScanned?.Invoke(this, file);
                            ProgressChanged?.Invoke(this, count);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to scan file: {File}", file);
                        }
                    }
                    _db.Commit();
                }
                catch (Exception ex)
                {
                    try { _db.Rollback(); } catch { }
                    _logger.LogWarning(ex, "Failed to scan folder: {Folder}", folder);
                }
            }
            
            _logger.LogInformation("Scan complete. {Count} tracks indexed.", count);
        }, cancellationToken);
        
        IsScanning = false;
        ScanCompleted?.Invoke(this, EventArgs.Empty);
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
        if (IsScanning) return (0, 0, 0);
        IsScanning = true;
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
                    if (!rootFolders.Any(r => normalized.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || normalized.Equals(r, StringComparison.OrdinalIgnoreCase)))
                    {
                        rootFolders.Add(normalized);
                    }
                }

                // If managed folders are in use, ensure all scanned root folders are registered in ManagedMusicFolder
                if (managedFolders.Count > 0)
                {
                    foreach (var rf in rootFolders)
                    {
                        if (!managedFolders.Any(mf => rf.StartsWith(mf.Path, StringComparison.OrdinalIgnoreCase)))
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

                // 2. Remove missing tracks whose files were deleted or moved from disk
                var allDbTracks = _db.GetAllTracks();
                var dbTracksByPath = new Dictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);

                _db.BeginTransaction();
                try
                {
                    foreach (var t in allDbTracks)
                    {
                        if (string.IsNullOrEmpty(t.FilePath) || !System.IO.File.Exists(t.FilePath))
                        {
                            _db.DeleteTrack(t.Id);
                            removed++;
                        }
                        else
                        {
                            dbTracksByPath[t.FilePath] = t;
                        }
                    }
                    _db.Commit();
                }
                catch
                {
                    try { _db.Rollback(); } catch { }
                }

                // 3. Scan all root folders on disk for new and modified audio files
                _db.BeginTransaction();
                int batchCount = 0;
                try
                {
                    foreach (var folder in rootFolders)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        if (!Directory.Exists(folder)) continue;

                        string[] allFiles;
                        try
                        {
                            allFiles = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to enumerate files in folder: {Folder}", folder);
                            continue;
                        }

                        foreach (var file in allFiles)
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            if (!IsMusicFile(file)) continue;

                            var dir = Path.GetDirectoryName(file)?.ToLowerInvariant() ?? "";
                            if (hiddenFolders.Any(h => dir.StartsWith(h))) continue;

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
                    _db.Commit();
                }
                catch (Exception ex)
                {
                    try { _db.Rollback(); } catch { }
                    _logger.LogError(ex, "Error during refresh and sync");
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
            IsScanning = false;
            ScanCompleted?.Invoke(this, EventArgs.Empty);
        }

        return (added, updated, removed);
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
