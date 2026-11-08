using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using MusicPlayer.Models;

namespace MusicPlayer.Services
{
    /// <summary>
    /// Represents an actionable artwork loading/decoding request managed by ArtworkPipeline.
    /// Supports multi-consumer registration with isolated cancellation, priority boosting, and deduplication.
    /// A shared request is only cancelled when ALL consumers have cancelled.
    /// </summary>
    public sealed class ArtworkRequest
    {
        public string Key { get; }
        public MusicTrack? Track { get; }
        public ArtworkQuality Quality { get; }
        public ArtworkPriority Priority { get; set; }
        public long CreatedTimestamp { get; }

        private readonly object _lock = new();
        private readonly List<ConsumerSubscription> _consumers = new();
        private bool _isCompleted;
        private ImageSource? _result;
        private Exception? _exception;

        private sealed class ConsumerSubscription
        {
            public TaskCompletionSource<ImageSource?> Tcs { get; }
            public CancellationTokenRegistration? Registration { get; set; }
            public bool IsCancelled { get; set; }

            public ConsumerSubscription(TaskCompletionSource<ImageSource?> tcs)
            {
                Tcs = tcs;
            }
        }

        public ArtworkRequest(
            string key,
            ArtworkQuality quality,
            ArtworkPriority priority,
            MusicTrack? track = null)
        {
            Key = key ?? string.Empty;
            Quality = quality;
            Priority = priority;
            Track = track;
            CreatedTimestamp = Stopwatch.GetTimestamp();
        }

        /// <summary>
        /// Registers a consumer for this request with isolated cancellation.
        /// If this consumer cancels, only its Task will be cancelled.
        /// </summary>
        public Task<ImageSource?> AddConsumer(CancellationToken cancellationToken)
        {
            lock (_lock)
            {
                if (_isCompleted)
                {
                    if (_exception != null)
                        return Task.FromException<ImageSource?>(_exception);
                    return Task.FromResult<ImageSource?>(_result);
                }

                if (cancellationToken.IsCancellationRequested)
                    return Task.FromCanceled<ImageSource?>(cancellationToken);

                var tcs = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
                var sub = new ConsumerSubscription(tcs);

                if (cancellationToken.CanBeCanceled)
                {
                    sub.Registration = cancellationToken.Register(() =>
                    {
                        lock (_lock)
                        {
                            sub.IsCancelled = true;
                            sub.Tcs.TrySetCanceled(cancellationToken);
                        }
                    });
                }

                _consumers.Add(sub);
                return tcs.Task;
            }
        }

        /// <summary>
        /// Checks whether all registered consumers have cancelled.
        /// If true, the underlying decoding task can be aborted safely.
        /// </summary>
        public bool AreAllConsumersCancelled()
        {
            lock (_lock)
            {
                if (_consumers.Count == 0) return false;
                for (int i = 0; i < _consumers.Count; i++)
                {
                    if (!_consumers[i].IsCancelled)
                        return false;
                }
                return true;
            }
        }

        public void BoostPriority(ArtworkPriority newPriority)
        {
            if (newPriority < Priority)
            {
                Priority = newPriority;
            }
        }

        public void TrySetResult(ImageSource? result)
        {
            List<ConsumerSubscription> subsToNotify;
            lock (_lock)
            {
                if (_isCompleted) return;
                _isCompleted = true;
                _result = result;
                subsToNotify = new List<ConsumerSubscription>(_consumers);
            }

            foreach (var sub in subsToNotify)
            {
                sub.Registration?.Dispose();
                if (!sub.IsCancelled)
                {
                    sub.Tcs.TrySetResult(result);
                }
            }
        }

        public void TrySetException(Exception ex)
        {
            List<ConsumerSubscription> subsToNotify;
            lock (_lock)
            {
                if (_isCompleted) return;
                _isCompleted = true;
                _exception = ex;
                subsToNotify = new List<ConsumerSubscription>(_consumers);
            }

            foreach (var sub in subsToNotify)
            {
                sub.Registration?.Dispose();
                if (!sub.IsCancelled)
                {
                    sub.Tcs.TrySetException(ex);
                }
            }
        }

        public void TrySetCanceled(CancellationToken token = default)
        {
            List<ConsumerSubscription> subsToNotify;
            lock (_lock)
            {
                if (_isCompleted) return;
                _isCompleted = true;
                subsToNotify = new List<ConsumerSubscription>(_consumers);
            }

            foreach (var sub in subsToNotify)
            {
                sub.Registration?.Dispose();
                if (!sub.IsCancelled)
                {
                    if (token.CanBeCanceled)
                        sub.Tcs.TrySetCanceled(token);
                    else
                        sub.Tcs.TrySetCanceled();
                }
            }
        }
    }
}
