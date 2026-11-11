using System;
using System.Collections.Concurrent;
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

public sealed class ImageLoadScheduler : IDisposable
{
    private readonly SemaphoreSlim _diskIoGate = new(4, 4);
    private readonly SemaphoreSlim _extractionGate = new(2, 2);
    private readonly SemaphoreSlim _decodeGate = new(4, 4);
    private readonly SemaphoreSlim _networkGate = new(4, 4);

    private readonly ILogger? _logger;
    private readonly CancellationTokenSource _cts = new();
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

        // Lower priority operations yield to allow high-priority operations to acquire
        if (priority >= ImagePriority.Preload)
        {
            await Task.Yield();
        }

        bool gateAcquired = false;
        try
        {
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
        _cts.Dispose();
    }
}

