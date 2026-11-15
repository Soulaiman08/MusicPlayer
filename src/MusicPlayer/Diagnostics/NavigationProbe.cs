using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MusicPlayer.Utilities;

namespace MusicPlayer.Diagnostics;

public sealed class NavigationProbe
{
    private static readonly bool _enabled =
        Debugger.IsAttached || Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1";

    public static bool Enabled => _enabled;

    public static bool IsPageZoomDisabled =>
        Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST_NO_PAGEZOOM") == "1";

    private static long _navIdCounter;
    private static NavigationSession? _currentSession;
    private static readonly object _syncLock = new();

    public static NavigationSession? Current => _currentSession;

    public static NavigationSession? BeginNavigation(string source, string target, string opType)
    {
        if (!_enabled) return null;

        lock (_syncLock)
        {
            long id = Interlocked.Increment(ref _navIdCounter);
            var session = new NavigationSession(id, source, target, opType);
            _currentSession = session;
            LogHelper.LogInfo($"NAV-INPUT-RECEIVED id={id} op={opType} from={source} to={target} t0={session.T0}");
            return session;
        }
    }

    public static void RecordHandlerStarted()
    {
        if (!_enabled || _currentSession == null) return;
        _currentSession.RecordHandlerStarted();
    }

    public static void RecordViewModelStateUpdated()
    {
        if (!_enabled || _currentSession == null) return;
        _currentSession.RecordViewModelStateUpdated();
    }

    public static void RecordDataLoadStarted(string scope)
    {
        if (!_enabled || _currentSession == null) return;
        _currentSession.RecordDataLoadStarted(scope);
    }

    public static void RecordDataLoadCompleted(string scope, int count)
    {
        if (!_enabled || _currentSession == null) return;
        _currentSession.RecordDataLoadCompleted(scope, count);
    }

    public static void RecordCollectionPublishStarted(string scope)
    {
        if (!_enabled || _currentSession == null) return;
        _currentSession.RecordCollectionPublishStarted(scope);
    }

    public static void RecordCollectionPublishCompleted(string scope)
    {
        if (!_enabled || _currentSession == null) return;
        _currentSession.RecordCollectionPublishCompleted(scope);
    }

    public static void RecordCollectionUpdate(string collectionName)
    {
        if (!_enabled || _currentSession == null) return;
        _currentSession.IncrementCollectionUpdates(collectionName);
    }

    public static void ScheduleLayoutAndCompletion(Dispatcher dispatcher)
    {
        if (!_enabled || _currentSession == null) return;
        _currentSession.ScheduleLayoutAndCompletion(dispatcher);
    }
}

public sealed class NavigationSession
{
    private static readonly double Freq = Stopwatch.Frequency;

    public long Id { get; }
    public string Source { get; }
    public string Target { get; }
    public string OpType { get; }

    public long T0 { get; } // Input received
    public long T1 { get; private set; } // Handler started
    public long T2 { get; private set; } // ViewModel updated
    public long T3 { get; private set; } // Data load started
    public long T4 { get; private set; } // Data load completed
    public long T5 { get; private set; } // Collection publish started
    public long T6 { get; private set; } // Collection publish completed
    public long T7 { get; private set; } // Layout ready (Dispatcher.Loaded)
    public long T8 { get; private set; } // Frame presented (CompositionTarget.Rendering)

    public int HandlerThreadId { get; private set; }
    public int LoadThreadId { get; private set; }
    public int PublishThreadId { get; private set; }
    public int ItemCount { get; private set; }
    public int CollectionUpdates { get; private set; }

    private int _completed;

    public NavigationSession(long id, string source, string target, string opType)
    {
        Id = id;
        Source = source;
        Target = target;
        OpType = opType;
        T0 = Stopwatch.GetTimestamp();
        T1 = T0;
        T2 = T0;
        T7 = 0;
        T8 = 0;
    }

    public void RecordHandlerStarted()
    {
        T1 = Stopwatch.GetTimestamp();
        HandlerThreadId = Environment.CurrentManagedThreadId;
    }

    public void RecordViewModelStateUpdated()
    {
        T2 = Stopwatch.GetTimestamp();
    }

    public void RecordDataLoadStarted(string scope)
    {
        T3 = Stopwatch.GetTimestamp();
        LoadThreadId = Environment.CurrentManagedThreadId;
    }

    public void RecordDataLoadCompleted(string scope, int count)
    {
        T4 = Stopwatch.GetTimestamp();
        ItemCount = count;
    }

    public void RecordCollectionPublishStarted(string scope)
    {
        T5 = Stopwatch.GetTimestamp();
        PublishThreadId = Environment.CurrentManagedThreadId;
    }

    public void RecordCollectionPublishCompleted(string scope)
    {
        T6 = Stopwatch.GetTimestamp();
    }

    public void IncrementCollectionUpdates(string collectionName)
    {
        Interlocked.Increment(ref _collectionUpdates);
    }
    private int _collectionUpdates;

    public void ScheduleLayoutAndCompletion(Dispatcher dispatcher)
    {
        dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            T7 = Stopwatch.GetTimestamp();

            EventHandler? renderingHandler = null;
            renderingHandler = (s, e) =>
            {
                CompositionTarget.Rendering -= renderingHandler;
                Complete();
            };
            CompositionTarget.Rendering += renderingHandler;

            // Fallback timeout in case CompositionTarget.Rendering doesn't fire (e.g. headless/minimized)
            dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                if (Interlocked.CompareExchange(ref _completed, 0, 0) == 0)
                {
                    CompositionTarget.Rendering -= renderingHandler;
                    Complete();
                }
            });
        });
    }

    private void Complete()
    {
        if (Interlocked.Exchange(ref _completed, 1) != 0) return;
        T8 = Stopwatch.GetTimestamp();

        double inputRespMs = Math.Max(0, (T2 - T0) * 1000.0 / Freq);
        double dataLoadMs = (T3 > 0 && T4 >= T3) ? (T4 - T3) * 1000.0 / Freq : 0;
        double colPublishMs = (T5 > 0 && T6 >= T5) ? (T6 - T5) * 1000.0 / Freq : 0;
        double uiBlockMs = Math.Max(0, (T2 - T1) * 1000.0 / Freq) + colPublishMs;
        double uiPresStart = (T5 > 0) ? T5 : T2;
        double uiPresMs = Math.Max(0, (T8 - uiPresStart) * 1000.0 / Freq);
        double layoutMs = (T7 > 0) ? Math.Max(0, (T7 - uiPresStart) * 1000.0 / Freq) : 0;
        double totalMs = Math.Max(0, (T8 - T0) * 1000.0 / Freq);

        LogHelper.LogInfo(
            $"NAV-SUMMARY id={Id} op={OpType} from={Source} to={Target} " +
            $"inputRespMs={inputRespMs:0.00} dataLoadMs={dataLoadMs:0.00} " +
            $"colPublishMs={colPublishMs:0.00} uiPresMs={uiPresMs:0.00} layoutMs={layoutMs:0.00} " +
            $"uiBlockMs={uiBlockMs:0.00} totalMs={totalMs:0.00} items={ItemCount} updates={_collectionUpdates} " +
            $"thH={HandlerThreadId} thL={LoadThreadId} thP={PublishThreadId}");
    }
}
