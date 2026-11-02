using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MusicPlayer.Utilities;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Tests
{
    public class BackgroundTaskRegistryTests
    {
        [Fact]
        public async Task TryStart_AllTasksTracked()
        {
            var registry = new BackgroundTaskRegistry();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int started = 0;

            for (int i = 0; i < 3; i++)
            {
                Assert.True(registry.TryStart("task" + i, async ct =>
                {
                    Interlocked.Increment(ref started);
                    await gate.Task.WaitAsync(ct);
                }));
            }

            Assert.Equal(3, registry.TrackedCount);
            for (int i = 0; i < 100 && Volatile.Read(ref started) < 3; i++)
            {
                await Task.Delay(10);
            }
            Assert.Equal(3, Volatile.Read(ref started));

            var stop = registry.StopAsync();
            gate.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task StopAsync_WaitsForAllTasks()
        {
            var registry = new BackgroundTaskRegistry();
            var gate1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var gate2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started1 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var started2 = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            int finished = 0;

            registry.TryStart("one", async _ =>
            {
                started1.TrySetResult();
                await gate1.Task;
                Interlocked.Increment(ref finished);
            });
            registry.TryStart("two", async _ =>
            {
                started2.TrySetResult();
                await gate2.Task;
                Interlocked.Increment(ref finished);
            });

            await Task.WhenAll(started1.Task, started2.Task).WaitAsync(TimeSpan.FromSeconds(5));
            var stop = registry.StopAsync();

            await Task.Delay(100);
            Assert.False(stop.IsCompleted);
            Assert.Equal(0, Volatile.Read(ref finished));

            gate1.TrySetResult();
            await Task.Delay(100);
            Assert.False(stop.IsCompleted);

            gate2.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(2, Volatile.Read(ref finished));
            Assert.Equal(0, registry.TrackedCount);
        }

        [Fact]
        public async Task StopAsync_RejectsNewTasks()
        {
            var registry = new BackgroundTaskRegistry();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            registry.TryStart("blocker", async ct => await gate.Task.WaitAsync(ct));

            var stop = registry.StopAsync();
            bool started = false;
            bool accepted = registry.TryStart("late", _ =>
            {
                started = true;
                return Task.CompletedTask;
            });

            Assert.False(accepted);
            Assert.False(started);
            Assert.True(registry.IsStopping);

            gate.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));
        }

        [Fact]
        public async Task StopAsync_ConcurrentCalls_ShareOneStopAndDisposeOnce()
        {
            var registry = new BackgroundTaskRegistry();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            registry.TryStart("blocker", async ct => await gate.Task.WaitAsync(ct));

            var stop1 = registry.StopAsync();
            var stop2 = registry.StopAsync();
            Assert.Same(stop1, stop2);

            gate.TrySetResult();
            await stop1.WaitAsync(TimeSpan.FromSeconds(5));

            var stop3 = registry.StopAsync();
            Assert.Same(stop1, stop3);
            Assert.Equal(0, registry.TrackedCount);
        }

        [Fact]
        public async Task FaultedTask_IsLogged_AndOtherTasksStillAwaited()
        {
            var logger = new RecordingLogger();
            var registry = new BackgroundTaskRegistry(logger);
            var faultStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var otherGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool otherFinished = false;

            registry.TryStart("faulted", async _ =>
            {
                faultStarted.TrySetResult();
                await Task.Yield();
                throw new InvalidOperationException("boom");
            });
            registry.TryStart("other", async _ =>
            {
                await otherGate.Task;
                otherFinished = true;
            });

            await faultStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var stop = registry.StopAsync();
            await Task.Delay(100);
            Assert.False(stop.IsCompleted);

            otherGate.TrySetResult();
            await stop.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(otherFinished);
            Assert.Contains(logger.Messages, m => m.Contains("faulted", StringComparison.OrdinalIgnoreCase) &&
                                                  m.Contains("boom", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public async Task StopAsync_Timeout_ReturnsFalseForHungTask()
        {
            var registry = new BackgroundTaskRegistry();
            var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Assert.True(registry.TryStart("hung", async _ => await gate.Task));

            Task<bool> stop = registry.StopAsync(TimeSpan.FromMilliseconds(100));
            Assert.False(await stop.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(registry.IsStopping);

            gate.TrySetResult();
            await registry.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, registry.TrackedCount);
        }

        [Fact]
        public async Task StopAsync_Timeout_ReturnsTrueWhenTaskCompletesInTime()
        {
            var registry = new BackgroundTaskRegistry();
            registry.TryStart("fast", _ => Task.CompletedTask);
            bool result = await registry.StopAsync(TimeSpan.FromSeconds(5));
            Assert.True(result);
        }

        [Fact]
        public async Task LifetimeCancellation_ReachesWorkAndWatchdog()
        {
            using var lifetime = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool workObservedCancellation = false;

            Task<bool> run = MainViewModel.RunCancellableWorkWithWatchdogAsync(
                async ct =>
                {
                    started.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        workObservedCancellation = true;
                        throw;
                    }
                },
                () => false,
                lifetime.Token,
                pollMs: 25);

            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            lifetime.Cancel();
            bool retry = await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(workObservedCancellation);
            Assert.False(retry);
        }

        [Fact]
        public async Task WatchdogCancellation_ReachesWork()
        {
            using var lifetime = new CancellationTokenSource();
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            bool interacting = false;
            bool workObservedCancellation = false;

            Task<bool> run = MainViewModel.RunCancellableWorkWithWatchdogAsync(
                async ct =>
                {
                    started.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                    }
                    catch (OperationCanceledException)
                    {
                        workObservedCancellation = true;
                        throw;
                    }
                },
                () => interacting,
                lifetime.Token,
                pollMs: 25);

            await started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            interacting = true;
            bool retry = await run.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.True(workObservedCancellation);
            Assert.True(retry);
        }

        private sealed class RecordingLogger : ILogger
        {
            public List<string> Messages { get; } = new();

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (Messages)
                {
                    Messages.Add(formatter(state, exception) + (exception == null ? "" : " " + exception.Message));
                }
            }
        }
    }
}
