using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Database;
using MusicPlayer.Models;
using Xunit;
using Xunit.Abstractions;

namespace MusicPlayer.Tests
{
    public class QueryBenchmarkTests : IDisposable
    {
        private readonly ITestOutputHelper _output;
        private readonly string _dbPath;
        private readonly DatabaseService _db;

        public QueryBenchmarkTests(ITestOutputHelper output)
        {
            _output = output;
            SQLitePCL.Batteries_V2.Init();
            _dbPath = Path.Combine(Path.GetTempPath(), "mp_bench_" + Guid.NewGuid() + ".db");
            _db = new DatabaseService(_dbPath, NullLogger<DatabaseService>.Instance);
        }

        public void Dispose()
        {
            try { _db.Dispose(); } catch { }
            try { File.Delete(_dbPath); } catch { }
            try { File.Delete(_dbPath + "-wal"); } catch { }
            try { File.Delete(_dbPath + "-shm"); } catch { }
        }

        private void SeedLibrary(int trackCount)
        {
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\Music", Name = "Music", Enabled = true });
            _db.AddManagedFolder(new ManagedMusicFolder { Path = @"C:\OtherMusic", Name = "OtherMusic", Enabled = true });

            var random = new Random(42);
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            _db.ExecuteInTransaction(() =>
            {
                for (int i = 0; i < trackCount; i++)
                {
                    // 90% in C:\Music, 8% in C:\OtherMusic, 2% unmanaged in C:\Unmanaged
                    string folder = (i % 50 == 0) ? @"C:\Unmanaged" : ((i % 10 == 0) ? @"C:\OtherMusic" : @"C:\Music");
                    string sub = (i % 3 == 0) ? @"Sub\" : "";
                    string path = Path.Combine(folder, sub, $"track_{i:D5}.mp3");

                    bool isHidden = (i % 100 == 99); // 1% hidden
                    bool isFav = (i % 7 == 0);       // ~14% favorite
                    long played = (i % 4 == 0) ? (now - random.Next(1, 3000000)) : 0; // 25% played
                    int playCount = (played > 0) ? random.Next(1, 100) : 0;
                    long added = now - (trackCount - i) * 60; // added sequentially

                    _db.InsertTrack(new MusicTrack
                    {
                        FilePath = path,
                        Title = $"Title {i:D5}",
                        Artist = $"Artist {i % 50}",
                        Album = $"Album {i % 100}",
                        Duration = 180 + (i % 120),
                        IsHidden = isHidden,
                        IsFavorite = isFav,
                        DateLastPlayed = played,
                        PlayCount = playCount,
                        DateAdded = added,
                        Year = 2000 + (i % 25)
                    });
                }
            });
        }

        [Theory]
        [InlineData(201)]
        [InlineData(5000)]
        public void Benchmark_Current_Implementation(int count)
        {
            SeedLibrary(count);

            // Measure GetFavoriteTracks
            var sw = Stopwatch.StartNew();
            var favs = _db.GetFavoriteTracks();
            sw.Stop();
            long favMs = sw.ElapsedMilliseconds;

            // Measure GetRecentPlay
            sw.Restart();
            var recentPlay = _db.GetRecentPlay(50);
            sw.Stop();
            long recentPlayMs = sw.ElapsedMilliseconds;

            // Measure GetRecentAdd
            sw.Restart();
            var recentAdd = _db.GetRecentAdd(50);
            sw.Stop();
            long recentAddMs = sw.ElapsedMilliseconds;

            // Measure GetMostPlay
            sw.Restart();
            var mostPlay = _db.GetMostPlay(50);
            sw.Stop();
            long mostPlayMs = sw.ElapsedMilliseconds;

            _output.WriteLine($"[BASELINE {count} tracks]");
            _output.WriteLine($"Favorites: count={favs.Count}, time={favMs}ms");
            _output.WriteLine($"RecentPlay: count={recentPlay.Count}, time={recentPlayMs}ms");
            _output.WriteLine($"RecentAdd: count={recentAdd.Count}, time={recentAddMs}ms");
            _output.WriteLine($"MostPlay: count={mostPlay.Count}, time={mostPlayMs}ms");

            int expectedPlayed = Math.Min(50, count / 4 - (count / 200)); // minus unmanaged played
            Assert.NotEmpty(favs);
            Assert.True(recentPlay.Count <= 50 && recentPlay.Count > 0);
            Assert.Equal(50, recentAdd.Count);
            Assert.True(mostPlay.Count <= 50 && mostPlay.Count > 0);
        }

