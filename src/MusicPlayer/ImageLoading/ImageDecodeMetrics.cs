using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace MusicPlayer.ImageLoading;

/// <summary>
/// Counters for the image pipeline.
///
/// The existing PERF-* probes only observe what happens on the UI thread
/// (tab slide, tab data, scroll bursts). The artwork itself is decoded on
/// worker threads, so nothing could tell how often the *same* bitmap was
/// actually decoded, how often the same source bytes were extracted again, or
/// how many requests were served from the memory cache.
///
/// These counters sit on the decode / extraction / cache choke points. They are
/// always on (a couple of interlocked increments and a dictionary update) and
/// they never influence control flow, so a build with them and a build without
/// them behave identically - they exist so the numbers in the M2 reports can be
/// reproduced instead of estimated.
/// </summary>
public static class ImageDecodeMetrics
{
    /// <summary>
    /// Upper bound for the number of distinct source keys that are tracked.
    /// The pipeline can touch thousands of files; the counters must not grow
    /// without limit, so tracking stops once the cap is reached (the totals
    /// keep counting).
    /// </summary>
    private const int MaxTrackedKeys = 8192;

    private static long _decodes;
    private static long _decodeTicks;
    private static long _extractions;
    private static long _memoryHits;
    private static long _memoryMisses;
    private static long _diskHits;
    private static long _diskMisses;
    private static long _singleFlightRequests;
    private static long _singleFlightJoins;
    private static long _singleFlightExecutions;
    private static long _metadataProbes;
    private static long _assetResolves;
    private static long _assetSourceReads;

    private static readonly ConcurrentDictionary<string, long> _phaseTicks = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, long> _phaseCounts = new(StringComparer.Ordinal);

    /// <summary>
    /// When a scope is active the pipeline reports into that scope as well as
    /// into the global counters. The scope flows with the async context, so a
    /// test can measure exactly the decodes caused by its own work even while
    /// other tests run in parallel.
    /// </summary>
    private static readonly AsyncLocal<ImageDecodeScope?> _activeScope = new();

    private static readonly ConcurrentDictionary<string, int> _decodesByRendition =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, long> _decodeTicksByRendition =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> _decodesBySourceRendition =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> _extractionsBySource =
        new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int> _metadataProbesBySource =
        new(StringComparer.Ordinal);

    // ── recording ────────────────────────────────────────────────────────────

    /// <summary>Records one bitmap decode.</summary>
    /// <param name="rendition">Target rendition, e.g. "80x80" or "native".</param>
    /// <param name="sourceKey">Identity of the source the bytes came from.</param>
    public static void RecordDecode(string rendition, string? sourceKey, long elapsedTicks)
    {
        Interlocked.Increment(ref _decodes);
        Interlocked.Add(ref _decodeTicks, elapsedTicks);

        Bump(_decodesByRendition, "rendition:" + rendition);
        _decodeTicksByRendition.AddOrUpdate("rendition:" + rendition, elapsedTicks, (_, v) => v + elapsedTicks);
        if (!string.IsNullOrEmpty(sourceKey))
        {
            Bump(_decodesBySourceRendition, sourceKey + "|" + rendition);
        }

        _activeScope.Value?.RecordDecode(rendition, sourceKey, elapsedTicks);
    }

    /// <summary>Records one source extraction (file read / tag read).</summary>
    public static void RecordExtraction(string? sourceKey)
    {
        Interlocked.Increment(ref _extractions);
        if (!string.IsNullOrEmpty(sourceKey))
        {
            Bump(_extractionsBySource, sourceKey);
        }
        _activeScope.Value?.RecordExtraction(sourceKey);
    }

    /// <summary>Records a metadata probe that decodes the source at native size.</summary>
    public static void RecordMetadataProbe(string? sourceKey, long elapsedTicks)
    {
        Interlocked.Increment(ref _metadataProbes);
        RecordPhase("probeDecode", elapsedTicks);
        if (!string.IsNullOrEmpty(sourceKey))
        {
            Bump(_metadataProbesBySource, sourceKey);
            Bump(_decodesBySourceRendition, sourceKey + "|native");
        }
        _activeScope.Value?.RecordMetadataProbe(sourceKey, elapsedTicks);
    }

