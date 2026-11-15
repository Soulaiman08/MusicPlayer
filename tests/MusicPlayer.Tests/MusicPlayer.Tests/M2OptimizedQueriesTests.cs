using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Database;
using MusicPlayer.Models;
using Xunit;

namespace MusicPlayer.Tests
{
    public class M2OptimizedQueriesTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DatabaseService _db;

        public M2OptimizedQueriesTests()
        {
            SQLitePCL.Batteries_V2.Init();
            _dbPath = Path.Combine(Path.GetTempPath(), "mp_m2_test_" + Guid.NewGuid() + ".db");
            _db = new DatabaseService(_dbPath, NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            try { _db.Dispose(); } catch { }
            try { File.Delete(_dbPath); } catch { }
            try { File.Delete(_dbPath + "-wal"); } catch { }
            try { File.Delete(_dbPath + "-shm"); } catch { }
        }

        #region Favorites Tests

        [Fact]
        public void Favorites_ReturnsOnlyQualified_ExcludesHiddenAndUnmanaged_PreservesSubfoldersAndOrder()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });

            // 1. Qualified direct favorite
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\b_song.mp3", Title = "B Song", IsFavorite = true, IsHidden = false });
            // 2. Qualified subfolder favorite
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Rock\a_song.mp3", Title = "A Song", IsFavorite = true, IsHidden = false });
            // 3. Hidden favorite (must be excluded)
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\c_song.mp3", Title = "C Hidden", IsFavorite = true, IsHidden = true });
            // 4. Non-favorite in managed folder (must be excluded)
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\d_song.mp3", Title = "D Not Fav", IsFavorite = false, IsHidden = false });
            // 5. Favorite in unmanaged folder (must be excluded)
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Other\e_song.mp3", Title = "E Unmanaged", IsFavorite = true, IsHidden = false });
            // 6. Favorite in similar folder name Music2 (must be excluded)
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music2\f_song.mp3", Title = "F Similar", IsFavorite = true, IsHidden = false });

            var favorites = _db.GetFavoriteTracks();

            // Must contain exactly A Song and B Song, ordered by Title ("A Song" then "B Song")
            Assert.Equal(2, favorites.Count);
            Assert.Equal("A Song", favorites[0].Title);
            Assert.Equal("B Song", favorites[1].Title);
        }

        #endregion

        #region Recent Play Tests

        [Fact]
        public void RecentPlay_OrdersByDateLastPlayedDesc_ExcludesUnplayedHiddenUnmanaged()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });

            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\p1.mp3", Title = "P1", DateLastPlayed = 100, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\p3.mp3", Title = "P3", DateLastPlayed = 300, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Sub\p2.mp3", Title = "P2", DateLastPlayed = 200, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\unplayed.mp3", Title = "Unplayed", DateLastPlayed = 0, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\hidden.mp3", Title = "Hidden Played", DateLastPlayed = 400, IsHidden = true });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Unmanaged\other.mp3", Title = "Unmanaged Played", DateLastPlayed = 500, IsHidden = false });

            var recent = _db.GetRecentPlay(50);

            Assert.Equal(3, recent.Count);
            Assert.Equal("P3", recent[0].Title); // 300
            Assert.Equal("P2", recent[1].Title); // 200
            Assert.Equal("P1", recent[2].Title); // 100
        }

        [Fact]
        public void RecentPlay_LargeNumberOfUnqualifiedCandidates_StillReturnsFullLimit()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });

            _db.ExecuteInTransaction(() =>
            {
                // Insert 120 unmanaged tracks with very high DateLastPlayed (5000 down to 4881)
                for (int i = 0; i < 120; i++)
                {
                    _db.InsertTrack(new MusicTrack
                    {
                        FilePath = $@"C:\UnmanagedFolder\track_{i}.mp3",
                        Title = $"Unmanaged {i}",
                        DateLastPlayed = 5000 - i,
                        IsHidden = false
                    });
                }

                // Insert 60 managed tracks with lower DateLastPlayed (4000 down to 3941)
                for (int i = 0; i < 60; i++)
                {
                    _db.InsertTrack(new MusicTrack
                    {
                        FilePath = $@"C:\Music\track_{i}.mp3",
                        Title = $"Managed {i}",
                        DateLastPlayed = 4000 - i,
                        IsHidden = false
                    });
                }
            });

            // Asking for 50 tracks: even though the first 120 top-played tracks in the DB are unmanaged,
            // the implementation must batch-fetch until it collects all 50 qualified managed tracks!
            var recent = _db.GetRecentPlay(50);

            Assert.Equal(50, recent.Count);
            Assert.All(recent, t => Assert.StartsWith(@"C:\Music\", t.FilePath, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Managed 0", recent[0].Title); // highest DateLastPlayed among managed (4000)
            Assert.Equal("Managed 49", recent[49].Title); // 50th item (3951)
        }

        #endregion

        #region Recent Add Tests

        [Fact]
        public void RecentAdd_OrdersByDateAddedDesc_ExcludesHiddenAndUnmanaged_RespectsLimit()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });

            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\a1.mp3", Title = "A1", DateAdded = 100, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\a3.mp3", Title = "A3", DateAdded = 300, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Sub\a2.mp3", Title = "A2", DateAdded = 200, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\hidden.mp3", Title = "Hidden", DateAdded = 400, IsHidden = true });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Other\unmanaged.mp3", Title = "Unmanaged", DateAdded = 500, IsHidden = false });

            var recentAdd = _db.GetRecentAdd(2); // limit = 2

            Assert.Equal(2, recentAdd.Count);
            Assert.Equal("A3", recentAdd[0].Title); // 300
            Assert.Equal("A2", recentAdd[1].Title); // 200
        }

        [Fact]
        public void RecentAdd_LargeNumberOfUnqualifiedCandidates_StillReturnsFullLimit()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });

            _db.ExecuteInTransaction(() =>
            {
                // 150 unmanaged tracks added recently
                for (int i = 0; i < 150; i++)
                {
                    _db.InsertTrack(new MusicTrack
                    {
                        FilePath = $@"C:\UnmanagedFolder\track_{i}.mp3",
                        Title = $"Unmanaged {i}",
                        DateAdded = 10000 - i,
                        IsHidden = false
                    });
                }

                // 55 managed tracks added earlier
                for (int i = 0; i < 55; i++)
                {
                    _db.InsertTrack(new MusicTrack
                    {
                        FilePath = $@"C:\Music\track_{i}.mp3",
                        Title = $"Managed {i}",
                        DateAdded = 5000 - i,
                        IsHidden = false
                    });
                }
            });

            var recentAdd = _db.GetRecentAdd(50);

            Assert.Equal(50, recentAdd.Count);
            Assert.All(recentAdd, t => Assert.StartsWith(@"C:\Music\", t.FilePath, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Managed 0", recentAdd[0].Title);
            Assert.Equal("Managed 49", recentAdd[49].Title);
        }

        #endregion

        #region Most Played Tests

        [Fact]
        public void MostPlayed_OrdersByPlayCountDesc_ExcludesZeroAndNegative_ExcludesHiddenAndUnmanaged()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });

            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\m1.mp3", Title = "M1", PlayCount = 10, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\m3.mp3", Title = "M3", PlayCount = 30, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Sub\m2.mp3", Title = "M2", PlayCount = 20, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\zero.mp3", Title = "Zero", PlayCount = 0, IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\hidden.mp3", Title = "Hidden", PlayCount = 50, IsHidden = true });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Other\unmanaged.mp3", Title = "Unmanaged", PlayCount = 100, IsHidden = false });

            var mostPlay = _db.GetMostPlay(50);

            Assert.Equal(3, mostPlay.Count);
            Assert.Equal("M3", mostPlay[0].Title); // 30
            Assert.Equal("M2", mostPlay[1].Title); // 20
            Assert.Equal("M1", mostPlay[2].Title); // 10
        }

        [Fact]
        public void MostPlayed_LargeNumberOfUnqualifiedCandidates_StillReturnsFullLimit()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });

            _db.ExecuteInTransaction(() =>
            {
                // 150 unmanaged tracks with huge play counts
                for (int i = 0; i < 150; i++)
                {
                    _db.InsertTrack(new MusicTrack
                    {
                        FilePath = $@"C:\UnmanagedFolder\track_{i}.mp3",
                        Title = $"Unmanaged {i}",
                        PlayCount = 1000 - i,
                        IsHidden = false
                    });
                }

                // 60 managed tracks with lower play counts
                for (int i = 0; i < 60; i++)
                {
                    _db.InsertTrack(new MusicTrack
                    {
                        FilePath = $@"C:\Music\track_{i}.mp3",
                        Title = $"Managed {i}",
                        PlayCount = 200 - i,
                        IsHidden = false
                    });
                }
            });

            var mostPlay = _db.GetMostPlay(50);

            Assert.Equal(50, mostPlay.Count);
            Assert.All(mostPlay, t => Assert.StartsWith(@"C:\Music\", t.FilePath, StringComparison.OrdinalIgnoreCase));
            Assert.Equal("Managed 0", mostPlay[0].Title);
            Assert.Equal("Managed 49", mostPlay[49].Title);
        }

        #endregion

        #region Common & Boundary Tests

        [Fact]
        public void Common_NoManagedFolders_AllReturnEmpty()
        {
            // Do NOT add any managed folders
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\song.mp3", Title = "S", IsFavorite = true, DateLastPlayed = 100, PlayCount = 10, DateAdded = 100 });

            Assert.Empty(_db.GetFavoriteTracks());
            Assert.Empty(_db.GetRecentPlay(50));
            Assert.Empty(_db.GetRecentAdd(50));
            Assert.Empty(_db.GetMostPlay(50));
        }

        [Fact]
        public void Common_MultipleManagedFolders_IncludesTracksFromAll()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\FolderA", Name = "Folder A", Enabled = true });
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"D:\FolderB", Name = "Folder B", Enabled = true });

            _db.InsertTrack(new MusicTrack { FilePath = @"C:\FolderA\songA.mp3", Title = "Song A", IsFavorite = true, DateLastPlayed = 10, DateAdded = 10, PlayCount = 10 });
            _db.InsertTrack(new MusicTrack { FilePath = @"D:\FolderB\songB.mp3", Title = "Song B", IsFavorite = true, DateLastPlayed = 20, DateAdded = 20, PlayCount = 20 });

            var favs = _db.GetFavoriteTracks();
            Assert.Equal(2, favs.Count);

            var recentPlay = _db.GetRecentPlay(50);
            Assert.Equal(2, recentPlay.Count);
            Assert.Equal("Song B", recentPlay[0].Title);

            var recentAdd = _db.GetRecentAdd(50);
            Assert.Equal(2, recentAdd.Count);
            Assert.Equal("Song B", recentAdd[0].Title);

            var mostPlay = _db.GetMostPlay(50);
            Assert.Equal(2, mostPlay.Count);
            Assert.Equal("Song B", mostPlay[0].Title);
        }

        [Fact]
        public void Common_NestedFolders_NoDuplicates()
        {
            // C:\Music and C:\Music\Sub both registered as managed folders
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music\Sub", Name = "Music Sub", Enabled = true });

            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Sub\track.mp3", Title = "Track Sub", IsFavorite = true, DateLastPlayed = 10, DateAdded = 10, PlayCount = 10 });

            // Each method must return the track exactly ONCE
            Assert.Single(_db.GetFavoriteTracks());
            Assert.Single(_db.GetRecentPlay(50));
            Assert.Single(_db.GetRecentAdd(50));
            Assert.Single(_db.GetMostPlay(50));
        }

        [Fact]
        public void Common_SimilarFolderNames_DoNotMix()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });

            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\song.mp3", Title = "In Music", IsFavorite = true, DateLastPlayed = 10, DateAdded = 10, PlayCount = 10 });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music2\song2.mp3", Title = "In Music2", IsFavorite = true, DateLastPlayed = 20, DateAdded = 20, PlayCount = 20 });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music_Extra\song3.mp3", Title = "In MusicExtra", IsFavorite = true, DateLastPlayed = 30, DateAdded = 30, PlayCount = 30 });

            var favs = _db.GetFavoriteTracks();
            Assert.Single(favs);
            Assert.Equal("In Music", favs[0].Title);

            var recentPlay = _db.GetRecentPlay(50);
            Assert.Single(recentPlay);
            Assert.Equal("In Music", recentPlay[0].Title);

            var recentAdd = _db.GetRecentAdd(50);
            Assert.Single(recentAdd);
            Assert.Equal("In Music", recentAdd[0].Title);

            var mostPlay = _db.GetMostPlay(50);
            Assert.Single(mostPlay);
            Assert.Equal("In Music", mostPlay[0].Title);
        }

        [Fact]
        public void Common_UnicodePaths_HandledCorrectly()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\موسيقى", Name = "Arabic Music", Enabled = true });
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Μουσική", Name = "Greek Music", Enabled = true });

            _db.InsertTrack(new MusicTrack { FilePath = @"C:\موسيقى\arabic.mp3", Title = "Arabic", IsFavorite = true, DateLastPlayed = 10, DateAdded = 10, PlayCount = 10 });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Μουσική\greek.mp3", Title = "Greek", IsFavorite = true, DateLastPlayed = 20, DateAdded = 20, PlayCount = 20 });

            Assert.Equal(2, _db.GetFavoriteTracks().Count);
            Assert.Equal(2, _db.GetRecentPlay(50).Count);
            Assert.Equal(2, _db.GetRecentAdd(50).Count);
            Assert.Equal(2, _db.GetMostPlay(50).Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(-100)]
        public void Common_ZeroOrNegativeLimit_ReturnsEmpty(int limit)
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\song.mp3", Title = "Song", DateLastPlayed = 10, DateAdded = 10, PlayCount = 10 });

            Assert.Empty(_db.GetRecentPlay(limit));
            Assert.Empty(_db.GetRecentAdd(limit));
            Assert.Empty(_db.GetMostPlay(limit));
        }

        #endregion
    }
}
