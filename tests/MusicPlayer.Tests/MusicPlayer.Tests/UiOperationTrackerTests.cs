using System;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Utilities;
using Xunit;

namespace MusicPlayer.Tests
{
    public class UiOperationTrackerTests
    {
        [Fact]
        public Task TryBegin_ReturnsScope_WhileRunning()
        {
            var tracker = new UiOperationTracker();
            using var scope = tracker.TryBegin("load");
            Assert.NotNull(scope);
            Assert.Equal(1, tracker.ActiveCount);
            Assert.False(tracker.IsStopping);
            return Task.CompletedTask;
        }

        [Fact]
        public async Task TryBegin_ReturnsNull_AfterStopStarts()
        {
            var tracker = new UiOperationTracker();
            await tracker.StopAsync(TimeSpan.Zero);
            Assert.Null(tracker.TryBegin("late"));
            Assert.True(tracker.IsStopping);
        }

        [Fact]
        public async Task StopAsync_WaitsForActiveScope_ThenReturnsTrue()
        {
            var tracker = new UiOperationTracker();
            var scope = tracker.TryBegin("load");
            Assert.NotNull(scope);

            Task<bool> stop = tracker.StopAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(50);
            Assert.False(stop.IsCompleted);

            scope.Dispose();
            Assert.True(await stop.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, tracker.ActiveCount);
        }

        [Fact]
        public async Task StopAsync_Timeout_ReturnsFalseAndKeepsActiveScope()
        {
            var tracker = new UiOperationTracker();
            using var scope = tracker.TryBegin("load");
            Assert.NotNull(scope);

            bool result = await tracker.StopAsync(TimeSpan.FromMilliseconds(100));
            Assert.False(result);
            Assert.Equal(1, tracker.ActiveCount);
        }

        [Fact]
        public async Task StopAsync_CancelsLifetimeToken()
        {
            var tracker = new UiOperationTracker();
            var scope = tracker.TryBegin("load");
            Assert.NotNull(scope);

            Task<bool> stop = tracker.StopAsync(TimeSpan.FromSeconds(5));
            Assert.True(tracker.Token.IsCancellationRequested);

            scope.Dispose();
            Assert.True(await stop.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task StopAsync_ConcurrentCalls_ShareCompletion()
        {
            var tracker = new UiOperationTracker();
            var scope = tracker.TryBegin("load");
            Assert.NotNull(scope);

            Task<bool> stop1 = tracker.StopAsync(TimeSpan.FromSeconds(5));
            Task<bool> stop2 = tracker.StopAsync(TimeSpan.FromSeconds(5));

            scope.Dispose();
            Assert.True(await stop1.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(await stop2.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        [Fact]
        public async Task StopAsync_WithNoActiveWork_ReturnsTrueImmediately()
        {
            var tracker = new UiOperationTracker();
            bool result = await tracker.StopAsync(TimeSpan.FromMilliseconds(100));
            Assert.True(result);
        }
    }
}
