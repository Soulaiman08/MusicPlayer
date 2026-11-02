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

/// <summary>
/// Result of one recursive root traversal. Only <see cref="ScanRootStatus.Completed"/>
/// authorizes deletion; every other state means the tree was not fully inspected.
/// </summary>
public sealed class RootEnumerationResult
{
    public RootEnumerationResult(
        IEnumerable<string> files,
        ScanRootStatus status,
        IEnumerable<string>? excludedPrefixes = null)
    {
        Files = files.ToArray();
        Status = status;
        ExcludedPrefixes = (excludedPrefixes ?? Array.Empty<string>()).ToArray();
    }

    public IReadOnlyList<string> Files { get; }
    public ScanRootStatus Status { get; }

    /// <summary>Nested reparse-point subtrees that were intentionally not traversed.</summary>
    public IReadOnlyList<string> ExcludedPrefixes { get; }

    public bool IsComplete => Status == ScanRootStatus.Completed;
}

public class MusicScanner
{
    private readonly object _scanGate = new();
    private readonly DatabaseService _db;
    private readonly ILogger<MusicScanner> _logger;

    // Deterministic test seams for filesystem inspection. Production leaves
    // these null and uses the real Directory/File APIs.
    internal Func<string, IEnumerable<string>>? DirectoryFileProvider { get; set; }
    internal Func<string, IEnumerable<string>>? DirectoryChildProvider { get; set; }
    internal Func<string, FileAttributes>? DirectoryAttributesProvider { get; set; }
    
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
                if (cancellationToken.IsCancellationRequested)
                {
                    RecordRootStatus(folder, ScanRootStatus.Cancelled);
                    continue;
                }

