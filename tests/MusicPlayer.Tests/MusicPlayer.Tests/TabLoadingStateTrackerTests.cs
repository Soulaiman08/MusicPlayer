using MusicPlayer.Utilities;
using Xunit;

namespace MusicPlayer.Tests;

public class TabLoadingStateTrackerTests
{
    [Fact]
    public void UnknownKey_IsLoading()
    {
        var tracker = new TabLoadingStateTracker();
        Assert.True(tracker.IsLoading("songs"));
    }

    [Fact]
    public void Begin_MarksLoading()
    {
        var tracker = new TabLoadingStateTracker();
        tracker.MarkLoaded("songs");
        Assert.False(tracker.IsLoading("songs"));

        tracker.Begin("songs");
        Assert.True(tracker.IsLoading("songs"));
    }

    [Fact]
    public void End_WithMatchingGeneration_MarksLoaded()
    {
        var tracker = new TabLoadingStateTracker();
        int gen = tracker.Begin("songs");
        Assert.True(tracker.End("songs", gen));
        Assert.False(tracker.IsLoading("songs"));
    }

    [Fact]
    public void End_WithStaleGeneration_DoesNotMarkLoaded()
    {
        var tracker = new TabLoadingStateTracker();
        int first = tracker.Begin("songs");
        int second = tracker.Begin("songs"); // newer load
        Assert.False(tracker.End("songs", first)); // stale
        Assert.True(tracker.IsLoading("songs"));
        Assert.True(tracker.End("songs", second));
        Assert.False(tracker.IsLoading("songs"));
    }

    [Fact]
    public void End_WithWrongGeneration_ReturnsFalse()
    {
        var tracker = new TabLoadingStateTracker();
        tracker.Begin("songs");
        Assert.False(tracker.End("songs", 999));
        Assert.True(tracker.IsLoading("songs"));
    }

    [Fact]
    public void MarkLoaded_MarksLoadedWithoutGeneration()
    {
        var tracker = new TabLoadingStateTracker();
        tracker.MarkLoaded("albums");
        Assert.False(tracker.IsLoading("albums"));
    }

    [Fact]
    public void ConcurrentKeys_AreIndependent()
    {
        var tracker = new TabLoadingStateTracker();
        int songsGen = tracker.Begin("songs");
        tracker.MarkLoaded("albums");

        Assert.True(tracker.IsLoading("songs"));
        Assert.False(tracker.IsLoading("albums"));

        tracker.End("songs", songsGen);
        Assert.False(tracker.IsLoading("songs"));
        Assert.False(tracker.IsLoading("albums"));
    }

    [Fact]
    public void FailedLoad_DoesNotBecomeLoaded_WhenEndNotCalled()
    {
        var tracker = new TabLoadingStateTracker();
        tracker.Begin("genres");
        // Simulate failure: End not called.
        Assert.True(tracker.IsLoading("genres"));
    }

    [Fact]
    public void Reload_TogglesLoadingThenLoaded()
    {
        var tracker = new TabLoadingStateTracker();
        int first = tracker.Begin("folders");
        tracker.End("folders", first);
        Assert.False(tracker.IsLoading("folders"));

        int second = tracker.Begin("folders");
        Assert.True(tracker.IsLoading("folders"));
        tracker.End("folders", second);
        Assert.False(tracker.IsLoading("folders"));
    }
}