using System;
using System.Diagnostics;
using System.Threading;
using MusicPlayer.Utilities;

namespace MusicPlayer.Services
{
    /// <summary>
    /// Thread-safe, centralized diagnostics metrics for the unified ArtworkPipeline.
    /// Captures request lifecycle, cache hit ratios, decode breakdown, and memory budgeting.
    /// </summary>
    public static class ArtworkMetrics
    {
        public static long RequestsTotal;
        public static long RequestsDeduplicated;
        public static long RequestsCompleted;
        public static long RequestsFailed;

        public static long MemoryHits;
        public static long MemoryMisses;
        public static long MemoryEvictions;
        public static long MemorySizeBytes;

        public static long DiskHits;
        public static long DiskMisses;
        public static long NegativeHits;

        public static long TagLibParses;
        public static long DecodesTotal;
        public static long DecodesList;
        public static long DecodesMini;
        public static long DecodesCards;
        public static long DecodesFull;
        public static long DecodesOnUIThread;
        public static long TotalDecodeDurationMs;

        public static int ActiveWorkers;
        public static int QueuedRequests;

        public static long StartupViewportDurationMs;
        public static int StartupCoversRequested;
        public static int StartupCoversReady;
        public static int StartupCoversNoArt;

        public static bool IsPerfTestEnabled => Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1";

        public static void RecordDecode(ArtworkQuality quality, double durationMs, bool onUIThread)
        {
            Interlocked.Increment(ref DecodesTotal);
            Interlocked.Add(ref TotalDecodeDurationMs, (long)durationMs);

            if (onUIThread)
            {
                Interlocked.Increment(ref DecodesOnUIThread);
            }

            switch (quality)
            {
                case ArtworkQuality.List:
                    Interlocked.Increment(ref DecodesList);
                    break;
                case ArtworkQuality.Mini:
                    Interlocked.Increment(ref DecodesMini);
                    break;
                case ArtworkQuality.Cards:
                    Interlocked.Increment(ref DecodesCards);
                    break;
                case ArtworkQuality.Full:
                    Interlocked.Increment(ref DecodesFull);
                    break;
            }
        }

        public static string GetSummary()
        {
            long reqs = Interlocked.Read(ref RequestsTotal);
            long dedup = Interlocked.Read(ref RequestsDeduplicated);
            long memHits = Interlocked.Read(ref MemoryHits);
            long memMiss = Interlocked.Read(ref MemoryMisses);
            long diskHits = Interlocked.Read(ref DiskHits);
            long diskMiss = Interlocked.Read(ref DiskMisses);
            long negHits = Interlocked.Read(ref NegativeHits);
            long taglib = Interlocked.Read(ref TagLibParses);
            long decodes = Interlocked.Read(ref DecodesTotal);
            long uiDecodes = Interlocked.Read(ref DecodesOnUIThread);
            long evictions = Interlocked.Read(ref MemoryEvictions);
            long bytes = Interlocked.Read(ref MemorySizeBytes);

            double memHitRate = (memHits + memMiss) > 0 ? (double)memHits / (memHits + memMiss) * 100.0 : 0;
            double diskHitRate = (diskHits + diskMiss) > 0 ? (double)diskHits / (diskHits + diskMiss) * 100.0 : 0;
            double ramMB = bytes / (1024.0 * 1024.0);

            return $"ArtworkPipeline: Requests={reqs} (Dedup={dedup}), " +
                   $"MemoryHits={memHits} ({memHitRate:F1}%), MemoryMisses={memMiss}, " +
                   $"DiskHits={diskHits} ({diskHitRate:F1}%), DiskMisses={diskMiss}, NegHits={negHits}, " +
                   $"TagLib={taglib}, Decodes={decodes} (List={Interlocked.Read(ref DecodesList)}, Mini={Interlocked.Read(ref DecodesMini)}, " +
                   $"Cards={Interlocked.Read(ref DecodesCards)}, Full={Interlocked.Read(ref DecodesFull)}), " +
                   $"UIDecodes={uiDecodes}, RAM={ramMB:F2}MB, Evictions={evictions}, " +
                   $"StartupViewportReady={StartupCoversReady}/{StartupCoversRequested} ({StartupViewportDurationMs}ms)";
        }

        public static void LogSummaryIfEnabled()
        {
            if (IsPerfTestEnabled)
            {
                LogHelper.LogInfo(GetSummary());
            }
        }
    }
}