    /// <summary>Accumulates the wall time of a named pipeline phase.</summary>
    public static void RecordPhase(string phase, long elapsedTicks)
    {
        _phaseTicks.AddOrUpdate(phase, elapsedTicks, (_, v) => v + elapsedTicks);
        _phaseCounts.AddOrUpdate(phase, 1, (_, v) => v + 1);
    }

    public static void RecordMemoryHit() => Interlocked.Increment(ref _memoryHits);
    public static void RecordMemoryMiss() => Interlocked.Increment(ref _memoryMisses);
    public static void RecordDiskHit() => Interlocked.Increment(ref _diskHits);
    public static void RecordDiskMiss() => Interlocked.Increment(ref _diskMisses);

    public static void RecordSingleFlightRequest() => Interlocked.Increment(ref _singleFlightRequests);
    public static void RecordSingleFlightJoin() => Interlocked.Increment(ref _singleFlightJoins);
    public static void RecordSingleFlightExecution() => Interlocked.Increment(ref _singleFlightExecutions);

    /// <summary>Records one <c>AlbumArtAssetService.Resolve</c> call.</summary>
    /// <param name="readSource">True when the call actually read the source bytes.</param>
    public static void RecordAssetResolve(bool readSource, long elapsedTicks)
    {
        Interlocked.Increment(ref _assetResolves);
        if (readSource) Interlocked.Increment(ref _assetSourceReads);
        RecordPhase("assetResolve", elapsedTicks);
        _activeScope.Value?.RecordAssetResolve(readSource, elapsedTicks);
    }

    /// <summary>
    /// Starts a scope that collects the decodes/extractions caused by the
    /// calling async context. Dispose ends the scope.
    /// </summary>
    public static ImageDecodeScope BeginScope() => new();

    internal static ImageDecodeScope? ActiveScope => _activeScope.Value;

    internal static void SetActiveScope(ImageDecodeScope? scope) => _activeScope.Value = scope;

    private static void Bump(ConcurrentDictionary<string, int> map, string key)
    {
        if (map.Count >= MaxTrackedKeys && !map.ContainsKey(key)) return;
        map.AddOrUpdate(key, 1, static (_, v) => v + 1);
    }

    // ── snapshot ─────────────────────────────────────────────────────────────

    public readonly record struct Snapshot(
        long Decodes,
        long DecodeTicks,
        long Extractions,
        long MemoryHits,
        long MemoryMisses,
        long DiskHits,
        long DiskMisses,
        long SingleFlightRequests,
        long SingleFlightJoins,
        long SingleFlightExecutions,
        long MetadataProbes,
        long AssetResolves,
        long AssetSourceReads,
        int DuplicateDecodePairs,
        long DuplicateDecodeCount,
        int SourcesWithSeveralRenditions,
        int DuplicateExtractionSources,
        long DuplicateExtractionCount);

    public static Snapshot GetSnapshot()
    {
        int dupPairs = 0;
        long dupCount = 0;
        var renditionsPerSource = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in _decodesBySourceRendition)
        {
            int sep = pair.Key.LastIndexOf('|');
            string source = sep >= 0 ? pair.Key[..sep] : pair.Key;
            renditionsPerSource[source] = renditionsPerSource.TryGetValue(source, out int c) ? c + 1 : 1;
            if (pair.Value > 1)
            {
                dupPairs++;
                dupCount += pair.Value - 1;
            }
        }

        int multiRenditionSources = 0;
        foreach (var pair in renditionsPerSource)
        {
            if (pair.Value > 1) multiRenditionSources++;
        }

        int dupExtractionSources = 0;
        long dupExtractionCount = 0;
        foreach (var pair in _extractionsBySource)
        {
            if (pair.Value > 1)
            {
                dupExtractionSources++;
                dupExtractionCount += pair.Value - 1;
            }
        }

