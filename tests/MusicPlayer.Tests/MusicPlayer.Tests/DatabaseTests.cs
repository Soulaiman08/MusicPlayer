using MusicPlayer.Database;
using Microsoft.Extensions.Logging.Abstractions;

namespace MusicPlayer.Tests
{
    public class DatabaseTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DatabaseService _db;

        public DatabaseTests()
        {
            SQLitePCL.Batteries_V2.Init();
            _dbPath = Path.Combine(Path.GetTempPath(), "mp_test_" + Guid.NewGuid() + ".db");
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
        public void InsertAndReadTrack()
        {
            var t = new MusicPlayer.Models.MusicTrack { FilePath = @"C:\m\a.mp3", Title = "A", Artist = "X" };
            var id = _db.InsertTrack(t);
            Assert.True(id > 0);
            var got = _db.GetTrack(id);
            Assert.NotNull(got);
            Assert.Equal("A", got!.Title);
        }

        [Fact]
        public void Transaction_Commit_Persists()
        {
            _db.ExecuteInTransaction(() =>
            {
                _db.InsertTrack(new MusicPlayer.Models.MusicTrack { FilePath = @"C:\m\b.mp3", Title = "B" });
            });
            Assert.Equal(1, _db.GetTrackCount());
        }

        [Fact]
        public void Transaction_Rollback_DoesNotPersist()
        {
            try
            {
                _db.ExecuteInTransaction(() =>
                {
                    _db.InsertTrack(new MusicPlayer.Models.MusicTrack { FilePath = @"C:\m\c.mp3", Title = "C" });
                    throw new InvalidOperationException("boom");
                });
            }
            catch (InvalidOperationException) { }
            Assert.Equal(0, _db.GetTrackCount());
        }

        [Fact]
        public void Transaction_WrongThreadCommit_Throws()
        {
            _db.BeginTransaction();
            Exception? ex = null;
            var t = Task.Run(() =>
            {
                try { _db.Commit(); }
                catch (Exception e) { ex = e; }
            });
            t.Wait();
            try { _db.Rollback(); } catch { }
            Assert.NotNull(ex);
        }

        [Fact]
        public void ConcurrentReadsAndWrites_NoCrash()
        {
            for (int i = 0; i < 50; i++)
                _db.InsertTrack(new MusicPlayer.Models.MusicTrack { FilePath = @"C:\m\" + i + ".mp3", Title = "T" + i });

            var tasks = new List<Task>();
            for (int r = 0; r < 8; r++)
            {
                tasks.Add(Task.Run(() => { for (int k = 0; k < 20; k++) _db.GetAllTracks(); }));
            }
            for (int w = 0; w < 4; w++)
            {
                tasks.Add(Task.Run(() =>
                {
                    for (int k = 0; k < 10; k++)
                        _db.InsertTrack(new MusicPlayer.Models.MusicTrack { FilePath = @"C:\m\w" + Guid.NewGuid() + ".mp3", Title = "W" });
                }));
            }
            Task.WaitAll(tasks.ToArray());
            Assert.True(_db.GetTrackCount() > 0);
        }

        [Fact]
        public void IntegrityCheck_Passes()
        {
            _db.InsertTrack(new MusicPlayer.Models.MusicTrack { FilePath = @"C:\m\i.mp3", Title = "I" });
            using var conn = new SQLite.SQLiteConnection(_dbPath);
            var res = conn.ExecuteScalar<string>("PRAGMA integrity_check;");
            Assert.Equal("ok", res);
        }

        [Fact]
        public void Dispose_Twice_NoCrash()
        {
            _db.Dispose();
            _db.Dispose();
        }

        [Fact]
        public void CleanOrphanTracks_NoManagedFolders_PreservesTracks()
        {
            _db.InsertTrack(new MusicPlayer.Models.MusicTrack { FilePath = @"C:\m\o.mp3", Title = "O" });
            _db.CleanOrphanTracks();
            Assert.Equal(1, _db.GetTrackCount());
        }

        [Fact]
        public void RemoveMissingTracks_UnscannedRoot_PreservesTrack()
        {
            _db.InsertTrack(new MusicPlayer.Models.MusicTrack { FilePath = @"C:\external\gone.mp3", Title = "Gone" });
            // No scanned roots provided -> must NOT delete.
            _db.RemoveMissingTracks(null);
            Assert.Equal(1, _db.GetTrackCount());
        }

        [Fact]
        public void RemoveMissingTracks_RemovesNonexistent()
        {
            _db.InsertTrack(new MusicPlayer.Models.MusicTrack { FilePath = @"C:\definitely\not\there.mp3", Title = "Gone" });
            _db.RemoveMissingTracks(new[] { @"C:\definitely" });
            Assert.Equal(0, _db.GetTrackCount());
        }
    }
}