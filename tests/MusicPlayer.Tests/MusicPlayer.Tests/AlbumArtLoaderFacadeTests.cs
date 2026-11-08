using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using MusicPlayer.Database;
using MusicPlayer.Library;
using MusicPlayer.Services;
using Xunit;

namespace MusicPlayer.Tests
{
    public class AlbumArtLoaderFacadeTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string _dbPath;
        private readonly DatabaseService _db;
        private readonly AlbumArtLoader _loader;

        public AlbumArtLoaderFacadeTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mp_facade_tests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
            _dbPath = Path.Combine(_tempDir, "test.db");
            _db = new DatabaseService(_dbPath, NullLogger<DatabaseService>.Instance);
            _loader = new AlbumArtLoader(_db, NullLogger<AlbumArtLoader>.Instance);
        }

        public void Dispose()
        {
            try { _db.Dispose(); } catch { }
            try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void Facade_UsesArtworkPipelineAsSourceOfTruth()
        {
            Assert.True(_loader.UsesArtworkPipelineAsSourceOfTruth);
        }

        [Theory]
        [InlineData("cover.png")]
        public void LoadAlbumArt_DoesNotPopulateLegacyCaches(string fileName)
        {
            var imageFile = Path.Combine(_tempDir, fileName);
            File.WriteAllBytes(imageFile, Create1x1Png());

            var result = _loader.LoadAlbumArt(imageFile);

            Assert.NotNull(result);
            Assert.Equal(0, _loader.LegacyImageCacheCount);
        }

        [Fact]
        public void LoadFullAndMiniAndCards_DoNotPopulateLegacyCaches()
        {
            var imageFile = Path.Combine(_tempDir, "cover2.png");
            File.WriteAllBytes(imageFile, Create1x1Png());

            Assert.NotNull(_loader.LoadFullAlbumArt(imageFile));
            Assert.NotNull(_loader.LoadMiniAlbumArt(imageFile));
            Assert.NotNull(_loader.LoadCardsAlbumArt(imageFile));
            Assert.NotNull(_loader.LoadMediumAlbumArt(imageFile));

            Assert.Equal(0, _loader.LegacyImageCacheCount);
        }

        private static byte[] Create1x1Png()
        {
            return new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52,
                0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
                0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4,
                0x89, 0x00, 0x00, 0x00, 0x0A, 0x49, 0x44, 0x41,
                0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
                0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00,
                0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE,
                0x42, 0x60, 0x82
            };
        }
    }
}