        return new Snapshot(
            Interlocked.Read(ref _decodes),
            Interlocked.Read(ref _decodeTicks),
            Interlocked.Read(ref _extractions),
            Interlocked.Read(ref _memoryHits),
            Interlocked.Read(ref _memoryMisses),
            Interlocked.Read(ref _diskHits),
            Interlocked.Read(ref _diskMisses),
            Interlocked.Read(ref _singleFlightRequests),
            Interlocked.Read(ref _singleFlightJoins),
            Interlocked.Read(ref _singleFlightExecutions),
            Interlocked.Read(ref _metadataProbes),
            Interlocked.Read(ref _assetResolves),
            Interlocked.Read(ref _assetSourceReads),
            dupPairs,
            dupCount,
            multiRenditionSources,
            dupExtractionSources,
            dupExtractionCount);
    }

    /// <summary>
    /// Formats the counters as a single log line. The line is intentionally
    /// machine readable so the perf harness can parse it.
    /// </summary>
    public static string FormatSnapshot(string tag = "PERF-IMAGE")
    {
        var s = GetSnapshot();
        double ticksToMs = 1000.0 / Stopwatch.Frequency;

        var sb = new StringBuilder(256);
        sb.Append(tag)
          .Append(" decodes=").Append(s.Decodes)
          .Append(" decodeMs=").Append((s.DecodeTicks * ticksToMs).ToString("0.0", CultureInfo.InvariantCulture))
          .Append(" extract=").Append(s.Extractions)
          .Append(" memHit=").Append(s.MemoryHits)
          .Append(" memMiss=").Append(s.MemoryMisses)
          .Append(" diskHit=").Append(s.DiskHits)
          .Append(" diskMiss=").Append(s.DiskMisses)
          .Append(" sfReq=").Append(s.SingleFlightRequests)
          .Append(" sfJoin=").Append(s.SingleFlightJoins)
          .Append(" sfExec=").Append(s.SingleFlightExecutions)
          .Append(" probeDecode=").Append(s.MetadataProbes)
          .Append(" assetResolve=").Append(s.AssetResolves)
          .Append(" assetRead=").Append(s.AssetSourceReads)
          .Append(" dupPairs=").Append(s.DuplicateDecodePairs)
          .Append(" dupDecodes=").Append(s.DuplicateDecodeCount)
          .Append(" multiRendition=").Append(s.SourcesWithSeveralRenditions)
          .Append(" dupExtract=").Append(s.DuplicateExtractionSources)
          .Append(" dupExtractCount=").Append(s.DuplicateExtractionCount);

        foreach (var phase in _phaseCounts.Keys.OrderBy(static k => k, StringComparer.Ordinal))
        {
            _phaseTicks.TryGetValue(phase, out long ticks);
            sb.Append(' ').Append(phase).Append("Ms=")
              .Append((ticks * ticksToMs).ToString("0.0", CultureInfo.InvariantCulture))
              .Append('/').Append(_phaseCounts[phase]);
        }

        sb.Append(" byRendition={");
        bool first = true;
        foreach (var pair in _decodesByRendition.OrderByDescending(p => p.Value))
        {
            if (!first) sb.Append(',');
            first = false;
            _decodeTicksByRendition.TryGetValue(pair.Key, out long rTicks);
            sb.Append(pair.Key.Substring("rendition:".Length)).Append(':').Append(pair.Value)
              .Append('@').Append((rTicks * ticksToMs).ToString("0.0", CultureInfo.InvariantCulture));
        }
        sb.Append('}');

        return sb.ToString();
    }

    /// <summary>
    /// Returns the (source, rendition) pairs that were decoded more than once,
    /// most frequent first. Used to prove - or disprove - redundant decoding.
    /// </summary>
    public static IReadOnlyList<(string Source, string Rendition, int Count)> GetDuplicateDecodes(int limit = 10)
    {
        var result = new List<(string, string, int)>();
        foreach (var pair in _decodesBySourceRendition)
        {
            if (pair.Value <= 1) continue;
            int sep = pair.Key.LastIndexOf('|');
            string source = sep >= 0 ? pair.Key[..sep] : pair.Key;
            string rendition = sep >= 0 ? pair.Key[(sep + 1)..] : "?";
            result.Add((source, rendition, pair.Value));
        }

        result.Sort(static (a, b) => b.Item3.CompareTo(a.Item3));
        if (result.Count > limit) result.RemoveRange(limit, result.Count - limit);
        return result;
    }

    /// <summary>
    /// Builds a short, stable identity for a source path: the file name plus a
    /// hash of the directory, so log lines stay readable but different files
    /// never collide.
    /// </summary>
    public static string CompactKey(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "?";
        try
        {
            string file = System.IO.Path.GetFileName(path);
            string dir = System.IO.Path.GetDirectoryName(path) ?? string.Empty;
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(dir));
            return file + "#" + Convert.ToHexString(hash, 0, 3);
        }
        catch
        {
            return path;
        }
    }
}

