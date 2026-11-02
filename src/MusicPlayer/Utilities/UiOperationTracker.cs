using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Utilities;

/// <summary>
/// Tracks UI-thread operations that are started fire-and-forget (for example
/// <c>async void</c> loaders). Closing the door to new operations and taking the
/// active snapshot happen under one lock, so shutdown cannot miss a loader that
/// was accepted just before it began.
/// </summary>
public sealed class UiOperationTracker
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Dictionary<long, string> _active = new();
    private readonly ILogger? _logger;

    private long _nextId;
    private bool _stopping;
    private TaskCompletionSource? _idle;

    public UiOperationTracker(ILogger? logger = null)
    {
        _logger = logger;
    }

    public bool IsStopping
    {
        get { lock (_gate) return _stopping; }
    }

    public int ActiveCount
    {
        get { lock (_gate) return _active.Count; }
    }

    public CancellationToken Token
    {
        get { lock (_gate) return _cts.Token; }
    }

    /// <summary>
    /// Registers one UI operation. The returned scope must be disposed when the
    /// operation completes. Returns null when shutdown has already begun; the
    /// caller must then abandon the operation.
    /// </summary>
    public IDisposable? TryBegin(string name)
    {
        lock (_gate)
        {
            if (_stopping)
            {
                _logger?.LogInformation("UI operation '{Name}' rejected: shutdown in progress", name);
                return null;
            }

            long id = ++_nextId;
            _active.Add(id, name);
            return new Scope(this, id, name);
        }
    }

    /// <summary>
    /// Stops accepting operations, cancels the lifetime token, and waits for
    /// every active scope to finish. Returns false when the deadline elapsed.
    /// Concurrent callers share the same idle signal.
    /// </summary>
    public async Task<bool> StopAsync(TimeSpan timeout)
    {
        Task idleTask;
        lock (_gate)
        {
            _stopping = true;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }

            if (_active.Count == 0) return true;

            _idle ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            idleTask = _idle.Task;
        }

        if (timeout <= TimeSpan.Zero)
        {
            LogActive("deadline already elapsed");
            return false;
        }

        Task delay = Task.Delay(timeout);
        Task finished = await Task.WhenAny(idleTask, delay).ConfigureAwait(false);
        if (finished == idleTask) return true;

        LogActive("deadline elapsed");
        return false;
    }

    private void Complete(long id)
    {
        lock (_gate)
        {
            if (!_active.Remove(id)) return;
            if (_active.Count == 0) _idle?.TrySetResult();
        }
    }

    private void LogActive(string reason)
    {
        string[] names;
        lock (_gate) { names = _active.Values.Distinct(StringComparer.Ordinal).ToArray(); }
        _logger?.LogError(
            "UI operations still active after {Reason}: {Names}",
            reason,
            string.Join(", ", names));
    }

    private sealed class Scope : IDisposable
    {
        private readonly UiOperationTracker _owner;
        private readonly long _id;
        private int _disposed;

        public Scope(UiOperationTracker owner, long id, string name)
        {
            _owner = owner;
            _id = id;
            Name = name;
        }

        public string Name { get; }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _owner.Complete(_id);
        }
    }
}
