using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Database;
using MusicPlayer.Models;
using Xunit;

namespace MusicPlayer.Tests
{
    public class FolderDetailTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DatabaseService _db;

        public FolderDetailTests()
        {
            SQLitePCL.Batteries_V2.Init();
            _dbPath = Path.Combine(Path.GetTempPath(), "mp_folder_test_" + Guid.NewGuid() + ".db");
            _db = new DatabaseService(_dbPath, NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            try { _db.Dispose(); } catch { }
            try { File.Delete(_dbPath); } catch { }
            try { File.Delete(_dbPath + "-wal"); } catch { }
            try { File.Delete(_dbPath + "-shm"); } catch { }
        }

        [Fact]
        public void Scenario1_FolderWithDirectTracks_ReturnsDirectTracks()
        {
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\song1.mp3", Title = "Song 1" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\song2.mp3", Title = "Song 2" });

            var tracks = _db.GetFolderTracks(@"C:\Music");

            Assert.Equal(2, tracks.Count);
            Assert.Contains(tracks, t => t.Title == "Song 1");
            Assert.Contains(tracks, t => t.Title == "Song 2");
        }

        [Fact]
        public void Scenario2_FolderWithNoDirectTracks_HasSubfolderTracks_ReturnsSubfolderTracks()
        {
            // Folder C:\Music has no direct tracks, only tracks inside C:\Music\Rock
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Rock\rock1.mp3", Title = "Rock 1" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Rock\rock2.mp3", Title = "Rock 2" });

            var tracks = _db.GetFolderTracks(@"C:\Music");

            Assert.Equal(2, tracks.Count);
            Assert.Contains(tracks, t => t.Title == "Rock 1");
            Assert.Contains(tracks, t => t.Title == "Rock 2");
        }

        [Fact]
        public void Scenario3_FolderWithDirectAndSubfolderTracks_ReturnsAll()
        {
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\direct.mp3", Title = "Direct" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Sub1\sub1.mp3", Title = "Sub 1" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Sub2\sub2.mp3", Title = "Sub 2" });

            var tracks = _db.GetFolderTracks(@"C:\Music");

            Assert.Equal(3, tracks.Count);
            Assert.Contains(tracks, t => t.Title == "Direct");
            Assert.Contains(tracks, t => t.Title == "Sub 1");
            Assert.Contains(tracks, t => t.Title == "Sub 2");
        }

        [Fact]
        public void Scenario4_FolderWithDeepSubfolders_ReturnsDeepTracks()
        {
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Level1\Level2\Level3\Level4\deep.mp3", Title = "Deep Song" });

            var tracks = _db.GetFolderTracks(@"C:\Music");

            Assert.Single(tracks);
            Assert.Equal("Deep Song", tracks[0].Title);
        }

        [Fact]
        public void Scenario5_HiddenTracks_AreExcluded()
        {
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\visible.mp3", Title = "Visible", IsHidden = false });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\hidden.mp3", Title = "Hidden", IsHidden = true });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Sub\sub_hidden.mp3", Title = "Sub Hidden", IsHidden = true });

            var tracks = _db.GetFolderTracks(@"C:\Music");

            Assert.Single(tracks);
            Assert.Equal("Visible", tracks[0].Title);
        }

        [Fact]
        public void Scenario6_SimilarFolderNames_DoNotMixContents()
        {
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\song.mp3", Title = "Music Song" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music2\song2.mp3", Title = "Music2 Song" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music_Extra\extra.mp3", Title = "Extra Song" });

            var tracks = _db.GetFolderTracks(@"C:\Music");

            Assert.Single(tracks);
            Assert.Equal("Music Song", tracks[0].Title);

            var tracks2 = _db.GetFolderTracks(@"C:\Music2");
            Assert.Single(tracks2);
            Assert.Equal("Music2 Song", tracks2[0].Title);
        }

        [Fact]
        public void Scenario7_CaseInsensitivityAndTrailingSeparators_WorkConsistently()
        {
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\song.mp3", Title = "Song" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Sub\sub.mp3", Title = "Sub" });

            // Test lowercase
            var lower = _db.GetFolderTracks(@"c:\music");
            Assert.Equal(2, lower.Count);

            // Test uppercase with trailing backslash
            var upperTrailing = _db.GetFolderTracks(@"C:\MUSIC\");
            Assert.Equal(2, upperTrailing.Count);

            // Test forward slashes
            var forwardSlash = _db.GetFolderTracks(@"C:/Music/");
            Assert.Equal(2, forwardSlash.Count);
        }

        [Fact]
        public void FolderDetailCount_Matches_GetFolderAggregates()
        {
            string folderPath = @"C:\ManagedMusic";
            _db.AddManagedFolder(new ManagedMusicFolder { Path = folderPath, Name = "ManagedMusic", Enabled = true });

            _db.InsertTrack(new MusicTrack { FilePath = @"C:\ManagedMusic\track1.mp3", Title = "Track 1", Duration = 100 });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\ManagedMusic\Rock\track2.mp3", Title = "Track 2", Duration = 200 });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\ManagedMusic\Jazz\Modern\track3.mp3", Title = "Track 3", Duration = 300 });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\ManagedMusic\hidden.mp3", Title = "Hidden", IsHidden = true, Duration = 400 });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\ManagedMusic2\other.mp3", Title = "Other", Duration = 500 });

            // Folder Aggregates (what card displays)
            var aggregates = _db.GetFolderAggregates();
            Assert.True(aggregates.TryGetValue(folderPath, out var agg));
            Assert.Equal(3, agg!.TrackCount);
            Assert.Equal(600, agg.TotalDuration);

            // Folder Detail Tracks (what detail page displays)
            var detailTracks = _db.GetFolderTracks(folderPath);

            Assert.Equal(agg.TrackCount, detailTracks.Count);
            Assert.DoesNotContain(detailTracks, t => t.Title == "Hidden");
            Assert.DoesNotContain(detailTracks, t => t.Title == "Other");
        }

        [Fact]
        public void GetManagedFolderTrackCount_CalculatesCorrectCountWithoutException()
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Podcasts", Name = "Podcasts", Enabled = true });
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Disabled", Name = "Disabled", Enabled = false });

            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\m1.mp3", Title = "M1" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\Sub\m2.mp3", Title = "M2" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Podcasts\p1.mp3", Title = "P1" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Disabled\d1.mp3", Title = "D1" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music\hidden.mp3", Title = "H", IsHidden = true });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music2\other.mp3", Title = "O" });

            int count = _db.GetManagedFolderTrackCount();

            // Should count m1, m2, p1 = 3
            Assert.Equal(3, count);
        }

        [Fact]
        public void EmptyOrWhitespaceFolder_ReturnsEmptyList()
        {
            Assert.Empty(_db.GetFolderTracks(""));
            Assert.Empty(_db.GetFolderTracks("   "));
            Assert.Empty(_db.GetFolderTracks(null!));
        }

        [Fact]
        public void FolderWithPercentAndUnderscore_NotTreatedAsWildcards()
        {
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music_2024\song1.mp3", Title = "Song Under" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Music-2024\song2.mp3", Title = "Song Dash" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\100% Hits\song3.mp3", Title = "Song Percent" });
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\100 Hits\song4.mp3", Title = "Song NonPercent" });

            var underTracks = _db.GetFolderTracks(@"C:\Music_2024");
            Assert.Single(underTracks);
            Assert.Equal("Song Under", underTracks[0].Title);

            var percentTracks = _db.GetFolderTracks(@"C:\100% Hits");
            Assert.Single(percentTracks);
            Assert.Equal("Song Percent", percentTracks[0].Title);
        }

        [Fact]
        public void FolderWithUnicodeAndCasingDifferences_ReturnsTracksCorrectly()
        {
            // Greek with casing difference: Upper Μουσική vs Lower μουσική
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\Μουσική\track_greek.mp3", Title = "Greek Track" });
            // Arabic unicode
            _db.InsertTrack(new MusicTrack { FilePath = @"C:\موسيقى\track_arabic.mp3", Title = "Arabic Track" });

            var greekTracks = _db.GetFolderTracks(@"C:\μουσική");
            Assert.Single(greekTracks);
            Assert.Equal("Greek Track", greekTracks[0].Title);

            var arabicTracks = _db.GetFolderTracks(@"C:\موسيقى");
            Assert.Single(arabicTracks);
            Assert.Equal("Arabic Track", arabicTracks[0].Title);
        }
    }
}
