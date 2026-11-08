using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using MusicPlayer.Models;

namespace MusicPlayer.Services
{
    /// <summary>
    /// Represents an actionable artwork loading/decoding request managed by ArtworkPipeline.
    /// Supports priority boosting, deduplication, and cancellation.
    /// </summary>
    public sealed class ArtworkRequest
    {
        public string Key { get; }
        public MusicTrack? Track { get; }
        public ArtworkQuality Quality { get; }
        public ArtworkPriority Priority { get; set; }
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<ImageSource?> TaskCompletionSource { get; }
        public long CreatedTimestamp { get; }

        public ArtworkRequest(
            string key,
            ArtworkQuality quality,
            ArtworkPriority priority,
            MusicTrack? track = null,
            CancellationToken cancellationToken = default)
        {
            Key = key ?? string.Empty;
            Quality = quality;
            Priority = priority;
            Track = track;
            CancellationToken = cancellationToken;
            TaskCompletionSource = new TaskCompletionSource<ImageSource?>(TaskCreationOptions.RunContinuationsAsynchronously);
            CreatedTimestamp = Stopwatch.GetTimestamp();
        }

        public void BoostPriority(ArtworkPriority newPriority)
        {
            if (newPriority < Priority)
            {
                Priority = newPriority;
            }
        }
    }
}

