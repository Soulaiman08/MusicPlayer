using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.Utilities;

/// <summary>
/// Owns the lifetime token and task list for fire-and-forget work that uses
/// application-scoped (DI) services. Starting a task and closing the door to
/// new tasks happen under the same lock, so shutdown cannot miss a task that
/// was accepted just before it began.
/// </summary>
public sealed class BackgroundTaskRegistry
{
    private readonly object _gate = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly List<Task> _tasks = new();
    private readonly Dictionary<Task, string> _taskNames = new();
    private readonly ILogger? _logger;

    private bool _stopping;
    private Task? _stopTask;

    public void BeginShutdown()
    {
        lock (_gate)
        {
            if (_stopping) return;
            _stopping = true;
            try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    public IReadOnlyList<string> GetPendingTaskNames()
    {
        lock (_gate)
        {
            var result = new List<string>();
            foreach (var pair in _taskNames)
            {
                if (!pair.Key.IsCompleted)
                {
                    result.Add(pair.Value);
                }
            }
            return result.Distinct(StringComparer.Ordinal).ToList();
        }
    }

    public BackgroundTaskRegistry(ILogger? logger = null)
    {
        _logger = logger;
    }

    public bool IsStopping
    {
        get { lock (_gate) return _stopping; }
    }

    public int TrackedCount
    {
        get { lock (_gate) return _tasks.Count; }
    }

    /// <summary>
    /// Registers and starts a task. Returns false when shutdown has already
    /// begun; in that case the operation is never invoked.
    /// </summary>
    public bool TryStart(string name, Func<CancellationToken, Task> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);

        lock (_gate)
        {
            if (_stopping)
            {
                _logger?.LogInformation("Background task '{Name}' rejected: shutdown in progress", name);
                return false;
            }

            var token = _cts.Token;
            var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            Task task = Task.Run(async () =>
            {
                // The task is added to _tasks before this gate opens, so StopAsync
                // can never observe a running task that it did not capture.
                await startGate.Task.ConfigureAwait(false);

                try
                {
                    await operation(token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    // Normal shutdown/cancellation.
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Background task '{Name}' failed", name);
                }
            }, CancellationToken.None);

            _tasks.RemoveAll(x => x.IsCompleted);
            foreach (var completed in _tasks.Where(t => t.IsCompleted).ToList())
            {
                _taskNames.Remove(completed);
            }
            _tasks.Add(task);
            _taskNames[task] = name;
            startGate.SetResult();
            return true;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { }
        }
    }

    /// <summary>
    /// Atomically rejects new tasks, cancels the lifetime token, and awaits
    /// every accepted task. Concurrent callers receive the same stop task.
    /// </summary>
    public Task StopAsync()
    {
        lock (_gate)
        {
            if (_stopTask != null) return _stopTask;

            _stopping = true;
            try { _cts.Cancel(); }
            catch (ObjectDisposedException) { }

            var snapshot = _tasks.ToArray();
            _stopTask = StopCoreAsync(snapshot);
            return _stopTask;
        }
    }

    /// <summary>
    /// Bounded stop used by application shutdown. Returns false when accepted
    /// work did not finish before the deadline; the caller must treat that as a
    /// shutdown failure rather than as proof that the work stopped.
    /// </summary>
    public async Task<bool> StopAsync(TimeSpan timeout)
    {
        Task stop = StopAsync();
        if (stop.IsCompleted)
        {
            try { await stop.ConfigureAwait(false); }
            catch { }
            return true;
        }

        if (timeout <= TimeSpan.Zero) return false;

        Task delay = Task.Delay(timeout);
        return await Task.WhenAny(stop, delay).ConfigureAwait(false) == stop;
    }

    private async Task StopCoreAsync(Task[] snapshot)
    {
        try
        {
            await Task.WhenAll(snapshot).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown/cancellation.
        }
        catch (Exception ex)
        {
            // Individual task failures are already logged with their source;
            // this is a last-resort guard so shutdown still completes.
            _logger?.LogError(ex, "Background task shutdown failed");
        }
        finally
        {
            lock (_gate) { _tasks.Clear(); _taskNames.Clear(); }
            try { _cts.Dispose(); } catch (ObjectDisposedException) { }
        }
    }
}
