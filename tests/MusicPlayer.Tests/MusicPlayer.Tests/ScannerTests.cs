using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Database;
using MusicPlayer.Library;
using MusicPlayer.Models;
using NAudio.Wave;

namespace MusicPlayer.Tests
{
    public class ScannerTests : IDisposable
    {
        private readonly string _tempBase;
        private readonly string _dbPath;
        private readonly DatabaseService _db;
        private readonly string _root;
        private readonly string _anchorRoot;
        private readonly string _outsideRoot;

        public ScannerTests()
        {
            SQLitePCL.Batteries_V2.Init();
            _tempBase = Path.Combine(Path.GetTempPath(), "mp_scan_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempBase);
            _dbPath = Path.Combine(_tempBase, "library.db");
            _db = new DatabaseService(_dbPath, NullLogger<DatabaseService>.Instance);
            _db.SetSetting("pref_exclude_music_by_size", "false");
            _db.SetSetting("pref_ignore_60seconds_music", "false");

            _root = Path.Combine(_tempBase, "root");
            _anchorRoot = Path.Combine(_tempBase, "anchor");
            _outsideRoot = Path.Combine(_tempBase, "outside");
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "sub"));
            Directory.CreateDirectory(_anchorRoot);

            CreateWav(Path.Combine(_root, "a.wav"));
            CreateWav(Path.Combine(_root, "sub", "b.wav"));
            CreateWav(Path.Combine(_anchorRoot, "anchor.wav"));

            AddManagedRoot(_root, "root");
            AddManagedRoot(_anchorRoot, "anchor");
        }

        public void Dispose()
        {
            try { _db.Dispose(); } catch { }
            try { Directory.Delete(_tempBase, true); } catch { }
        }

