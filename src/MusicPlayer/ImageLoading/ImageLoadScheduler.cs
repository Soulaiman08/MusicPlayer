using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace MusicPlayer.ImageLoading;

public enum OperationType
{
    DiskIo,
    TagLibExtraction,
    ImageDecode,
    NetworkFetch
}

/// <summary>
/// Schedules image work with a real priority split.
///
/// Phase 1 note: the scheduler only had "yield once for low priority" plus one
/// semaphore per operation type, so a library-wide warmup (Preload/Background)
/// could occupy every disk/decode slot while the user was scrolling. The
/// Phase 2 rules are:
///
///   * user-visible work (Visible / Playback / NearScroll / OpenPage) is counted
///     as "pending" and low-priority work waits until the pending count is zero;
///   * low-priority work runs at most two operations at a time, so slots stay
///     available for the UI at all times.
/// </summary>
public sealed class ImageLoadScheduler : IDisposable
{
    private readonly SemaphoreSlim _diskIoGate = new(4, 4);
    private readonly SemaphoreSlim _extractionGate = new(2, 2);
    private readonly SemaphoreSlim _decodeGate = new(4, 4);
    private readonly SemaphoreSlim _networkGate = new(4, 4);

    // Limits how much warmup/preload work may run at the same time.
    private readonly SemaphoreSlim _lowPriorityGate = new(2, 2);

    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();
    private int _highPriorityPending;
    private bool _disposed;

    public ImageLoadScheduler(ILogger? logger = null)
    {
        _logger = logger;
    }

    private SemaphoreSlim GetGate(OperationType type) => type switch
    {
        OperationType.DiskIo => _diskIoGate,
        OperationType.TagLibExtraction => _extractionGate,
        OperationType.ImageDecode => _decodeGate,
        OperationType.NetworkFetch => _networkGate,
        _ => _decodeGate
    };

    internal static bool IsLowPriority(ImagePriority priority) => priority >= ImagePriority.Preload;

    internal int HighPriorityPending => Volatile.Read(ref _highPriorityPending);

    public async Task<T> ScheduleAsync<T>(
        OperationType operationType,
        ImagePriority priority,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);
        var token = linkedCts.Token;

        var gate = GetGate(operationType);
        bool lowPriority = IsLowPriority(priority);

        if (!lowPriority)
            Interlocked.Increment(ref _highPriorityPending);

        bool gateAcquired = false;
        bool lowPriorityGateAcquired = false;
        try
        {
            if (lowPriority)
            {
                // Never let background work start while the UI has work queued.
                await WaitForNoHighPriorityAsync(token).ConfigureAwait(false);
                await _lowPriorityGate.WaitAsync(token).ConfigureAwait(false);
                lowPriorityGateAcquired = true;
                token.ThrowIfCancellationRequested();

                // A high-priority request may have arrived while we waited for
                // the low-priority slot: yield again instead of starting.
                await WaitForNoHighPriorityAsync(token).ConfigureAwait(false);
            }

            await gate.WaitAsync(token).ConfigureAwait(false);
            gateAcquired = true;
            token.ThrowIfCancellationRequested();

            return await work(token).ConfigureAwait(false);
        }
        finally
        {
            if (gateAcquired)
            {
                gate.Release();
            }
            if (lowPriorityGateAcquired)
            {
                _lowPriorityGate.Release();
            }
            if (!lowPriority)
            {
                Interlocked.Decrement(ref _highPriorityPending);
            }
        }
    }

    private async Task WaitForNoHighPriorityAsync(CancellationToken token)
    {
        while (Volatile.Read(ref _highPriorityPending) > 0)
        {
            await Task.Delay(15, token).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { _cts.Cancel(); } catch { }

        _diskIoGate.Dispose();
        _extractionGate.Dispose();
        _decodeGate.Dispose();
        _networkGate.Dispose();
        _lowPriorityGate.Dispose();
        _cts.Dispose();
    }
}