                ScanRootStatus enumerationStatus;
                IReadOnlyList<string> enumerated = Array.Empty<string>();
                try
                {
                    var availability = CheckRootAvailability(folder);
                    if (availability != ScanRootStatus.Completed)
                    {
                        enumerationStatus = availability;
                    }
                    else
                    {
                        var result = EnumerateRoot(folder, cancellationToken);
                        enumerated = result.Files;
                        enumerationStatus = cancellationToken.IsCancellationRequested
                            ? ScanRootStatus.Cancelled
                            : result.Status;
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    _logger.LogWarning(ex, "Access denied while enumerating folder: {Folder}", folder);
                    enumerationStatus = ScanRootStatus.AccessDenied;
                }
                catch (OperationCanceledException)
                {
                    enumerationStatus = ScanRootStatus.Cancelled;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to enumerate folder: {Folder}", folder);
                    enumerationStatus = ScanRootStatus.Failed;
                }

                if (enumerationStatus == ScanRootStatus.Cancelled)
                {
                    RecordRootStatus(folder, ScanRootStatus.Cancelled);
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

                // Short batched writes. Cancellation rolls this batch back and
                // the root is never reported as completed.
                bool indexFailed = false;
                _db.BeginTransaction();
                try
                {
                    foreach (var track in parsedTracks)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        _db.InsertOrIgnoreTrack(track);
                    }
                    _db.Commit();
                }
                catch (OperationCanceledException)
                {
                    SafeRollback();
                    RecordRootStatus(folder, ScanRootStatus.Cancelled);
                    continue;
                }
                catch (Exception ex)
                {
                    SafeRollback();
                    indexFailed = true;
                    _logger.LogWarning(ex, "Failed to index folder: {Folder}", folder);
                }

                ScanRootStatus finalStatus = enumerationStatus;
                if (cancellationToken.IsCancellationRequested)
                {
                    finalStatus = ScanRootStatus.Cancelled;
                }
                else if (indexFailed && finalStatus == ScanRootStatus.Completed)
                {
                    finalStatus = ScanRootStatus.Failed;
                }

                RecordRootStatus(folder, finalStatus);
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
                // 1. Gather all candidate folders to scan. Unavailable managed
                //    roots are intentionally kept in the list so they are
                //    reported as RootUnavailable and can never authorize deletion.
                var foldersToScan = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                var managedFolders = _db.GetManagedFolders();
                foreach (var mf in managedFolders)
                {
                    if (!string.IsNullOrWhiteSpace(mf.Path))
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

                // Optimize root folders to prevent duplicate nested scans.
                var rootFolders = new List<string>();
                foreach (var f in foldersToScan.OrderBy(p => p.Length))
                {
                    string normalized;
                    try
                    {
                        normalized = Path.GetFullPath(f.Trim())
                            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    }
                    catch
                    {
                        _logger.LogWarning("Skipping invalid scan root: {Folder}", f);
                        continue;
                    }
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
                // access errors, skipped subtrees, or cancellation.
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
                    else
                    {
                        try
                        {
                            var availability = CheckRootAvailability(folder);
                            if (availability != ScanRootStatus.Completed)
                            {
                                status = availability;
                            }
                            else
                            {
                                RootEnumerationResult result = EnumerateRoot(folder, cancellationToken);
                                folderFiles[folder] = result.Files.ToArray();
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    status = ScanRootStatus.Cancelled;
                                }
                                else
                                {
                                    status = result.Status;
                                    if (status == ScanRootStatus.Completed)
                                    {
                                        fullyEnumeratedRoots.Add(folder);
                                    }
                                }
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

                // 3. Build trusted deletion candidates and delete them in one
                //    conservative transaction. Any cancellation, root invalidation,
                //    or database fault rolls the entire transaction back.
                var allDbTracks = _db.GetAllTracks();
                var dbTracksByPath = new Dictionary<string, MusicTrack>(StringComparer.OrdinalIgnoreCase);
                var deletionCandidates = new List<DeletionCandidate>();

                foreach (var t in allDbTracks)
                {
                    if (string.IsNullOrEmpty(t.FilePath)) continue;
                    if (System.IO.File.Exists(t.FilePath))
                    {
                        dbTracksByPath[t.FilePath] = t;
                        continue;
                    }

                    var trackRoot = FindOwningRoot(t.FilePath, fullyEnumeratedRoots);
                    if (trackRoot == null) continue; // unknown/unavailable root -> keep

                    if (!folderFiles.TryGetValue(trackRoot, out var enumeratedFiles))
                    {
                        continue; // no trusted enumeration result -> keep
                    }

                    if (enumeratedFiles.Contains(t.FilePath, StringComparer.OrdinalIgnoreCase))
                    {
                        continue; // defensive: listed as present -> keep
                    }

                    deletionCandidates.Add(new DeletionCandidate(t, trackRoot));
                }

                removed += DeleteMissingTracksTransactional(
                    deletionCandidates,
                    fullyEnumeratedRoots,
                    rootEnumStatus,
                    cancellationToken);

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
                            RecordRootStatus(folder, ScanRootStatus.Completed);
                        }
                        else
                        {
                            RecordRootStatus(folder, status);
                        }
                    }
                    _db.Commit();
                }
                catch (Exception ex)
                {
                    SafeRollback();
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

    private sealed record DeletionCandidate(MusicTrack Track, string Root);

    private sealed class DeletionSafetyException : Exception
    {
        public DeletionSafetyException(string? root, ScanRootStatus status, string message)
            : base(message)
        {
            Root = root;
            Status = status;
        }

        public string? Root { get; }
        public ScanRootStatus Status { get; }
    }

    private static string? FindOwningRoot(string filePath, IEnumerable<string> roots)
        => roots
            .Where(r => PathContainment.IsWithin(filePath, r))
            .OrderByDescending(r => r.Length)
            .FirstOrDefault();

    /// <summary>
    /// Deletes missing tracks in one all-or-nothing transaction. The transaction
    /// commits only when every participating root is still available and no
    /// cancellation was requested immediately before Commit.
    /// </summary>
    private int DeleteMissingTracksTransactional(
        List<DeletionCandidate> candidates,
        HashSet<string> fullyEnumeratedRoots,
        Dictionary<string, ScanRootStatus> rootEnumStatus,
        CancellationToken cancellationToken)
    {
        if (candidates.Count == 0) return 0;

        var participatingRoots = candidates
            .Select(c => c.Root)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (cancellationToken.IsCancellationRequested)
        {
            MarkRootsNonCompleted(participatingRoots, ScanRootStatus.Cancelled, rootEnumStatus);
            return 0;
        }

        // Pre-transaction validation: every root authorizing deletion must still
        // be available. One uncertain root aborts the whole deletion phase.
        foreach (var root in participatingRoots)
        {
            if (!fullyEnumeratedRoots.Contains(root) ||
                !rootEnumStatus.TryGetValue(root, out var status) ||
                status != ScanRootStatus.Completed)
            {
                MarkRootNonCompleted(root, ScanRootStatus.Partial, rootEnumStatus);
                return 0;
            }

            var availability = CheckRootAvailability(root);
            if (availability != ScanRootStatus.Completed)
            {
                MarkRootNonCompleted(root, availability, rootEnumStatus);
                _logger.LogWarning("Deletion aborted: root {Root} is no longer available ({Status})", root, availability);
                return 0;
            }
        }

        bool transactionStarted = false;
        int removedInTransaction = 0;
        try
        {
            _db.BeginTransaction();
            transactionStarted = true;

            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!fullyEnumeratedRoots.Contains(candidate.Root) ||
                    !rootEnumStatus.TryGetValue(candidate.Root, out var status) ||
                    status != ScanRootStatus.Completed)
                {
                    throw new DeletionSafetyException(
                        candidate.Root,
                        ScanRootStatus.Partial,
                        "Deletion candidate root is no longer trusted.");
                }

                var availability = CheckRootAvailability(candidate.Root);
                if (availability != ScanRootStatus.Completed)
                {
                    throw new DeletionSafetyException(
                        candidate.Root,
                        availability,
                        "Deletion candidate root became unavailable.");
                }

                BeforeDeletionFileCheck(candidate.Track);
                if (System.IO.File.Exists(candidate.Track.FilePath))
                {
                    // The file reappeared after enumeration: preserve the record.
                    continue;
                }

                DeleteTrackInTransaction(candidate.Track.Id);
                removedInTransaction++;
                OnDeletionCandidateProcessed(removedInTransaction);
            }

            // Final pre-commit validation. A timeout or access failure here
            // rolls back every deletion, including the ones above.
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var root in participatingRoots)
            {
                var availability = CheckRootAvailability(root);
                if (availability != ScanRootStatus.Completed)
                {
                    throw new DeletionSafetyException(
                        root,
                        availability,
                        "Deletion root became unavailable before commit.");
                }
            }

            if (!ValidateRootsBeforeCommit(participatingRoots))
            {
                throw new DeletionSafetyException(
                    null,
                    ScanRootStatus.Partial,
                    "Final deletion validation failed.");
            }

            CommitDeletionTransaction();
            transactionStarted = false;
            return removedInTransaction;
        }
        catch (OperationCanceledException)
        {
            if (transactionStarted) SafeRollback();
            MarkRootsNonCompleted(participatingRoots, ScanRootStatus.Cancelled, rootEnumStatus);
            return 0;
        }
        catch (DeletionSafetyException ex)
        {
            if (transactionStarted) SafeRollback();
            if (ex.Root != null)
            {
                MarkRootNonCompleted(ex.Root, ex.Status, rootEnumStatus);
            }
            else
            {
                MarkRootsNonCompleted(participatingRoots, ex.Status, rootEnumStatus);
            }
            _logger.LogWarning(ex, "Deletion transaction rolled back");
            return 0;
        }
        catch (Exception ex)
        {
            if (transactionStarted) SafeRollback();
            MarkRootsNonCompleted(participatingRoots, ScanRootStatus.Failed, rootEnumStatus);
            _logger.LogError(ex, "Error while removing missing tracks");
            return 0;
        }
    }

