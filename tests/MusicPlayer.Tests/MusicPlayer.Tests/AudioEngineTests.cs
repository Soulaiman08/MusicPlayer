using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Audio;
using MusicPlayer.Models;
using NAudio.Wave;

namespace MusicPlayer.Tests
{
    public class AudioEngineTests : IDisposable
    {
        private readonly string _tempDir;
        private int _nextTrackId;

        public AudioEngineTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "mp_audio_" + Guid.NewGuid());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Fact]
        public void Dispose_Twice_NoCrash()
        {
            var e = new AudioEngine();
            e.Dispose();
            e.Dispose();
        }

        [Fact]
        public async Task LoadAndPlayAsync_AfterDispose_DoesNotStartWorkerOrReader()
        {
            var engine = new TestAudioEngine();
            try
            {
                await engine.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
                int readerCount = engine.ReaderSnapshot.Length;
                int deviceCount = engine.DeviceSnapshot.Length;

                engine.LoadTrack(Track("after-dispose.wav"));
                engine.LoadAndPlayAsync(Track("after-dispose-2.wav"));
                await Task.Delay(150);

                Assert.Equal(readerCount, engine.ReaderSnapshot.Length);
                Assert.Equal(deviceCount, engine.DeviceSnapshot.Length);
                Assert.True(engine.ShutdownAsync().IsCompleted);
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Fact]
        public async Task LoadingNewTrack_DisposesOldReaderOnlyAfterSuccessfulRetirement()
        {
            var engine = new TestAudioEngine();
            try
            {
                MusicTrack first = Track("first.wav");
                MusicTrack second = Track("second.wav");

                engine.LoadTrack(first);
                FakeWavePlayer firstDevice = engine.DeviceSnapshot[0];
                TrackingAudioFileReader firstReader = engine.ReaderSnapshot[0];

                Assert.Equal(1, firstDevice.InitCalls);
                Assert.Equal(0, firstDevice.StopCalls);
                Assert.False(firstReader.IsDisposed);

                engine.LoadTrack(second);

                Assert.True(firstReader.IsDisposed);
                Assert.Equal(1, firstDevice.StopCalls);
                Assert.Equal(1, firstDevice.DisposeCalls);

                TrackingAudioFileReader secondReader = engine.ReaderSnapshot[1];
                Assert.False(secondReader.IsDisposed);

                await engine.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

                Assert.True(secondReader.IsDisposed);
                Assert.Equal(1, engine.DeviceSnapshot[1].StopCalls);
                Assert.Equal(1, engine.DeviceSnapshot[1].DisposeCalls);
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Theory]
        [InlineData(true, false)]
        [InlineData(false, true)]
        public async Task RetireStopOrDisposeFailure_RetainsOldReaderUntilExplicitCleanup(
            bool stopFails,
            bool disposeFails)
        {
            var engine = new TestAudioEngine();
            try
            {
                engine.LoadTrack(Track("retire-a.wav"));
                FakeWavePlayer oldDevice = engine.DeviceSnapshot[0];
                TrackingAudioFileReader oldReader = engine.ReaderSnapshot[0];

                oldDevice.ThrowOnStop = stopFails;
                oldDevice.ThrowOnDispose = disposeFails;

                engine.LoadTrack(Track("retire-b.wav"));

                Assert.Equal(1, oldDevice.StopCalls);
                Assert.Equal(1, oldDevice.DisposeCalls);
                Assert.False(oldReader.IsDisposed);

                await engine.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

                // Ownership is unconfirmed after Stop/Dispose failure: shutdown
                // must not free the reader under the old device.
                Assert.False(oldReader.IsDisposed);
                Assert.True(engine.ReaderSnapshot[1].IsDisposed);

                // Test-only cleanup of the deliberately retained reader.
                oldReader.Dispose();
                Assert.True(oldReader.IsDisposed);
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Fact]
        public async Task RetireTimeout_DefersReaderDisposal_UntilDeviceActuallyStops()
        {
            var engine = new TestAudioEngine
            {
                RetireWaitTimeout = TimeSpan.FromMilliseconds(150)
            };
            try
            {
                engine.LoadTrack(Track("timeout-a.wav"));
                FakeWavePlayer oldDevice = engine.DeviceSnapshot[0];
                TrackingAudioFileReader oldReader = engine.ReaderSnapshot[0];
                oldDevice.HangOnStop = true;

                var sw = Stopwatch.StartNew();
                engine.LoadTrack(Track("timeout-b.wav"));
                sw.Stop();

                Assert.True(sw.Elapsed >= TimeSpan.FromMilliseconds(100),
                    "LoadTrack should have waited for the configured retirement timeout.");
                Assert.False(oldReader.IsDisposed);

                oldDevice.ReleaseStop();
                await WaitUntilAsync(() => oldReader.IsDisposed, TimeSpan.FromSeconds(5));

                Assert.True(oldReader.IsDisposed);
                Assert.Equal(1, oldDevice.DisposeCalls);

                await engine.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Fact]
        public async Task Shutdown_WaitsForHungDeviceRetirement_AndDisposesAllReaders()
        {
            var engine = new TestAudioEngine
            {
                RetireWaitTimeout = TimeSpan.FromMilliseconds(100)
            };
            try
            {
                engine.LoadTrack(Track("shutdown-a.wav"));
                FakeWavePlayer oldDevice = engine.DeviceSnapshot[0];
                TrackingAudioFileReader oldReader = engine.ReaderSnapshot[0];
                oldDevice.HangOnStop = true;

                engine.LoadTrack(Track("shutdown-b.wav"));
                TrackingAudioFileReader currentReader = engine.ReaderSnapshot[1];
                Assert.False(oldReader.IsDisposed);

                Task shutdown = engine.ShutdownAsync();
                await Task.Delay(150);
                Assert.False(shutdown.IsCompleted);

                oldDevice.ReleaseStop();
                await shutdown.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.True(oldReader.IsDisposed);
                Assert.True(currentReader.IsDisposed);
                Assert.Equal(1, oldDevice.StopCalls);
                Assert.Equal(1, oldDevice.DisposeCalls);
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Fact]
        public async Task ShutdownAsync_IsIdempotent_AndRejectsFurtherPlayback()
        {
            var engine = new TestAudioEngine();
            try
            {
                MusicTrack track = Track("idempotent.wav");
                engine.LoadTrack(track);

                Task first = engine.ShutdownAsync();
                Task second = engine.ShutdownAsync();
                Assert.Same(first, second);

                await first.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.True(engine.ReaderSnapshot[0].IsDisposed);
                Assert.Same(first, engine.ShutdownAsync());

                int readers = engine.ReaderSnapshot.Length;
                int devices = engine.DeviceSnapshot.Length;
                engine.LoadTrack(track);
                engine.LoadAndPlayAsync(track);
                engine.Play();
                await Task.Delay(100);

                Assert.Equal(readers, engine.ReaderSnapshot.Length);
                Assert.Equal(devices, engine.DeviceSnapshot.Length);
                engine.Dispose();
                engine.Dispose();
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Fact]
        public async Task Dispose_WhileWorkerIsLoading_ReturnsBounded_ThenShutdownOwnsReader()
        {
            var engine = new BlockingDeviceEngine
            {
                ShutdownSyncWaitTimeout = TimeSpan.FromMilliseconds(100)
            };
            try
            {
                engine.LoadAndPlayAsync(Track("blocked.wav"));
                Assert.True(engine.DeviceEntered.Wait(TimeSpan.FromSeconds(5)),
                    "Playback worker did not reach device creation.");

                TrackingAudioFileReader reader = engine.ReaderSnapshot[0];
                Assert.False(reader.IsDisposed);

                var sw = Stopwatch.StartNew();
                engine.Dispose();
                sw.Stop();

                Assert.True(sw.Elapsed < TimeSpan.FromSeconds(3),
                    "Synchronous Dispose must not wait indefinitely for a live worker.");
                Assert.False(reader.IsDisposed);

                Task shutdown = engine.ShutdownAsync();
                Assert.False(shutdown.IsCompleted);

                engine.ReleaseDevice.Set();
                await shutdown.WaitAsync(TimeSpan.FromSeconds(5));

                Assert.True(reader.IsDisposed);
            }
            finally
            {
                engine.ReleaseDevice.Set();
                engine.Dispose();
            }
        }

        [Fact]
        public async Task RetireTimeoutThenFailure_RetainsReaderUntilExplicitCleanup()
        {
            var engine = new TestAudioEngine
            {
                RetireWaitTimeout = TimeSpan.FromMilliseconds(100)
            };
            try
            {
                engine.LoadTrack(Track("timeout-fail-a.wav"));
                FakeWavePlayer oldDevice = engine.DeviceSnapshot[0];
                TrackingAudioFileReader oldReader = engine.ReaderSnapshot[0];
                oldDevice.HangOnStop = true;
                oldDevice.ThrowOnStop = true;

                engine.LoadTrack(Track("timeout-fail-b.wav"));
                Assert.False(oldReader.IsDisposed);

                oldDevice.ReleaseStop();
                await WaitUntilAsync(() => oldDevice.DisposeCalls == 1, TimeSpan.FromSeconds(5));
                await Task.Delay(100);

                // The deferred cleanup observed a failed retirement and must
                // keep ownership instead of freeing the reader.
                Assert.False(oldReader.IsDisposed);

                await engine.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Assert.False(oldReader.IsDisposed);
                oldReader.Dispose();
            }
            finally
            {
                engine.Dispose();
            }
        }

        [Fact]
        public async Task RepeatedTrackSwitching_RetiresEveryDeviceAndDisposesEveryReader()
        {
            var engine = new TestAudioEngine();
            try
            {
                MusicTrack[] tracks = Enumerable.Range(0, 4)
                    .Select(i => Track("repeat-" + i + ".wav"))
                    .ToArray();

                foreach (MusicTrack track in tracks)
                {
                    engine.LoadTrack(track);
                }

                await engine.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

                TrackingAudioFileReader[] readers = engine.ReaderSnapshot;
                FakeWavePlayer[] devices = engine.DeviceSnapshot;
                Assert.Equal(tracks.Length, readers.Length);
                Assert.Equal(tracks.Length, devices.Length);

                for (int i = 0; i < readers.Length; i++)
                {
                    Assert.True(readers[i].IsDisposed, "Reader " + i + " was not disposed.");
                }
                for (int i = 0; i < devices.Length; i++)
                {
                    Assert.Equal(1, devices[i].StopCalls);
                    Assert.Equal(1, devices[i].DisposeCalls);
                }
            }
            finally
            {
                engine.Dispose();
            }
        }

        private MusicTrack Track(string fileName)
        {
            string path = Path.Combine(_tempDir, fileName);
            var format = new WaveFormat(8000, 16, 1);
            using (var writer = new WaveFileWriter(path, format))
            {
                var buffer = new byte[format.AverageBytesPerSecond / 2];
                writer.Write(buffer, 0, buffer.Length);
            }

            return new MusicTrack
            {
                Id = Interlocked.Increment(ref _nextTrackId),
                FilePath = path,
                Title = Path.GetFileNameWithoutExtension(fileName),
                Artist = "Test Artist",
                Album = "Test Album"
            };
        }

        private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (!condition())
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException("Condition was not satisfied in time.");
                }
                await Task.Delay(10);
            }
        }

        private class TestAudioEngine : AudioEngine
        {
            private readonly object _gate = new();
            private readonly List<FakeWavePlayer> _devices = new();
            private readonly List<TrackingAudioFileReader> _readers = new();

            public FakeWavePlayer[] DeviceSnapshot
            {
                get { lock (_gate) return _devices.ToArray(); }
            }

            public TrackingAudioFileReader[] ReaderSnapshot
            {
                get { lock (_gate) return _readers.ToArray(); }
            }

            protected override IWavePlayer CreateOutputDevice()
            {
                var device = new FakeWavePlayer();
                lock (_gate) { _devices.Add(device); }
                return device;
            }

            protected override AudioFileReader CreateAudioFileReader(string filePath)
            {
                var reader = new TrackingAudioFileReader(filePath);
                lock (_gate) { _readers.Add(reader); }
                return reader;
            }
        }

        private sealed class BlockingDeviceEngine : TestAudioEngine
        {
            private int _blocked;

            public ManualResetEventSlim DeviceEntered { get; } = new(false);
            public ManualResetEventSlim ReleaseDevice { get; } = new(false);

            protected override IWavePlayer CreateOutputDevice()
            {
                if (Interlocked.Exchange(ref _blocked, 1) == 0)
                {
                    DeviceEntered.Set();
                    if (!ReleaseDevice.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Test did not release device creation.");
                    }
                }
                return base.CreateOutputDevice();
            }
        }

        private sealed class FakeWavePlayer : IWavePlayer
        {
            private readonly ManualResetEventSlim _stopGate = new(false);

            public event EventHandler<StoppedEventArgs>? PlaybackStopped
            {
                add { }
                remove { }
            }

            public PlaybackState PlaybackState { get; private set; } = PlaybackState.Stopped;
            public float Volume { get; set; } = 1f;
            public WaveFormat? OutputWaveFormat => null;

            public bool ThrowOnStop { get; set; }
            public bool ThrowOnDispose { get; set; }
            public bool HangOnStop { get; set; }
            public int StopCalls { get; private set; }
            public int DisposeCalls { get; private set; }
            public int PlayCalls { get; private set; }
            public int InitCalls { get; private set; }

            public void Init(IWaveProvider waveProvider)
            {
                InitCalls++;
            }

            public void Play()
            {
                PlayCalls++;
                PlaybackState = PlaybackState.Playing;
            }

            public void Pause()
            {
                PlaybackState = PlaybackState.Paused;
            }

            public void Stop()
            {
                StopCalls++;
                if (HangOnStop)
                {
                    _stopGate.Wait(TimeSpan.FromSeconds(10));
                }
                PlaybackState = PlaybackState.Stopped;
                if (ThrowOnStop)
                {
                    throw new InvalidOperationException("simulated Stop failure");
                }
            }

            public void Dispose()
            {
                DisposeCalls++;
                _stopGate.Set();
                if (ThrowOnDispose)
                {
                    throw new InvalidOperationException("simulated Dispose failure");
                }
            }

            public void ReleaseStop()
            {
                _stopGate.Set();
            }
        }

        private sealed class TrackingAudioFileReader : AudioFileReader
        {
            public TrackingAudioFileReader(string fileName) : base(fileName)
            {
            }

            public bool IsDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                base.Dispose(disposing);
            }
        }
    }
}
