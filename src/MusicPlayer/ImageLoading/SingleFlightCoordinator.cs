using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace MusicPlayer.ImageLoading;

public sealed class SingleFlightCoordinator<TResult>
{
    private sealed class Flight
    {
        private readonly object _gate = new();
        private readonly CancellationTokenSource _internalCts = new();
        private int _waiterCount;
        private System.Threading.Tasks.Task<TResult?>? _task;
        private readonly Func<CancellationToken, System.Threading.Tasks.Task<TResult?>> _factory;

        public Flight(Func<CancellationToken, System.Threading.Tasks.Task<TResult?>> factory)
        {
            _waiterCount = 1;
            _factory = factory;
            // Task is NOT started here - it will be started after successful insertion into dictionary
        }

        /// <summary>
        /// The single task that represents this flight.
        ///
        /// The task must be created exactly once. The previous implementation
        /// used <c>_task ??= Task.Run(...)</c>, and <c>??=</c> is not atomic:
        /// two threads that reach the property at the same time can both see
        /// <c>_task == null</c>, both queue the factory and both run it. That
        /// silently breaks the single-flight guarantee and made the *same*
        /// rendition of the *same* artwork decode twice (once per racing
        /// thread). The lock below guarantees one factory execution per key.
        /// </summary>
        public System.Threading.Tasks.Task<TResult?> Task
        {
            get
            {
                var existing = Volatile.Read(ref _task);
                if (existing != null) return existing;

                lock (_gate)
                {
                    if (_task != null) return _task;
                    ImageDecodeMetrics.RecordSingleFlightExecution();
                    _task = System.Threading.Tasks.Task.Run(() => _factory(_internalCts.Token));
                    return _task;
                }
            }
        }

        public void AddWaiter()
        {
            lock (_gate)
            {
                _waiterCount++;
            }
        }

        public void RemoveWaiter()
        {
            lock (_gate)
            {
                _waiterCount--;
                if (_waiterCount <= 0 && _task != null && !_task.IsCompleted)
                {
                    try { _internalCts.Cancel(); } catch { }
                }
            }
        }
    }

    private readonly ConcurrentDictionary<string, Flight> _flights = new(StringComparer.OrdinalIgnoreCase);

    public async Task<TResult?> RunOrJoinAsync(
        string key,
        Func<CancellationToken, Task<TResult?>> factory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(factory);

        Flight flight;

        while (true)
        {
            if (_flights.TryGetValue(key, out var existing))
            {
                flight = existing;
                flight.AddWaiter();
                ImageDecodeMetrics.RecordSingleFlightJoin();
                break;
            }

            var newFlight = new Flight(factory);
            if (_flights.TryAdd(key, newFlight))
            {
                flight = newFlight;
                break;
            }
        }

        try
        {
            // If the caller's cancellationToken fires, unregister waiter but don't cancel other callers
            using var reg = cancellationToken.Register(() => flight.RemoveWaiter());
            return await flight.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return default;
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                flight.RemoveWaiter();
            }

            // Clean up flight once completed so future requests can retry or re-fetch
            if (flight.Task.IsCompleted)
            {
                _flights.TryRemove(new KeyValuePair<string, Flight>(key, flight));
            }
            else
            {
                _ = flight.Task.ContinueWith(
                    _ => _flights.TryRemove(new KeyValuePair<string, Flight>(key, flight)),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
    }
}