    private void MarkRootNonCompleted(
        string root,
        ScanRootStatus status,
        Dictionary<string, ScanRootStatus> rootEnumStatus)
    {
        if (status == ScanRootStatus.Completed) status = ScanRootStatus.Partial;
        rootEnumStatus[root] = status;
        LastRootStatus[root] = status;
        LastScannedRoots.Remove(root);
        LastFailedRoots.Add(root);
    }

    private void MarkRootsNonCompleted(
        IEnumerable<string> roots,
        ScanRootStatus status,
        Dictionary<string, ScanRootStatus> rootEnumStatus)
    {
        foreach (var root in roots)
        {
            MarkRootNonCompleted(root, status, rootEnumStatus);
        }
    }

    private void SafeRollback()
    {
        try { _db.Rollback(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Scanner rollback failed"); }
    }

    private void RecordRootStatus(string folder, ScanRootStatus status)
    {
        LastRootStatus[folder] = status;
        if (status == ScanRootStatus.Completed)
        {
            LastScannedRoots.Add(folder);
            LastFailedRoots.Remove(folder);
        }
        else
        {
            LastScannedRoots.Remove(folder);
            LastFailedRoots.Add(folder);
        }
    }

    /// <summary>
    /// Checks that a deletion-authorizing root still exists and can be inspected.
    /// Directory.Exists alone is not sufficient: an ACL or IO failure can make it
    /// return false while the drive is still present.
    /// </summary>
    protected virtual ScanRootStatus CheckRootAvailability(string root)
    {
        try
        {
            var attributes = System.IO.File.GetAttributes(root);
            if ((attributes & FileAttributes.Directory) == 0)
                return ScanRootStatus.RootUnavailable;

            // Prove the directory can actually be opened. Reading attributes can
            // succeed even when listing is denied.
            using var entries = Directory.EnumerateFileSystemEntries(root).GetEnumerator();
            entries.MoveNext();
            return ScanRootStatus.Completed;
        }
        catch (DirectoryNotFoundException)
        {
            return ScanRootStatus.RootUnavailable;
        }
        catch (FileNotFoundException)
        {
            return ScanRootStatus.RootUnavailable;
        }
        catch (UnauthorizedAccessException)
        {
            return ScanRootStatus.AccessDenied;
        }
        catch (IOException)
        {
            return ScanRootStatus.RootUnavailable;
        }
        catch (Exception)
        {
            return ScanRootStatus.Failed;
        }
    }

    protected virtual void BeforeDeletionFileCheck(MusicTrack track) { }

    protected virtual void OnDeletionCandidateProcessed(int removedCount) { }

    protected virtual void DeleteTrackInTransaction(int trackId) => _db.DeleteTrack(trackId);

    protected virtual void CommitDeletionTransaction() => _db.Commit();

    /// <summary>
    /// Additional final validation hook. Production returns true after the
    /// per-root checks above; tests can force a deterministic last-moment failure.
    /// </summary>
    protected virtual bool ValidateRootsBeforeCommit(IReadOnlyList<string> roots) => true;

    /// <summary>
    /// Enumerates one root recursively. Production uses
    /// <see cref="EnumerateFilesSafe"/>; tests can override this seam.
    /// </summary>
    protected virtual RootEnumerationResult EnumerateRoot(string root, CancellationToken cancellationToken)
        => EnumerateFilesSafe(root, cancellationToken);

    private RootEnumerationResult EnumerateFilesSafe(string root, CancellationToken cancellationToken)
    {
        var files = new List<string>();
        var excludedPrefixes = new List<string>();
        var status = ScanRootStatus.Completed;

        if (string.IsNullOrWhiteSpace(root))
            return new RootEnumerationResult(files, ScanRootStatus.Failed, excludedPrefixes);

        string fullRoot;
        try { fullRoot = Path.GetFullPath(root.Trim()); }
        catch { return new RootEnumerationResult(files, ScanRootStatus.Failed, excludedPrefixes); }

        if (cancellationToken.IsCancellationRequested)
            return new RootEnumerationResult(files, ScanRootStatus.Cancelled, excludedPrefixes);

        // Root-level reparse points are allowed because the user explicitly
        // selected that root. Nested reparse points are never followed and mark
        // the root Partial so they can never authorize deletion.
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(fullRoot);

        while (stack.Count > 0)
        {
            if (cancellationToken.IsCancellationRequested)
                return new RootEnumerationResult(files, ScanRootStatus.Cancelled, excludedPrefixes);

            var dir = stack.Pop();
            string fullDir;
            try { fullDir = Path.GetFullPath(dir); }
            catch
            {
                status = CombineStatus(status, ScanRootStatus.Failed);
                continue;
            }

            if (!visited.Add(fullDir)) continue;
            if (!PathContainment.IsWithin(fullDir, fullRoot))
            {
                status = CombineStatus(status, ScanRootStatus.Failed);
                continue;
            }

            bool isRoot = string.Equals(fullDir, fullRoot, StringComparison.OrdinalIgnoreCase);
            FileAttributes attributes;
            try
            {
                attributes = ReadDirectoryAttributes(fullDir);
            }
            catch (UnauthorizedAccessException)
            {
                status = CombineStatus(status, ScanRootStatus.AccessDenied);
                continue;
            }
            catch (DirectoryNotFoundException)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.RootUnavailable : ScanRootStatus.Partial);
                continue;
            }
            catch (FileNotFoundException)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.RootUnavailable : ScanRootStatus.Partial);
                continue;
            }
            catch (IOException)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.RootUnavailable : ScanRootStatus.Failed);
                continue;
            }
            catch (Exception)
            {
                status = CombineStatus(status, ScanRootStatus.Failed);
                continue;
            }

            if ((attributes & FileAttributes.Directory) == 0)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.RootUnavailable : ScanRootStatus.Partial);
                continue;
            }

            if ((attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint && !isRoot)
            {
                // Intentionally excluded subtree: preserve everything below it
                // by never letting this root reach Completed.
                excludedPrefixes.Add(fullDir);
                status = CombineStatus(status, ScanRootStatus.Partial);
                continue;
            }

            try
            {
                foreach (var file in EnumerateDirectoryFiles(fullDir))
                {
                    if (cancellationToken.IsCancellationRequested)
                        return new RootEnumerationResult(files, ScanRootStatus.Cancelled, excludedPrefixes);

                    if (!PathContainment.IsWithin(file, fullRoot))
                    {
                        status = CombineStatus(status, ScanRootStatus.Failed);
                        continue;
                    }
                    files.Add(file);
                }
            }
            catch (UnauthorizedAccessException)
            {
                status = CombineStatus(status, ScanRootStatus.AccessDenied);
            }
            catch (DirectoryNotFoundException)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.RootUnavailable : ScanRootStatus.Partial);
            }
            catch (FileNotFoundException)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.RootUnavailable : ScanRootStatus.Partial);
            }
            catch (IOException)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.Failed : ScanRootStatus.Partial);
            }
            catch (Exception)
            {
                status = CombineStatus(status, ScanRootStatus.Failed);
            }

            try
            {
                foreach (var child in EnumerateDirectoryChildren(fullDir))
                {
                    if (cancellationToken.IsCancellationRequested)
                        return new RootEnumerationResult(files, ScanRootStatus.Cancelled, excludedPrefixes);

                    string fullChild;
                    try { fullChild = Path.GetFullPath(child); }
                    catch
                    {
                        status = CombineStatus(status, ScanRootStatus.Failed);
                        continue;
                    }

                    if (!PathContainment.IsWithin(fullChild, fullRoot))
                    {
                        status = CombineStatus(status, ScanRootStatus.Failed);
                        continue;
                    }
                    stack.Push(fullChild);
                }
            }
            catch (UnauthorizedAccessException)
            {
                status = CombineStatus(status, ScanRootStatus.AccessDenied);
            }
            catch (DirectoryNotFoundException)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.RootUnavailable : ScanRootStatus.Partial);
            }
            catch (FileNotFoundException)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.RootUnavailable : ScanRootStatus.Partial);
            }
            catch (IOException)
            {
                status = CombineStatus(status, isRoot ? ScanRootStatus.RootUnavailable : ScanRootStatus.Failed);
            }
            catch (Exception)
            {
                status = CombineStatus(status, ScanRootStatus.Failed);
            }
        }

        if (cancellationToken.IsCancellationRequested)
            return new RootEnumerationResult(files, ScanRootStatus.Cancelled, excludedPrefixes);

        return new RootEnumerationResult(files, status, excludedPrefixes);
    }

    private FileAttributes ReadDirectoryAttributes(string path)
        => DirectoryAttributesProvider != null
            ? DirectoryAttributesProvider(path)
            : System.IO.File.GetAttributes(path);

    private IEnumerable<string> EnumerateDirectoryFiles(string path)
        => DirectoryFileProvider != null
            ? DirectoryFileProvider(path)
            : Directory.GetFiles(path);

    private IEnumerable<string> EnumerateDirectoryChildren(string path)
        => DirectoryChildProvider != null
            ? DirectoryChildProvider(path)
            : Directory.GetDirectories(path);

    private static ScanRootStatus CombineStatus(ScanRootStatus current, ScanRootStatus update)
    {
        if (current == ScanRootStatus.Cancelled || update == ScanRootStatus.Cancelled)
            return ScanRootStatus.Cancelled;
        if (current == ScanRootStatus.RootUnavailable || update == ScanRootStatus.RootUnavailable)
            return ScanRootStatus.RootUnavailable;
        if (current == ScanRootStatus.AccessDenied || update == ScanRootStatus.AccessDenied)
            return ScanRootStatus.AccessDenied;
        if (current == ScanRootStatus.Failed || update == ScanRootStatus.Failed)
            return ScanRootStatus.Failed;
        if (current == ScanRootStatus.Partial || update == ScanRootStatus.Partial)
            return ScanRootStatus.Partial;
        return ScanRootStatus.Completed;
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