        [Fact]
        public async Task RefreshAndSync_CompleteRoot_DeletesMissingAndIndexesPresent()
        {
            string missingInRoot = Path.Combine(_root, "gone.wav");
            string outsideTrack = Path.Combine(_outsideRoot, "outside-missing.wav");
            InsertMissingTrack(missingInRoot);
            InsertMissingTrack(outsideTrack);

            var scanner = new MusicScanner(_db, NullLogger<MusicScanner>.Instance);
            var result = await scanner.RefreshAndSyncLibraryAsync();

            Assert.Equal(1, result.Removed);
            Assert.Null(_db.GetTrackByPath(missingInRoot));
            Assert.NotNull(_db.GetTrackByPath(outsideTrack));
            Assert.NotNull(_db.GetTrackByPath(Path.Combine(_root, "a.wav")));
            Assert.NotNull(_db.GetTrackByPath(Path.Combine(_root, "sub", "b.wav")));

            Assert.Contains(_root, scanner.LastScannedRoots);
            Assert.Contains(_anchorRoot, scanner.LastScannedRoots);
            Assert.Equal(ScanRootStatus.Completed, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(missingInRoot, scanner.LastFailedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_PartialRoot_PreservesMissingTrack_AndIsNotPromotedToCompleted()
        {
            string missingInRoot = Path.Combine(_root, "gone.wav");
            InsertMissingTrack(missingInRoot);
            string a = Path.Combine(_root, "a.wav");

            var scanner = new SeamMusicScanner(_db, (folder, _) =>
            {
                if (string.Equals(folder, _root, StringComparison.OrdinalIgnoreCase))
                {
                    // Only part of the tree was readable: a.wav was seen, b.wav
                    // and gone.wav were not. The root must not authorize deletion.
                    return new RootEnumerationResult(new[] { a }, ScanRootStatus.Partial);
                }
                return Complete(folder);
            });

            var result = await scanner.RefreshAndSyncLibraryAsync();

            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(missingInRoot));
            Assert.Equal(ScanRootStatus.Partial, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
            Assert.Contains(_root, scanner.LastFailedRoots);
            // Partial is a terminal result for this scan, not a Completed promotion.
            Assert.NotEqual(ScanRootStatus.Completed, scanner.LastRootStatus[_root]);
        }

        [Fact]
        public async Task RefreshAndSync_UnavailableRoot_PreservesMissingTrack()
        {
            string missingInRoot = Path.Combine(_root, "gone.wav");
            InsertMissingTrack(missingInRoot);
            Directory.Delete(_root, true);

            var scanner = new MusicScanner(_db, NullLogger<MusicScanner>.Instance);
            var result = await scanner.RefreshAndSyncLibraryAsync();

            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(missingInRoot));
            Assert.Contains(_anchorRoot, scanner.LastScannedRoots);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
            Assert.Equal(ScanRootStatus.RootUnavailable, scanner.LastRootStatus[_root]);
        }

        [Fact]
        public async Task RefreshAndSync_CancelledDuringEnumeration_PreservesMissingTrack()
        {
            string missingInRoot = Path.Combine(_root, "gone.wav");
            InsertMissingTrack(missingInRoot);
            using var cts = new CancellationTokenSource();

            var scanner = new SeamMusicScanner(_db, (folder, _) =>
            {
                if (string.Equals(folder, _root, StringComparison.OrdinalIgnoreCase))
                {
                    cts.Cancel();
                }
                return Complete(folder);
            });

            var result = await scanner.RefreshAndSyncLibraryAsync(cts.Token);

            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(missingInRoot));
            Assert.Equal(ScanRootStatus.Cancelled, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
            Assert.Contains(_root, scanner.LastFailedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_CancelledDuringDeletion_RollsBackEveryDeletion()
        {
            string first = Path.Combine(_root, "gone1.wav");
            string second = Path.Combine(_root, "gone2.wav");
            InsertMissingTrack(first);
            InsertMissingTrack(second);
            using var cts = new CancellationTokenSource();

            var scanner = new SeamMusicScanner(
                _db,
                (folder, _) => Complete(folder),
                beforeDelete: _ => cts.Cancel());

            var result = await scanner.RefreshAndSyncLibraryAsync(cts.Token);

            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(first));
            Assert.NotNull(_db.GetTrackByPath(second));
            Assert.Equal(ScanRootStatus.Cancelled, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_RootInvalidatedDuringDeletion_RollsBackEveryDeletion()
        {
            string first = Path.Combine(_root, "gone1.wav");
            string second = Path.Combine(_root, "gone2.wav");
            InsertMissingTrack(first);
            InsertMissingTrack(second);
            bool unavailable = false;

            var scanner = new SeamMusicScanner(
                _db,
                (folder, _) => Complete(folder),
                beforeDelete: _ => unavailable = true,
                availability: _ => unavailable
                    ? ScanRootStatus.RootUnavailable
                    : ScanRootStatus.Completed);

            var result = await scanner.RefreshAndSyncLibraryAsync();

            // The first row may have been deleted inside the transaction, but
            // the invalidated root must roll the whole transaction back.
            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(first));
            Assert.NotNull(_db.GetTrackByPath(second));
            Assert.Equal(ScanRootStatus.RootUnavailable, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_FinalValidationFailure_RollsBackAndReportsNoRemoved()
        {
            string first = Path.Combine(_root, "gone1.wav");
            string second = Path.Combine(_root, "gone2.wav");
            InsertMissingTrack(first);
            InsertMissingTrack(second);

            var scanner = new SeamMusicScanner(
                _db,
                (folder, _) => Complete(folder),
                failFinalValidation: true);

            var result = await scanner.RefreshAndSyncLibraryAsync();

            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(first));
            Assert.NotNull(_db.GetTrackByPath(second));
            Assert.Equal(ScanRootStatus.Partial, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_AccessDenied_PreservesTrack_ThenRetryDeletesIt()
        {
            string missingInRoot = Path.Combine(_root, "gone.wav");
            InsertMissingTrack(missingInRoot);
            int rootCalls = 0;

            var scanner = new SeamMusicScanner(_db, (folder, _) =>
            {
                if (string.Equals(folder, _root, StringComparison.OrdinalIgnoreCase))
                {
                    if (Interlocked.Increment(ref rootCalls) == 1)
                    {
                        throw new UnauthorizedAccessException("simulated ACL failure");
                    }
                }
                return Complete(folder);
            });

            var first = await scanner.RefreshAndSyncLibraryAsync();
            Assert.Equal(0, first.Removed);
            Assert.NotNull(_db.GetTrackByPath(missingInRoot));
            Assert.Equal(ScanRootStatus.AccessDenied, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);

            var second = await scanner.RefreshAndSyncLibraryAsync();
            Assert.Equal(1, second.Removed);
            Assert.Null(_db.GetTrackByPath(missingInRoot));
            Assert.Equal(ScanRootStatus.Completed, scanner.LastRootStatus[_root]);
            Assert.Contains(_root, scanner.LastScannedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_GenericEnumerationFailure_PreservesTrack()
        {
            string missingInRoot = Path.Combine(_root, "gone.wav");
            InsertMissingTrack(missingInRoot);

            var scanner = new SeamMusicScanner(_db, (folder, _) =>
            {
                if (string.Equals(folder, _root, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException("simulated IO failure");
                }
                return Complete(folder);
            });

            var result = await scanner.RefreshAndSyncLibraryAsync();

            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(missingInRoot));
            Assert.Equal(ScanRootStatus.Failed, scanner.LastRootStatus[_root]);
            Assert.Contains(_root, scanner.LastFailedRoots);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_CompleteRoot_DoesNotDeleteTrackFromUnscannedOutsideRoot()
        {
            string outsideTrack = Path.Combine(_outsideRoot, "outside-missing.wav");
            InsertMissingTrack(outsideTrack);

            var scanner = new MusicScanner(_db, NullLogger<MusicScanner>.Instance);
            await scanner.RefreshAndSyncLibraryAsync();

            Assert.NotNull(_db.GetTrackByPath(outsideTrack));
            Assert.DoesNotContain(_outsideRoot, scanner.LastScannedRoots);
            Assert.DoesNotContain(_outsideRoot, scanner.LastRootStatus.Keys);
        }

        [Fact]
        public async Task ScanFolders_RecordsUnavailableAndPartialRoots()
        {
            Directory.Delete(_root, true);
            string other = Path.Combine(_tempBase, "other");
            Directory.CreateDirectory(other);
            CreateWav(Path.Combine(other, "c.wav"));

            var scanner = new SeamMusicScanner(_db, (folder, _) =>
                string.Equals(folder, other, StringComparison.OrdinalIgnoreCase)
                    ? new RootEnumerationResult(Directory.GetFiles(folder, "*", SearchOption.AllDirectories), ScanRootStatus.Partial)
                    : Complete(folder));

            await scanner.ScanFoldersAsync(new[] { _root, other });

            Assert.Equal(ScanRootStatus.RootUnavailable, scanner.LastRootStatus[_root]);
            Assert.Equal(ScanRootStatus.Partial, scanner.LastRootStatus[other]);
            Assert.Contains(_root, scanner.LastFailedRoots);
            Assert.Contains(other, scanner.LastFailedRoots);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
            Assert.DoesNotContain(other, scanner.LastScannedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_NestedReparsePoint_IsPartialAndPreservesTracksBelowIt()
        {
            string sub = Path.Combine(_root, "sub");
            string missingInSkippedSubtree = Path.Combine(sub, "gone.wav");
            string existingInSkippedSubtree = Path.Combine(sub, "b.wav");
            InsertMissingTrack(missingInSkippedSubtree);
            InsertMissingTrack(existingInSkippedSubtree);

            var scanner = new MusicScanner(_db, NullLogger<MusicScanner>.Instance)
            {
                DirectoryAttributesProvider = path =>
                    string.Equals(path, sub, StringComparison.OrdinalIgnoreCase)
                        ? FileAttributes.Directory | FileAttributes.ReparsePoint
                        : File.GetAttributes(path)
            };

            var result = await scanner.RefreshAndSyncLibraryAsync();

            // The junction subtree was intentionally not traversed, so neither
            // the vanished file nor the present file below it may be judged.
            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(missingInSkippedSubtree));
            Assert.NotNull(_db.GetTrackByPath(existingInSkippedSubtree));
            Assert.Equal(ScanRootStatus.Partial, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_AccessDeniedSubdirectory_IsAccessDeniedAndPreservesTrack()
        {
            string sub = Path.Combine(_root, "sub");
            string missingInRoot = Path.Combine(_root, "gone.wav");
            InsertMissingTrack(missingInRoot);

            var scanner = new MusicScanner(_db, NullLogger<MusicScanner>.Instance)
            {
                DirectoryAttributesProvider = path =>
                {
                    if (string.Equals(path, sub, StringComparison.OrdinalIgnoreCase))
                        throw new UnauthorizedAccessException("simulated subtree ACL failure");
                    return File.GetAttributes(path);
                }
            };

            var result = await scanner.RefreshAndSyncLibraryAsync();

            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(missingInRoot));
            Assert.Equal(ScanRootStatus.AccessDenied, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_UninspectableSubdirectory_IsFailedAndPreservesTrack()
        {
            string sub = Path.Combine(_root, "sub");
            string missingInRoot = Path.Combine(_root, "gone.wav");
            InsertMissingTrack(missingInRoot);

            var scanner = new MusicScanner(_db, NullLogger<MusicScanner>.Instance)
            {
                DirectoryAttributesProvider = path =>
                {
                    if (string.Equals(path, sub, StringComparison.OrdinalIgnoreCase))
                        throw new IOException("simulated uninspectable directory");
                    return File.GetAttributes(path);
                }
            };

            var result = await scanner.RefreshAndSyncLibraryAsync();

            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(missingInRoot));
            Assert.Equal(ScanRootStatus.Failed, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
        }

        [Fact]
        public async Task RefreshAndSync_CancellationPropagatesThroughRecursiveTraversal()
        {
            string missingInRoot = Path.Combine(_root, "gone.wav");
            string sub = Path.Combine(_root, "sub");
            InsertMissingTrack(missingInRoot);
            using var cts = new CancellationTokenSource();

            var scanner = new MusicScanner(_db, NullLogger<MusicScanner>.Instance)
            {
                DirectoryFileProvider = path =>
                {
                    if (string.Equals(path, sub, StringComparison.OrdinalIgnoreCase))
                        cts.Cancel();
                    return Directory.GetFiles(path);
                }
            };

            var result = await scanner.RefreshAndSyncLibraryAsync(cts.Token);

            Assert.Equal(0, result.Removed);
            Assert.NotNull(_db.GetTrackByPath(missingInRoot));
            Assert.Equal(ScanRootStatus.Cancelled, scanner.LastRootStatus[_root]);
            Assert.DoesNotContain(_root, scanner.LastScannedRoots);
        }

        private static RootEnumerationResult Complete(string folder)
            => new(
                Directory.GetFiles(folder, "*", SearchOption.AllDirectories),
                ScanRootStatus.Completed);

        private void AddManagedRoot(string path, string name)
        {
            _db.AddManagedFolder(new ManagedMusicFolder
            {
                Path = path,
                Name = name,
                Enabled = true,
                AddedDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                LastScanDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            });
        }

        private MusicTrack InsertMissingTrack(string path)
        {
            var track = new MusicTrack
            {
                FilePath = path,
                Title = Path.GetFileNameWithoutExtension(path),
                Artist = "Test Artist",
                Album = "Test Album",
                FolderPath = Path.GetDirectoryName(path) ?? string.Empty
            };
            track.Id = _db.InsertTrack(track);
            return track;
        }

        private static void CreateWav(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var format = new WaveFormat(8000, 16, 1);
            using var writer = new WaveFileWriter(path, format);
            var buffer = new byte[format.AverageBytesPerSecond / 2];
            writer.Write(buffer, 0, buffer.Length);
        }

        private sealed class SeamMusicScanner : MusicScanner
        {
            private readonly Func<string, CancellationToken, RootEnumerationResult>? _enumerate;
            private readonly Func<string, ScanRootStatus>? _availability;
            private readonly Action<MusicTrack>? _beforeDelete;
            private readonly bool _failFinalValidation;

            public SeamMusicScanner(
                DatabaseService db,
                Func<string, CancellationToken, RootEnumerationResult>? enumerate = null,
                Func<string, ScanRootStatus>? availability = null,
                Action<MusicTrack>? beforeDelete = null,
                bool failFinalValidation = false)
                : base(db, NullLogger<MusicScanner>.Instance)
            {
                _enumerate = enumerate;
                _availability = availability;
                _beforeDelete = beforeDelete;
                _failFinalValidation = failFinalValidation;
            }

            protected override RootEnumerationResult EnumerateRoot(
                string root,
                CancellationToken cancellationToken)
                => _enumerate != null
                    ? _enumerate(root, cancellationToken)
                    : base.EnumerateRoot(root, cancellationToken);

            protected override ScanRootStatus CheckRootAvailability(string root)
                => _availability != null
                    ? _availability(root)
                    : base.CheckRootAvailability(root);

            protected override void BeforeDeletionFileCheck(MusicTrack track)
                => _beforeDelete?.Invoke(track);

            protected override bool ValidateRootsBeforeCommit(IReadOnlyList<string> roots)
                => !_failFinalValidation;
        }
    }
}
