using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MusicPlayer.Utilities;
using MusicPlayer.Views;

namespace MusicPlayer.Services;

public enum WorkPriority
{
    ImmediateVisible = 0, // Currently on screen or required for active hero transition
    Viewport = 1,         // Items in active tab's viewport
    CurrentTab = 2,       // Items in active tab outside viewport
    BackgroundLibrary = 3,// Items in inactive library tabs
    Maintenance = 4       // Upgrades, face re-crop, wallpaper warmup
}

public sealed class BackgroundWorkCoordinator : IDisposable
{
    private static readonly Lazy<BackgroundWorkCoordinator> _instance =
        new(() => new BackgroundWorkCoordinator());

    public static BackgroundWorkCoordinator Instance => _instance.Value;

    // Hard ceiling for concurrent CPU-intensive image work (TagLib parse, JPEG/PNG decode, face crop)
    private readonly int _maxCpuConcurrency;
    private readonly SemaphoreSlim _cpuGate;
    private readonly SemaphoreSlim _networkGate = new(4, 4);

    // Prioritized work queues (0 = ImmediateVisible ... 4 = Maintenance)
    private readonly object _queueLock = new();
    private readonly LinkedList<PrioritizedWorkItem>[] _queues;
    private readonly ConcurrentDictionary<string, PrioritizedWorkItem> _pendingByKey = new(StringComparer.OrdinalIgnoreCase);

    private readonly CancellationTokenSource _cts = new();
    private int _activeWorkers;
    private int _runningCpuTasks;
    private long _completedCount;
    private long _failedCount;
    private bool _disposed;

    private sealed class PrioritizedWorkItem
    {
        public string Key { get; }
        public WorkPriority Priority { get; set; }
        public Func<CancellationToken, Task> Work { get; }
        public TaskCompletionSource<bool> Tcs { get; }
        public CancellationToken Token { get; }

        public PrioritizedWorkItem(string key, WorkPriority priority, Func<CancellationToken, Task> work, CancellationToken token)
        {
            Key = key;
            Priority = priority;
            Work = work;
            Tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Token = token;
        }
    }

    public BackgroundWorkCoordinator()
    {
        // On 4 cores: 2. On 8+ cores: 3. Never saturate all CPU cores so WPF UI + Render stay 100% fluid.
        _maxCpuConcurrency = Math.Max(2, Math.Min(Environment.ProcessorCount / 2, 3));
        _cpuGate = new SemaphoreSlim(_maxCpuConcurrency, _maxCpuConcurrency);

        _queues = new LinkedList<PrioritizedWorkItem>[5];
        for (int i = 0; i < _queues.Length; i++)
            _queues[i] = new LinkedList<PrioritizedWorkItem>();
    }

    public int MaxCpuConcurrency => _maxCpuConcurrency;
    public int RunningCpuTasks => Volatile.Read(ref _runningCpuTasks);
    public long CompletedCount => Interlocked.Read(ref _completedCount);
    public long FailedCount => Interlocked.Read(ref _failedCount);

    /// <summary>
    /// Enqueues a CPU-bound operation (image decode, face crop, audio tag read) with unified priority.
    /// If an item with the same key is already queued, its priority is boosted to the higher priority.
    /// </summary>
    public Task EnqueueCpuWorkAsync(
        string key,
        WorkPriority priority,
        Func<CancellationToken, Task> work,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || _cts.IsCancellationRequested)
            return Task.FromCanceled(_cts.Token);

        PrioritizedWorkItem item;
        lock (_queueLock)
        {
            if (_pendingByKey.TryGetValue(key, out var existing))
            {
                // If already queued at lower priority, boost it
                if (priority < existing.Priority)
                {
                    int oldBucket = (int)existing.Priority;
                    int newBucket = (int)priority;
                    _queues[oldBucket].Remove(existing);
                    existing.Priority = priority;
                    _queues[newBucket].AddFirst(existing);
                }
                return existing.Tcs.Task;
            }

            item = new PrioritizedWorkItem(key, priority, work, cancellationToken);
            _pendingByKey[key] = item;
            int bucket = (int)priority;
            _queues[bucket].AddLast(item);

            EnsureWorkersLocked();
        }

