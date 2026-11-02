using MusicPlayer.Audio;

namespace MusicPlayer.Tests
{
    public class AudioEngineTests
    {
        [Fact]
        public void Dispose_Twice_NoCrash()
        {
            var e = new AudioEngine();
            e.Dispose();
            e.Dispose();
        }

        [Fact]
        public void LoadAndPlayAsync_AfterDispose_DoesNotCrash()
        {
            var e = new AudioEngine();
            e.Dispose();
            // Should be ignored
            e.LoadAndPlayAsync(new MusicPlayer.Models.MusicTrack { FilePath = "x.mp3", Title = "x" });
        }
    }
}