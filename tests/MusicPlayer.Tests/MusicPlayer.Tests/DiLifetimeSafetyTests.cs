using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Utilities;
using Xunit;

namespace MusicPlayer.Tests
{
    public class DiLifetimeSafetyTests
    {
        [Fact]
        public async Task UiTimeout_KeepsServiceProviderAliveUntilOperationFinishes()
        {
            var tracker = new UiOperationTracker();
            var service = new TrackedDisposable();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            IDisposable? scope = tracker.TryBegin("ui-op");
            Assert.NotNull(scope);

            Task operation = Task.Run(async () =>
            {
                started.TrySetResult();
                await release.Task;
                scope!.Dispose();
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task<bool> stop = tracker.StopAsync(TimeSpan.FromMilliseconds(100));
            Assert.False(await stop.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(service.IsDisposed);
            Assert.Equal(1, tracker.ActiveCount);

            release.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));

            Task<bool> stop2 = tracker.StopAsync(TimeSpan.FromSeconds(5));
            Assert.True(await stop2.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, tracker.ActiveCount);

            service.Dispose();
            Assert.True(service.IsDisposed);
        }

        [Fact]
        public async Task BackgroundTimeout_KeepsServiceProviderAliveUntilTaskFinishes()
        {
            var registry = new BackgroundTaskRegistry();
            var service = new TrackedDisposable();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Assert.True(registry.TryStart("bg-op", async _ =>
            {
                started.TrySetResult();
                await release.Task;
            }));
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task<bool> stop = registry.StopAsync(TimeSpan.FromMilliseconds(100));
            Assert.False(await stop.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(service.IsDisposed);
            Assert.True(registry.IsStopping);

            release.TrySetResult();
            await registry.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, registry.TrackedCount);

            service.Dispose();
            Assert.True(service.IsDisposed);
        }

        [Fact]
        public async Task DisposalOrdering_OperationFinishesBeforeServiceDispose()
        {
            var tracker = new UiOperationTracker();
            var events = new List<string>();
            var service = new TrackedDisposable(events);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            IDisposable? scope = tracker.TryBegin("ordered-op");
            Assert.NotNull(scope);

            Task operation = Task.Run(async () =>
            {
                events.Add("Operation Started");
                started.TrySetResult();
                await release.Task;
                events.Add("Operation Finished");
                scope!.Dispose();
            });
            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Task<bool> stop = tracker.StopAsync(TimeSpan.FromSeconds(5));
            Assert.False(stop.IsCompleted);

            release.TrySetResult();
            await operation.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True(await stop.WaitAsync(TimeSpan.FromSeconds(5)));

            service.Dispose();
            Assert.True(events.IndexOf("Operation Finished") >= 0);
            Assert.True(events.IndexOf("Service Dispose") > events.IndexOf("Operation Finished"));
        }

        [Fact]
        public async Task ConcurrentShutdown_IsIdempotent()
        {
            var tracker = new UiOperationTracker();
            IDisposable? scope = tracker.TryBegin("concurrent-op");
            Assert.NotNull(scope);

            Task<bool> stop1 = tracker.StopAsync(TimeSpan.FromSeconds(5));
            Task<bool> stop2 = tracker.StopAsync(TimeSpan.FromSeconds(5));

            scope.Dispose();
            Assert.True(await stop1.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(await stop2.WaitAsync(TimeSpan.FromSeconds(5)));
        }

        private sealed class TrackedDisposable : IDisposable
        {
            private readonly List<string>? _events;

            public TrackedDisposable(List<string>? events = null)
            {
                _events = events;
            }

            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
                _events?.Add("Service Dispose");
            }
        }
    }
}