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
        private readonly System.Threading.Tasks.Task<TResult?> _task;

        public Flight(Func<CancellationToken, System.Threading.Tasks.Task<TResult?>> factory)
        {
            _waiterCount = 1;
            _task = System.Threading.Tasks.Task.Run(() => factory(_internalCts.Token));
        }

        public System.Threading.Tasks.Task<TResult?> Task => _task;

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
                if (_waiterCount <= 0 && !_task.IsCompleted)
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
