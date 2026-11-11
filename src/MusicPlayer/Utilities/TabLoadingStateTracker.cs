using System;
using System.Collections.Generic;

namespace MusicPlayer.Utilities;

/// <summary>
/// Tracks per-key loading state for independently loaded tabs/views.
/// A key that has never completed a load is considered "Loading", so an empty
/// collection during loading is not mistaken for a genuinely empty result.
/// Generation counters prevent a stale load from overwriting a newer one.
/// </summary>
public sealed class TabLoadingStateTracker
{
    public enum State { Loading, Loaded }

    private readonly Dictionary<string, State> _states = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> _generations = new(StringComparer.OrdinalIgnoreCase);

    public bool IsLoading(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        return !_states.TryGetValue(key, out var state) || state == State.Loading;
    }

    public int Begin(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return 0;
        _states[key] = State.Loading;
        int generation = _generations.TryGetValue(key, out var current) ? current + 1 : 1;
        _generations[key] = generation;
        return generation;
    }

    public bool End(string key, int generation)
    {
        if (string.IsNullOrWhiteSpace(key)) return false;
        if (!_generations.TryGetValue(key, out var current) || current != generation) return false;
        _states[key] = State.Loaded;
        return true;
    }

    public void MarkLoaded(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return;
        _states[key] = State.Loaded;
        if (!_generations.ContainsKey(key)) _generations[key] = 0;
    }
}