/// <summary>
/// A measurement scope for the image pipeline. Only used by the performance
/// tests; in a normal session no scope is active and the behaviour of the
/// pipeline is unchanged.
/// </summary>
public sealed class ImageDecodeScope : IDisposable
{
    private readonly ConcurrentDictionary<string, int> _decodesByRendition = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _decodesBySourceRendition = new(StringComparer.Ordinal);
    private long _decodes;
    private long _extractions;
    private long _metadataProbes;
    private long _assetResolves;
    private long _assetSourceReads;

    internal ImageDecodeScope()
    {
        ImageDecodeMetrics.SetActiveScope(this);
    }

    public long Decodes => Interlocked.Read(ref _decodes);
    public long Extractions => Interlocked.Read(ref _extractions);
    public long MetadataProbes => Interlocked.Read(ref _metadataProbes);
    public long AssetResolves => Interlocked.Read(ref _assetResolves);
    public long AssetSourceReads => Interlocked.Read(ref _assetSourceReads);

    /// <summary>Number of decodes per rendition label, e.g. "80x80".</summary>
    public IReadOnlyDictionary<string, int> DecodesByRendition =>
        new Dictionary<string, int>(_decodesByRendition, StringComparer.Ordinal);

    /// <summary>Number of decodes per (source, rendition) pair.</summary>
    public IReadOnlyDictionary<string, int> DecodesBySourceRendition =>
        new Dictionary<string, int>(_decodesBySourceRendition, StringComparer.Ordinal);

    internal void RecordDecode(string rendition, string? sourceKey, long elapsedTicks)
    {
        Interlocked.Increment(ref _decodes);
        _decodesByRendition.AddOrUpdate(rendition, 1, static (_, v) => v + 1);
        if (!string.IsNullOrEmpty(sourceKey))
        {
            string key = sourceKey + "|" + rendition;
            _decodesBySourceRendition.AddOrUpdate(key, 1, static (_, v) => v + 1);
        }
    }

    internal void RecordExtraction(string? sourceKey) => Interlocked.Increment(ref _extractions);

    internal void RecordMetadataProbe(string? sourceKey, long elapsedTicks)
    {
        Interlocked.Increment(ref _metadataProbes);
        if (!string.IsNullOrEmpty(sourceKey))
        {
            string key = sourceKey + "|native";
            _decodesBySourceRendition.AddOrUpdate(key, 1, static (_, v) => v + 1);
        }
    }

    internal void RecordAssetResolve(bool readSource, long elapsedTicks)
    {
        Interlocked.Increment(ref _assetResolves);
        if (readSource) Interlocked.Increment(ref _assetSourceReads);
    }

    public void Dispose()
    {
        if (ReferenceEquals(ImageDecodeMetrics.ActiveScope, this))
        {
            ImageDecodeMetrics.SetActiveScope(null);
        }
    }
}