        [Fact]
        public void ExplainQueryPlans_Current()
        {
            SeedLibrary(500);

            using var conn = new SQLite.SQLiteConnection(_dbPath);

            var queries = new Dictionary<string, string>
            {
                ["Favorites_Old"] = "SELECT * FROM tracks WHERE IsHidden = 0 AND IsFavorite = 1",
                ["RecentPlay_Old"] = "SELECT * FROM tracks WHERE IsHidden = 0 AND DateLastPlayed > 0 ORDER BY DateLastPlayed DESC",
                ["RecentAdd_Old"] = "SELECT * FROM tracks WHERE IsHidden = 0 ORDER BY DateAdded DESC",
                ["MostPlay_Old"] = "SELECT * FROM tracks WHERE IsHidden = 0 AND PlayCount > 0 ORDER BY PlayCount DESC"
            };

            foreach (var kvp in queries)
            {
                _output.WriteLine($"--- EXPLAIN QUERY PLAN (Before): {kvp.Key} ---");
                var plans = conn.Query<ExplainPlanRow>($"EXPLAIN QUERY PLAN {kvp.Value}");
                foreach (var p in plans)
                {
                    _output.WriteLine($"id={p.id}, detail={p.detail}");
                }
            }

            // Create proposed indexes
            conn.Execute("CREATE INDEX IF NOT EXISTS idx_tracks_favorite_opt ON tracks (IsHidden, IsFavorite, Title COLLATE NOCASE)");
            conn.Execute("CREATE INDEX IF NOT EXISTS idx_tracks_recent_play_opt ON tracks (IsHidden, DateLastPlayed DESC)");
            conn.Execute("CREATE INDEX IF NOT EXISTS idx_tracks_recent_add_opt ON tracks (IsHidden, DateAdded DESC)");
            conn.Execute("CREATE INDEX IF NOT EXISTS idx_tracks_most_play_opt ON tracks (IsHidden, PlayCount DESC)");

            var optQueries = new Dictionary<string, string>
            {
                ["Favorites_New"] = "SELECT * FROM tracks WHERE IsHidden = 0 AND IsFavorite = 1 ORDER BY Title COLLATE NOCASE",
                ["RecentPlay_New"] = "SELECT * FROM tracks WHERE IsHidden = 0 AND DateLastPlayed > 0 ORDER BY DateLastPlayed DESC LIMIT 50",
                ["RecentAdd_New"] = "SELECT * FROM tracks WHERE IsHidden = 0 ORDER BY DateAdded DESC LIMIT 50",
                ["MostPlay_New"] = "SELECT * FROM tracks WHERE IsHidden = 0 AND PlayCount > 0 ORDER BY PlayCount DESC LIMIT 50"
            };

            foreach (var kvp in optQueries)
            {
                _output.WriteLine($"--- EXPLAIN QUERY PLAN (After): {kvp.Key} ---");
                var plans = conn.Query<ExplainPlanRow>($"EXPLAIN QUERY PLAN {kvp.Value}");
                foreach (var p in plans)
                {
                    _output.WriteLine($"id={p.id}, detail={p.detail}");
                }
            }
        }
    }

    public class ExplainPlanRow
    {
        public int id { get; set; }
        public int parent { get; set; }
        public int notused { get; set; }
        public string detail { get; set; } = string.Empty;
    }
}
