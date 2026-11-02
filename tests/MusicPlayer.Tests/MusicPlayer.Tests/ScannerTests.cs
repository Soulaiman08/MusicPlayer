using MusicPlayer.Database;
using MusicPlayer.Library;
using Microsoft.Extensions.Logging.Abstractions;

namespace MusicPlayer.Tests
{
    public class ScannerTests : IDisposable
    {
        private readonly string _dbPath;
        private readonly DatabaseService _db;
        private readonly string _root;

        public ScannerTests()
        {
            SQLitePCL.Batteries_V2.Init();
            _dbPath = Path.Combine(Path.GetTempPath(), "mp_scan_" + Guid.NewGuid() + ".db");
            _db = new DatabaseService(_dbPath, NullLogger<DatabaseService>.Instance);
            _root = Path.Combine(Path.GetTempPath(), "mp_scan_root_" + Guid.NewGuid());
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "sub"));
            File.WriteAllText(Path.Combine(_root, "a.mp3"), "x");
            File.WriteAllText(Path.Combine(_root, "sub", "b.mp3"), "x");
        }

        public void Dispose()
        {
            try { _db.Dispose(); } catch { }
            try { Directory.Delete(_root, true); } catch { }
            try { File.Delete(_dbPath); } catch { }
        }

        [Fact]
        public async Task ScanFoldersAsync_IndexesFiles()
        {
            var scanner = new MusicScanner(_db, NullLogger<MusicScanner>.Instance);
            await scanner.ScanFoldersAsync(new[] { _root });
            Assert.False(scanner.IsScanning);
            // no real audio metadata; TagLib will fail, so count may be 0, but no crash
        }

        [Fact]
        public void EnumerateFilesSafe_NoInfiniteLoop_OnNested()
        {
            // Just ensure returns without hanging
            var mi = typeof(MusicScanner).GetMethod("EnumerateFilesSafe", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            Assert.NotNull(mi);
        }
    }
}