        return item.Tcs.Task;
    }

    /// <summary>
    /// Executes work immediately through the global CPU decode gate, observing concurrency limits.
    /// Used by viewport decodes that cannot be deferred.
    /// </summary>
    public async Task<T> ExecuteCpuWorkAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        await _cpuGate.WaitAsync(linked.Token).ConfigureAwait(false);
        Interlocked.Increment(ref _runningCpuTasks);
        try
        {
            return await work(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _runningCpuTasks);
            _cpuGate.Release();
        }
    }

    /// <summary>
    /// Executes Network I/O through the dedicated network gate, separate from CPU budget.
    /// </summary>
    public async Task<T> ExecuteNetworkWorkAsync<T>(
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        await _networkGate.WaitAsync(linked.Token).ConfigureAwait(false);
        try
        {
            return await work(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            _networkGate.Release();
        }
    }

    private void EnsureWorkersLocked()
    {
        while (_activeWorkers < _maxCpuConcurrency)
        {
            _activeWorkers++;
            _ = Task.Run(WorkerLoopAsync);
        }
    }

    private async Task WorkerLoopAsync()
    {
        while (!_disposed && !_cts.IsCancellationRequested)
        {
            PrioritizedWorkItem? item = null;

            lock (_queueLock)
            {
                // While user is actively scrolling or clicking, ONLY process ImmediateVisible or Viewport items.
                // Low-priority background items yield immediately to keep 60-144 FPS smoothness!
                bool isInteracting = SmoothScrollHelper.IsInteractingNow(400);
                int maxBucket = isInteracting ? (int)WorkPriority.Viewport : (int)WorkPriority.Maintenance;

                for (int b = 0; b <= maxBucket; b++)
                {
                    if (_queues[b].Count > 0)
                    {
                        item = _queues[b].First!.Value;
                        _queues[b].RemoveFirst();
                        _pendingByKey.TryRemove(item.Key, out _);
                        break;
                    }
                }

                if (item == null)
                {
                    _activeWorkers--;
                    return;
                }
            }

            if (item.Token.IsCancellationRequested || _cts.IsCancellationRequested)
            {
                item.Tcs.TrySetCanceled();
                continue;
            }

            // Acquire global CPU decode budget
            await _cpuGate.WaitAsync(_cts.Token).ConfigureAwait(false);
            Interlocked.Increment(ref _runningCpuTasks);
            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, item.Token);
                await item.Work(linked.Token).ConfigureAwait(false);
                item.Tcs.TrySetResult(true);
                Interlocked.Increment(ref _completedCount);
            }
            catch (OperationCanceledException)
            {
                item.Tcs.TrySetCanceled();
            }
            catch (Exception ex)
            {
                Interlocked.Increment(ref _failedCount);
                item.Tcs.TrySetException(ex);
            }
            finally
            {
                Interlocked.Decrement(ref _runningCpuTasks);
                _cpuGate.Release();
            }

            // Short polite yield between background items to let CPU and threadpool breathe
            if (item.Priority >= WorkPriority.BackgroundLibrary)
            {
                await Task.Delay(20, _cts.Token).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Notifies coordinator that the user is actively interacting (e.g. scrolling, navigating).
    /// </summary>
    public void NotifyInteractionStarted()
    {
        // WorkerLoopAsync actively polls SmoothScrollHelper.IsInteractingNow to pause background tasks
    }

    /// <summary>
    /// Boosts all items belonging to a given tab or key prefix to Viewport priority.
    /// </summary>
    public void BoostPriority(string keyPrefix, WorkPriority newPriority)
    {
        lock (_queueLock)
        {
            foreach (var kvp in _pendingByKey)
            {
                if (kvp.Key.StartsWith(keyPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    var item = kvp.Value;
                    if (newPriority < item.Priority)
                    {
                        int oldBucket = (int)item.Priority;
                        int newBucket = (int)newPriority;
                        _queues[oldBucket].Remove(item);
                        item.Priority = newPriority;
                        _queues[newBucket].AddFirst(item);
                    }
                }
            }
            EnsureWorkersLocked();
        }
    }

    public void CancelAll()
    {
        _cts.Cancel();
        lock (_queueLock)
        {
            foreach (var list in _queues)
            {
                foreach (var item in list)
                    item.Tcs.TrySetCanceled();
                list.Clear();
            }
            _pendingByKey.Clear();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CancelAll();
        _cpuGate.Dispose();
        _networkGate.Dispose();
        _cts.Dispose();
    }
}
