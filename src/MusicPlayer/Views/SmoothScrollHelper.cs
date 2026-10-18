using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MusicPlayer.Library;
using MusicPlayer.Utilities;

namespace MusicPlayer.Views;

/// <summary>
/// AntiGravity Smooth Scroll — target-driven continuous motion engine.
///
/// Separate input from motion; two input modalities share ONE motion loop:
///   • Wheel: a wheel event accumulates TargetOffset (clamped) and seeds
///     Velocity from the wheel cadence — the motion then chases the target
///     at that CONSTANT speed (input-locked: content moves exactly as fast
///     as the wheel turns, hard stop on arrival, no glide decay tail).
///   • Drag: pressing the content and pulling moves the offset 1:1 with the
///     pointer (grab-the-sheet gesture). Releasing STOPS DEAD — no inertia,
///     no fling, no post-release physics. A plain press+release never
///     activates, so clicks behave as before.
///   • ONE continuous motion runs per ScrollViewer. A vsync-aligned
///     CompositionTarget.Rendering loop (active only while scrolling)
///     integrates Position += Velocity × dt until the target is reached;
///     arrival snaps the last remainder (no sub-pixel crawl).
///
/// The attached VerticalOffsetProperty still drives
/// ScrollViewer.ScrollToVerticalOffset each frame (60fps layout), exactly
/// like the previous animation-clock design, but the clock is gone: no
/// FillBehavior snapback, no base-value (0.0) callback, no Completed closure.
///
/// Profiling (counter semantics):
///   starts   — motion sessions begun on a viewer
///   replaced — wheel events that updated the target while one motion run was
///              already in flight (target updates, NOT restartable animations)
///   restarts — always 0 by construction: the animation clock no longer exists
///   completed— motion sessions that settled naturally
///   maxRunning — peak concurrent motions; 1 per viewer by design
/// </summary>
public static class SmoothScrollHelper
{
// ── Scroll feel ────────────────────────────────────────────────
    // Reference physics aligned with Harmonoid (48px row height):
    // 48px per notch gives exactly 1 natural row displacement per gentle wheel tick.
    private const double PixelsPerNotch = 48.0;

    // ── Refresh-rate-adaptive frame interval ──────────────────────
    // Default = 60 Hz. Updated at startup by App.xaml.cs once the real
    // monitor refresh rate is known via EnumDisplaySettings.
    // Used only as the nominal dt for the first integration step after a
    // cold wake (and as a guard against degenerate elapsed times); every
    // other step measures real Stopwatch elapsed time, so the engine is
    // frame-rate independent at any cadence the render loop delivers.
    private static double _refreshIntervalMs = 1000.0 / 60.0; // ~16.67 ms default

    /// <summary>
    /// Called once at startup (from App.xaml.cs) with the monitor's real
    /// refresh rate (Hz). Clamped to [30, 360] for safety.
    /// </summary>
    public static void SetRefreshRate(double hz)
    {
        if (hz < 30 || hz > 360) hz = 60.0;
        _refreshIntervalMs = 1000.0 / hz;
        try
        {
            MusicPlayer.Utilities.LogHelper.LogInfo(
                $"ScrollEngine: monitor {hz:0.#}Hz → nominal frame {_refreshIntervalMs:0.00}ms (driver: CompositionTarget.Rendering, vsync-aligned)");
        }
        catch { }
    }

    private const int    HeavyBurstThreshold = 4;    // ≥4 wheels arriving mid-flight = sustained burst
    private const double MaxStepMs           = 50.0; // clamp dt after stalls/window pauses
    // Floor for the step gate: step on every render pass once this gap
    // elapses (~140Hz pump measured during bursts → bounded at 250Hz by this
    // floor, and self-limited further by render duration). Keeps the
    // presented frame's temporal error ≤ one pump interval.
    private const double MinStepGapMs        = 4.0;

    // ═══════════════════════════════════════════════════════════════
    //  FEEL TUNING — all scroll-character knobs in ONE place.
    //  Adjust these to reshape the motion; no other code needs edits.
    // ═══════════════════════════════════════════════════════════════
    private const double SeedNotchIntervalMs = 55.0;
    // EMA smoothing (τ) of the notch interval — larger = steadier speed.
    private const double RateEmaTauMs        = 70.0;
    // Cadence clamps: never seed faster than one notch per frame (16ms)
    // nor slower than one notch per 150ms.
    private const double MinRateIntervalMs   = 16.0;
    private const double MaxRateIntervalMs   = 150.0;
    // Drag (press + hold + pull): movement (px) needed to tell a drag
    // apart from a click. Also requires |dy| ≥ |dx| (vertical dominance)
    // so horizontal gestures (page swipes) stay untouched.
    private const double DragThresholdPx   = 8.0;

    // Natural deceleration time-constant for position approach.
    // tau = 75ms at 60Hz gives a smooth, organic glide matching Harmonoid's Flutter curve
    // while small single ticks settle within ~250ms with no lingering crawl.
    private const double ApproachTauS      = 0.075;

    // Per-frame step cap: allows fast flicks to reach up to ~5760 px/s while maintaining smooth flow.
    private const double MaxFrameStepPx    = 96.0;

    // ── Safety / infra (not feel) ────────────────────────────────
    // Absolute safety net: extended to 1200ms to allow long continuous glides to settle naturally.
    private const double MaxFlightMs       = 1200.0;

    // Micro-pixel guard: offsets changing by less than this are not pushed
    // into ScrollToVerticalOffset — redundant sub-pixel measure/arrange passes
    // during the tail are discarded, so structural updates freeze sooner.
    private const double MicroPixelEpsilon = 0.2;

    // ── Per-viewer cached state — one instance, zero per-event allocation ──
    private sealed class ViewerState
    {
        public readonly ScrollViewer Owner;
        public double     Position;       // offset currently driven by the motion loop
        public double     Target;         // accumulated & clamped destination
        public double     Velocity;       // signed px/sec, seeded from wheel cadence
        public double     RateEmaMs;      // smoothed notch interval (0 = next wheel is cold)
        public long       LastInputTicks; // Stopwatch timestamp of the most recent wheel
        public long       LastStepTicks;  // Stopwatch timestamp of the previous integration step
        public bool       MotionActive;   // true while the shared timer must tick this viewer
        public bool       Dragging;       // press is held as a drag on this viewer

        public ViewerState(ScrollViewer owner)
        {
            Owner          = owner;
            Position       = owner.VerticalOffset;
            Target         = owner.VerticalOffset;
            owner.LayoutUpdated += OnOwnerLayoutUpdated;
        }
    }

    // Layout-cost probe (profiling only): ApplyOffset marks the write time;
    // the first LayoutUpdated after it is the end of the layout pass that the
    // write forced — the delta is what the offset change costs on the UI
    // thread (measure/arrange of the virtualized rows + recycling).
    private static long _layPendingSince;

    private static void OnOwnerLayoutUpdated(object? sender, EventArgs e)
    {
        long since = _layPendingSince;
        if (since == 0L || !ProfilingEnabled) return;
        _layPendingSince = 0L;
        Profiler.RecordLayout((Stopwatch.GetTimestamp() - since) * 1000.0 / Stopwatch.Frequency);
    }

    // ── Attached property that drives ScrollViewer.VerticalOffset each frame ──
    private static readonly DependencyProperty VerticalOffsetProperty =
        DependencyProperty.RegisterAttached(
            "VerticalOffsetAnim",
            typeof(double),
            typeof(SmoothScrollHelper),
            new PropertyMetadata(0.0, OnVerticalOffsetChanged));

    private static void OnVerticalOffsetChanged(
        DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ScrollViewer sv) return;

        // Execution gate — single-writer guard: while a wheel burst is active,
        // ONLY motion-originated writes (marked via _applyingMotion) are pushed
        // through to ScrollToVerticalOffset. Any other writer that reaches the
        // attached property mid-burst is dropped, so the layout engine is never
        // invalidated twice per cycle and the visualizer's render frames keep
        // priority. Motion pushes are never gated (they chain _applyingMotion).
        if (_layoutGateBusy && !_applyingMotion) return;

        double newOffset = (double)e.NewValue;
        if (Math.Abs(newOffset - sv.VerticalOffset) < MicroPixelEpsilon)
            return; // sub-resolution tick — no redundant layout invalidation

        if (ProfilingEnabled) Profiler.SampleOffset(newOffset);

        _applyingMotion = true;
        try
        {
            sv.ScrollToVerticalOffset(newOffset);
            if (ProfilingEnabled) _layPendingSince = Stopwatch.GetTimestamp();
        }
        finally
        {
            _applyingMotion = false;
        }
    }

    // ── Per-viewer tables ──────────────────────────────────────────
    private static readonly ConditionalWeakTable<ScrollViewer, ViewerState> _states     = new();
    private static readonly ConditionalWeakTable<ScrollViewer, object>      _configured = new();

    // ── Initialization ─────────────────────────────────────────────
    private static bool _isInitialized;

    public static void Initialize()
    {
        if (_isInitialized) return;
        _isInitialized = true;

        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.PreviewMouseWheelEvent,
            new MouseWheelEventHandler(OnGlobalPreviewMouseWheel),
            handledEventsToo: false);

        // Drag-to-scroll: press+pull moves the content 1:1, release stops
        // dead (no fling).
        // Tunneling class handlers see every press/move/release before any
        // instance handler, so activation can suppress the row's click by
        // handling the release (and only the release).
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(OnGlobalPreviewMouseLeftButtonDown),
            handledEventsToo: false);
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.PreviewMouseMoveEvent,
            new MouseEventHandler(OnGlobalPreviewMouseMove),
            handledEventsToo: false);
        EventManager.RegisterClassHandler(
            typeof(UIElement),
            UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(OnGlobalPreviewMouseLeftButtonUp),
            handledEventsToo: false);
    }

    // ── Hardware layout constraints — applied once per ScrollViewer ──
    // Ultra-lightweight dynamic virtualization: the framework's axis
    // Measure/Arrange passes only ever touch the 10–12 rows in the viewport.
    // Containers are recycled (never destroyed), the scroll unit is Pixel, and
    // NO BitmapCache / GPU-VRAM surface is allocated — the scroll surface is
    // re-rendered dynamically each frame, so mixed-integer-graphics hardware
    // with small VRAM budgets (e.g. 128MB) never thrashes.
    private static void EnsureOptimized(ScrollViewer sv)
    {
        if (_configured.TryGetValue(sv, out _)) return;

        // 1. Lock container recycling hooks — containers are reused instead of
        //    destroyed/recreated on every restructured generation. Recycling
        //    mode with Pixel scroll unit keeps the ItemsPresenter host frozen:
        //    no Measure/Arrange runs for content that leaves the viewport.
        VirtualizingPanel.SetIsVirtualizing(sv, true);
        VirtualizingPanel.SetVirtualizationMode(sv, VirtualizationMode.Recycling);
        VirtualizingPanel.SetScrollUnit(sv, ScrollUnit.Pixel);
        VirtualizingPanel.SetCacheLengthUnit(sv, VirtualizationCacheLengthUnit.Item);
        VirtualizingPanel.SetCacheLength(sv, new VirtualizationCacheLength(4, 4));

        // Instant response — no deferred ghost scroll (keeps the animation clock
        // the single source of truth for the offset).
        sv.IsDeferredScrollingEnabled = false;

        // Hardware-accelerated HighQuality bilinear scaling for crisp images without GPU downsampling stall.
        RenderOptions.SetBitmapScalingMode(sv, BitmapScalingMode.HighQuality);

        _configured.Add(sv, new object());
    }

    // ── Global mouse-wheel handler — INPUT ONLY ─────────────────────
    // Collision-free separation of concerns:
    //   • Mouse wheel  → accumulate TargetOffset (clamped), seed Velocity
    //                    from the notch cadence, wake the motion.
    //   • Motion loop  → constant-velocity chase of TargetOffset (the
    //                    velocity the wheel seeded), never restarted per
    //                    wheel; arrives with a hard snap — no decay tail.
    private static void OnGlobalPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        MarkInteraction();
        if (e.Handled) return;

        var sv = FindScrollableTarget(e.OriginalSource as DependencyObject, e.Delta);
        if (sv == null) return;

        EnsureOptimized(sv);

        double maxOffset = sv.ScrollableHeight;
        if (maxOffset <= 0.5) return;

        // Get or create the single cached state for this viewer
        if (!_states.TryGetValue(sv, out var state))
        {
            state = new ViewerState(sv);
            _states.Add(sv, state);
        }

        long nowTicks = Stopwatch.GetTimestamp();

        // The sheet is in the hand — wheels do nothing during a drag.
        if (state.Dragging)
        {
            e.Handled = true;
            return;
        }

        // Fresh wheel session: ensure cached coordinates start exactly from live viewer offset
        if (!state.MotionActive)
        {
            state.Position  = sv.VerticalOffset;
            state.Target    = sv.VerticalOffset;
            state.Velocity  = 0.0;
            state.RateEmaMs = 0.0;
        }

        // 1. DIRECTION REVERSAL: an incoming wheel that opposes the pending motion
        // instantly cancels the stale direction and re-anchors to current position.
        double pixelDelta = PixelsPerNotch * (e.Delta / 120.0);
        bool wheelUp      = e.Delta > 0;
        double currentOffset = state.Position;

        if ((wheelUp && state.Target > currentOffset) || (!wheelUp && state.Target < currentOffset))
        {
            state.Target   = currentOffset;
            state.Velocity = 0.0;
        }

        // Measure interval between wheel ticks to scale velocity on rapid flicks
        double intervalMs = state.LastInputTicks != 0L
            ? (nowTicks - state.LastInputTicks) * 1000.0 / Stopwatch.Frequency
            : 200.0;

        // Acceleration on rapid rolling (<150ms): allows long spins/flicks to travel deep
        // into the list with natural momentum, matching Harmonoid desktop feel.
        double accel = 1.0;
        if (intervalMs < 150.0)
        {
            accel = 1.0 + Math.Min(2.8, Math.Pow((150.0 - intervalMs) / 45.0, 1.25));
        }

        double rawTarget = state.Target - (pixelDelta * accel);

        // Generous lead cap for long flicks (up to 120 rows ≈ 5760px)
        double maxLead   = PixelsPerNotch * 120.0;
        double leadMin   = Math.Max(0.0, currentOffset - maxLead);
        double leadMax   = Math.Min(maxOffset, currentOffset + maxLead);
        double newTarget = Math.Clamp(rawTarget, leadMin, leadMax);

        if (ProfilingEnabled)
        {
            Profiler.StartBurst();
            Profiler.RecordWheel(e.Delta, sv.VerticalOffset, newTarget,
                state.Position, newTarget, restartAllowed: true, state.MotionActive);
            if (state.MotionActive) Profiler.MarkAnimationReplaced();
            else Profiler.MarkAnimationStart(1);
        }

        state.Target          = newTarget;
        state.LastInputTicks  = nowTicks;

        if (!state.MotionActive)
        {
            state.MotionActive  = true;
            state.LastStepTicks = 0L;
            _layoutGateBusy     = true;
        }
        AlbumArtLoader.SetScrollingActive(true);

        EnsureMotionRendering(sv);
        e.Handled = true;
    }

    // ── Continuous motion engine ───────────────────────────────────
    // Vsync-aligned driver: motion is stepped from CompositionTarget.Rendering,
    // WPF's per-frame render callback. Each step therefore lands at ONE fixed
    // position in the frame pipeline — immediately before layout/render for
    // that frame — so the offset update and the frame it appears in are
    // always the same frame. A DispatcherTimer at Render priority instead
    // fires at an OS-timer phase independent of the compositor's vsync phase;
    // when that phase races the present deadline the frame's content arrives
    // one vsync late and the next one on time — perceived as irregular
    // stutter even though the math runs at 60 Hz. The handler is subscribed
    // only while a motion is in flight (first wheel → settle), so the loop
    // never competes with the Audio Visualizer's Rendering subscription when
    // the list is at rest.
    private static bool _motionRenderingActive;
    private static long  _lastMotionStepTicks;
    private static DispatcherTimer? _paceTimer;
    private static double _paceMult = 0.83;
    private static bool   _paceMultRead;
    private static double _achievedGapMs; // EWMA of accepted step gaps (SSCB diagnostics)

    private static double PaceMult()
    {
        if (!_paceMultRead)
        {
            _paceMultRead = true;
            var v = Environment.GetEnvironmentVariable("MUSICPLAYER_PACE_MULT");
            if (double.TryParse(v, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var m) && m > 0.3 && m < 1.5)
            {
                _paceMult = m;
            }
        }
        return _paceMult;
    }

    // Read-only access for BurstProfiler diagnostics (SSCB line).
    internal static double PaceMultNow => PaceMult();
    internal static double AchievedGapMs => _achievedGapMs;

    private static void EnsureMotionRendering(ScrollViewer sv)
    {
        _lastMotionStepTicks = 0L;
        _paceTimer?.Stop();
        if (!_motionRenderingActive)
        {
            _motionRenderingActive = true;
            CompositionTarget.Rendering += OnMotionRendering;
            EnsureHighPrecisionTimer(true);
        }
    }

    // Layout-engine execution gate: while true a wheel burst exclusively owns
    // vertical-offset writes. All offset application is funnelled through the
    // single per-frame ApplyOffset call — at most one ScrollToVerticalOffset per
    // motion step, no nested/reentrant invalidation, and nothing outside the
    // motion loop can write the attached property during the burst. That bounds
    // wasted measure/arrange passes to the frame cadence, so the
    // Audio Visualizer's render cycles keep their share of the UI thread.
    private static bool _layoutGateBusy;
    private static bool _applyingMotion;

    // One-shot scheduled for the exact time the next step falls due. Render
    // passes that arrive before the deadline are consumed by the pace gate;
    // without this arm the pump would have to be re-invalidated immediately
    // (render-spin), and without a scheduled follow-up the pump could strand
    // while a step is still due. Fires at Render priority so the kick lands in
    // the next composition pass; the 1ms OS timer granularity is already
    // guaranteed during motion by timeBeginPeriod(1).
    private static void ArmPaceTimer(double remainingMs)
    {
        if (_paceTimer == null)
        {
            _paceTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(1)
            };
            _paceTimer.Tick += (_, _) =>
            {
                _paceTimer.Stop();
                if (ProfilingEnabled)
                {
                    double lagMs = (Stopwatch.GetTimestamp() - _paceArmTicks) * 1000.0
                                   / Stopwatch.Frequency - _paceIntervalMs;
                    Profiler.RecordTickLag(lagMs);
                }
                bool any = false;
                foreach (var kv in _states)
                {
                    if (!kv.Value.MotionActive) continue;
                    kv.Value.Owner.InvalidateVisual();
                    any = true;
                }
                if (!any) EnsureHighPrecisionTimer(false);
            };
        }
        if (remainingMs < 1.0) remainingMs = 1.0;
        _paceIntervalMs = remainingMs;
        _paceArmTicks   = Stopwatch.GetTimestamp();
        _paceTimer.Interval = TimeSpan.FromMilliseconds(remainingMs);
        _paceTimer.Start();
    }

    private static long   _paceArmTicks;
    private static double _paceIntervalMs;

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint uMilliseconds);

    [System.Runtime.InteropServices.DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint uMilliseconds);

    private static bool _timePeriodActive;

    private static void EnsureHighPrecisionTimer(bool enable)
    {
        try
        {
            if (enable && !_timePeriodActive)
            {
                TimeBeginPeriod(1);
                _timePeriodActive = true;
            }
            else if (!enable && _timePeriodActive)
            {
                TimeEndPeriod(1);
                _timePeriodActive = false;
            }
        }
        catch { }
    }

    private static void OnMotionRendering(object? sender, EventArgs e)
    {
        long nowTicks = Stopwatch.GetTimestamp();

        // ── Step gate: one step per render pass (bounded) ─────────────────
        // Earlier revisions gated at refresh×0.83 (≈13.8ms), but the pump
        // fires ~7ms apart during bursts, so the gate + timer rounded the
        // ACHIEVED gap up to 16.7–17.9ms median with tails past 31ms. Against
        // the 16.67ms present grid that meant roughly a third of presents got
        // NO new offset (frozen frame) while others got two — a periodic beat
        // that reads as low/weak fps even though vsync itself is fine (60Hz
        // panel, confirmed via EnumDisplaySettings). Aiming anywhere else just
        // moves the beat (p/c̄ must be an integer for a constant per-present
        // step count), so the robust fix is the smallest practical gap: step
        // on every render pass, which bounds the temporal error of whatever is
        // on screen to one pump interval (~7ms) — every present then shows an
        // offset no older than that, so motion never stalls. Rate stays bounded
        // by MinStepGapMs + render duration; StepState integrates REAL elapsed
        // dt, so a higher tick rate changes no motion kinematics.
        double elapsedMs = _lastMotionStepTicks != 0L
            ? (nowTicks - _lastMotionStepTicks) * 1000.0 / Stopwatch.Frequency
            : 0.0;
        if (_lastMotionStepTicks != 0L)
        {
            _achievedGapMs = _achievedGapMs <= 1.0
                ? elapsedMs
                : _achievedGapMs + 0.3 * (elapsedMs - _achievedGapMs);
        }
        _lastMotionStepTicks = nowTicks;
        _paceTimer?.Stop();

        bool anyActive = false;

        // All scrolling happens on the UI thread; the table is only mutated
        // there too, so enumeration is safe without locks.
        foreach (var kv in _states)
        {
            if (!kv.Value.MotionActive) continue;
            if (StepState(kv.Value, nowTicks)) anyActive = true;
        }

        if (!anyActive)
        {
            // Burst over: hand the UI thread back to the visualizer.
            _layoutGateBusy = false;
            if (_motionRenderingActive)
            {
                _motionRenderingActive = false;
                CompositionTarget.Rendering -= OnMotionRendering;
            }
            EnsureHighPrecisionTimer(false);
        }
    }

    /// <returns>true while the viewer still needs ticking, false when settled.</returns>
    private static bool StepState(ViewerState s, long nowTicks)
    {
        // dt: use actual elapsed time between ticks for frame-rate-independent
        // integration.
        // First step: always use the nominal frame interval as dt.
        // Using the real elapsed from the wheel event (sinceWheelMs) causes a
        // jump on cold start: if the renderer was busy (20-50ms late), the
        // first step covers an outsized distance — the perceived stutter when
        // starting wheel scrolling after idle. A fixed nominal dt means the
        // first frame always covers the same fraction of distance as every
        // subsequent frame, so the motion feels connected from frame 1.
        double rawGapMs;
        if (s.LastStepTicks == 0L)
        {
            rawGapMs = _refreshIntervalMs; // nominal dt — consistent, no jump
        }
        else
        {
            rawGapMs = (nowTicks - s.LastStepTicks) * 1000.0 / Stopwatch.Frequency;
        }
        if (ProfilingEnabled && rawGapMs > 0.0) Profiler.RecordStepGap(rawGapMs, s.Position);
        double dtMs = rawGapMs;
        if (dtMs > MaxStepMs) dtMs = MaxStepMs;
        if (dtMs < 0.1)       dtMs = _refreshIntervalMs;
        double dt = dtMs / 1000.0;
        s.LastStepTicks = nowTicks;
        if (ProfilingEnabled) Profiler.MarkFirstStep(nowTicks);

        double dist = s.Target - s.Position;
        bool arrived;

        if (Math.Abs(dist) <= 0.4)
        {
            s.Position = s.Target;
            s.Velocity = 0.0;
            arrived    = true;
        }
        else
        {
            // VSync-locked natural approach: position converges toward target
            // with tau = 48ms matching Harmonoid's deceleration curve.
            double factor = 1.0 - Math.Exp(-dt / ApproachTauS);
            double step   = dist * factor;

            double absDist = Math.Abs(dist);
            if (Math.Abs(step) > MaxFrameStepPx)
                step = Math.Sign(step) * MaxFrameStepPx;

            if (absDist <= 0.5 || Math.Abs(step) < 0.15)
            {
                s.Position = s.Target;
                s.Velocity = 0.0;
                arrived    = true;
            }
            else
            {
                double newPos = s.Position + step;
                double maxOff = s.Owner.ScrollableHeight;
                if (newPos <= 0.0)
                {
                    newPos     = 0.0;
                    s.Target   = 0.0;
                    s.Velocity = 0.0;
                    arrived    = true;
                }
                else if (newPos >= maxOff)
                {
                    newPos     = maxOff;
                    s.Target   = maxOff;
                    s.Velocity = 0.0;
                    arrived    = true;
                }
                else
                {
                    s.Velocity = step / dt;
                    arrived    = false;
                }
                s.Position = newPos;
            }
        }

        if (arrived)
        {
            s.Position     = s.Target;
            s.Velocity     = 0.0;
            s.MotionActive = false;
            ApplyOffset(s);
            if (ProfilingEnabled) Profiler.MarkAnimationCompleted(catchUpRestart: false);
            AlbumArtLoader.SetScrollingActive(false);
            return false;
        }

        ApplyOffset(s);
        return true;
    }

    private static void ApplyOffset(ViewerState s)
    {
        double diff = s.Position - s.Owner.VerticalOffset;
        if (Math.Abs(diff) < MicroPixelEpsilon) return;
        _applyingMotion = true;
        try
        {
            s.Owner.SetValue(VerticalOffsetProperty, s.Position);
        }
        catch
        {
            // Viewer torn down mid-scroll (window closed) — stop driving it.
            s.MotionActive = false;
        }
        finally
        {
            _applyingMotion = false;
        }
    }

    // ── Visual-tree helpers ────────────────────────────────────────
    private static ScrollViewer? FindScrollableTarget(DependencyObject? start, int delta)
    {
        DependencyObject? cur = start;
        while (cur != null)
        {
            if (cur is ScrollViewer sv && CanScrollVertical(sv, delta))
                return sv;
            cur = VisualTreeHelper.GetParent(cur);
        }
        return null;
    }

    private static bool CanScrollVertical(ScrollViewer sv, int delta)
    {
        if (!sv.IsVisible || sv.ScrollableHeight <= 0.5) return false;
        if (delta > 0) return sv.VerticalOffset > 0.5;
        if (delta < 0) return sv.VerticalOffset < sv.ScrollableHeight - 0.5;
        return false;
    }

    // ══════════════════════════════════════════════════════════════
    //  DRAG-TO-SCROLL — grab the sheet, pull, release to stop dead
    // ══════════════════════════════════════════════════════════════
    // Pressing the content and pulling behaves like grabbing a sheet of
    // paper: the list follows the pointer 1:1. Releasing STOPS DEAD —
    // there is deliberately no inertial fling (no post-release physics,
    // no extra frames rendered for a glide nobody asked for). A plain
    // press+release travels < DragThresholdPx and falls through
    // untouched, so clicks (play, navigate) behave exactly as before —
    // the row's click moves to MouseLeftButtonUp, and an activated drag
    // handles the release, which suppresses the click.
    private static ViewerState? _dragState;      // press candidate (or active drag)
    private static Point        _dragStart;
    private static Point        _dragLast;
    private static bool         _dragActive;

    private static void OnGlobalPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        MarkInteraction();
        var src = e.OriginalSource as DependencyObject;
        if (src == null) return;

        // A new press heals any drag that lost its release (capture stolen,
        // window deactivated, …) before arming the new candidate.
        if (_dragActive || _dragState != null) ForceEndDrag();

        var sv = FindDragTarget(src);
        if (sv == null) return;
        EnsureOptimized(sv);

        if (!_states.TryGetValue(sv, out var st))
        {
            st = new ViewerState(sv);
            _states.Add(sv, st);
        }

        _dragState     = st;
        _dragStart     = e.GetPosition(null);
        _dragLast      = _dragStart;
        _dragActive    = false;
    }

    private static void OnGlobalPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var s = _dragState;
        if (s == null) return;                 // fast path — no drag pending

        Point pt = e.GetPosition(null);

        if (!_dragActive)
        {
            double dx = pt.X - _dragStart.X, dy = pt.Y - _dragStart.Y;
            // Vertical dominance keeps horizontal gestures (page swipe,
            // queue swipe) for their own owners.
            if (Math.Abs(dy) < DragThresholdPx || Math.Abs(dy) < Math.Abs(dx)) return;

            // Capture before mutating state — if it is refused the press
            // falls through as a plain click with nothing to clean up.
            if (!s.Owner.CaptureMouse())
            {
                _dragState = null;
                return;
            }

            _dragActive    = true;
            s.Dragging     = true;
            s.MotionActive = false;            // a glide in flight is grabbed mid-air
            s.LastStepTicks = 0L;
            s.Velocity     = 0.0;
            // Start from the viewer's true offset (an external writer may
            // have moved it since the state was cached) — no jump on grab.
            s.Position     = s.Owner.VerticalOffset;
            s.Target       = s.Position;
            _layoutGateBusy = true;
            AlbumArtLoader.SetScrollingActive(true);
            _dragLast      = pt;
            MarkInteraction();
            e.Handled = true;
            return;
        }

        // Active drag: 1:1 follow — the content moves exactly with the
        // pointer. No velocity tracking (the release stops dead, so there
        // is nothing to seed).
        MarkInteraction();
        double dY = pt.Y - _dragLast.Y;
        _dragLast = pt;

        double maxOff = s.Owner.ScrollableHeight;
        s.Position = Math.Clamp(s.Position - dY, 0.0, maxOff);
        s.Target   = s.Position;
        ApplyOffset(s);
        e.Handled = true;

        // Capture lost (system steal, window deactivated) — finish cleanly.
        if (!s.Owner.IsMouseCaptured) ForceEndDrag();
    }

    private static void OnGlobalPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var s = _dragState;
        if (s == null) return;
        _dragState = null;
        if (!_dragActive) return;              // plain click — let it through

        _dragActive = false;
        s.Dragging  = false;
        s.Owner.ReleaseMouseCapture();

        // Release = HARD STOP. No velocity hand-off, no inertial fling:
        // the sheet stops dead where the finger left it — zero
        // post-release frames, zero device cost. Clear the gates only if
        // no other viewer still has a wheel motion in flight.
        s.Velocity      = 0.0;
        s.Target        = s.Position;
        s.MotionActive  = false;
        s.LastStepTicks = 0L;
        StopMotionPumpIfIdle();
        e.Handled = true;                      // a drag must never click
    }

    // Tear down a candidate/active drag without a release event — the
    // same hard stop as a real release (a new press takes over).
    private static void ForceEndDrag()
    {
        var s = _dragState;
        _dragState = null;
        bool wasActive = _dragActive;
        _dragActive = false;
        if (s == null) return;
        s.Dragging = false;
        try { s.Owner.ReleaseMouseCapture(); } catch { }
        if (!wasActive) return;
        s.Velocity      = 0.0;
        s.Target        = s.Position;
        s.MotionActive  = false;
        s.LastStepTicks = 0L;
        StopMotionPumpIfIdle();
    }

    /// <summary>
    /// Safely halts any active smooth-scrolling animations across all tracked viewers,
    /// releases the layout gate, and detaches the CompositionTarget.Rendering hook.
    /// Called on tab change, window deactivation, or view transition.
    /// </summary>
    public static void CancelActiveMotions()
    {
        foreach (var kv in _states)
        {
            if (kv.Value.MotionActive)
            {
                kv.Value.MotionActive = false;
                kv.Value.Velocity = 0.0;
                kv.Value.Target = kv.Value.Position;
            }
        }
        StopMotionPumpIfIdle();
    }

    // End-of-motion cleanup when nothing is left to animate: drop the
    // layout/scroll gates and unsubscribe the render pump + high-precision
    // timer so a hard-stopped drag can't leave them latched (the pump's own
    // settle path does the same, but only if a render pass still fires).
    private static void StopMotionPumpIfIdle()
    {
        foreach (var kv in _states)
            if (kv.Value.MotionActive) return;
        _layoutGateBusy = false;
        AlbumArtLoader.SetScrollingActive(false);
        if (_motionRenderingActive)
        {
            _motionRenderingActive = false;
            CompositionTarget.Rendering -= OnMotionRendering;
        }
        _paceTimer?.Stop();
        EnsureHighPrecisionTimer(false);
    }

    // First visible vertical-scrollable ScrollViewer above the hit point,
    // unless the press belongs to an owner with its own press-drag gestures:
    // the Queue list (reorder/swipe) or an interactive control (thumb,
    // slider, scroll bar, text box). Full walk to the root so overlays,
    // popups and modal backdrops (which are siblings of the content, not
    // ancestors) can never claim a scrollable behind them.
    private static ScrollViewer? FindDragTarget(DependencyObject src)
    {
        DependencyObject? cur = src;
        ScrollViewer? sv = null;
        while (cur != null)
        {
            if (sv == null && cur is ScrollViewer s0 && s0.IsVisible && s0.ScrollableHeight > 0.5)
                sv = s0;
            if (cur is FrameworkElement { Name: "QueueListBox" }) return null;
            if (IsInteractive(cur)) return null;
            cur = NextParent(cur);
        }
        return sv;
    }

    private static bool IsInteractive(DependencyObject cur) =>
           cur is System.Windows.Controls.Primitives.Thumb
        or Slider
        or System.Windows.Controls.Primitives.ScrollBar
        or System.Windows.Controls.Primitives.TextBoxBase
        or ComboBox;

    // Parent of either a Visual (visual tree) or a content element (logical
    // tree) — text Runs under a TextBlock hit-test as content elements, and
    // VisualTreeHelper.GetParent throws on them.
    private static DependencyObject? NextParent(DependencyObject cur)
    {
        if (cur is FrameworkContentElement fce) return fce.Parent;
        if (cur is Visual || cur is System.Windows.Media.Media3D.Visual3D)
            return VisualTreeHelper.GetParent(cur);
        return null;
    }

    // ══════════════════════════════════════════════════════════════
    //  PROFILING & DIAGNOSTICS
    // ══════════════════════════════════════════════════════════════

    internal static bool ProfilingEnabled;
    internal static BurstProfiler Profiler = new BurstProfiler();
    private static readonly Stopwatch _frameSw       = new();
    private static long _totalFrameUs;
    private static long _maxFrameUs;
    private static long _minFrameUs;
    private static long _frameCount;
    private static long _gt16Count;
    private static long _gt33Count;
    private static long _scrollFrameCount;
    private static long _idleFrameCount;
    private static int  _inRender;
    private static long _scrollPhaseStartTicks;
    private static bool _scrollPhaseActive;

    internal static void AutoStartIfPerfTest()
    {
        try
        {
            if (Environment.GetEnvironmentVariable("MUSICPLAYER_PERFTEST") == "1")
            {
                ProfilingEnabled = true;
                _frameSw.Start();
                CompositionTarget.Rendering += OnProfilingRender;
            }
        }
        catch { }
    }

    // ── Tab-slide frame probe (PERFTEST only) ─────────────────────
    // Captures real frame pacing across a whole TRACKS/ARTISTS/ALBUMS/
    // GENRES/FOLDERS switch: firstFrameMs = click → first render tick
    // (the UpdateLayout prep stall), then the gap distribution over the
    // slide + data-load window. One log line when the window closes.
    private static readonly List<double>       _tabGaps = new(512);
    private static readonly List<double>       _tabWorst = new(8);   // [relMs, gapMs] pairs > 25ms
    private static DispatcherTimer? _tabEndTimer;
    private static long   _tabT0;
    private static long   _tabFirst;
    private static long   _tabLast;
    private static string _tabName = "";
    private static int    _tabGt16;
    private static int    _tabGt33;
    private static int    _tabGc0AtStart;
    private static int    _tabGc1AtStart;
    private static int    _tabGc2AtStart;

    internal static void BeginTabSlide(string tab)
    {
        CancelActiveMotions();
        MarkInteraction();
        if (!ProfilingEnabled) return;
        try
        {
            _tabGaps.Clear();
            _tabWorst.Clear();
            _tabGt16 = 0;
            _tabGt33 = 0;
            _tabName = tab;
            _tabT0 = _tabLast = _tabFirst = 0L;
            _tabT0 = Stopwatch.GetTimestamp();
            _tabLast = _tabT0;
            _tabGc0AtStart = System.GC.CollectionCount(0);
            _tabGc1AtStart = System.GC.CollectionCount(1);
            _tabGc2AtStart = System.GC.CollectionCount(2);
            CompositionTarget.Rendering += OnTabSlideRender;
            if (_tabEndTimer != null) { _tabEndTimer.Stop(); _tabEndTimer.Tick -= OnTabSlideWindowEnd; }
            _tabEndTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(1100)
            };
            _tabEndTimer.Tick += OnTabSlideWindowEnd;
            _tabEndTimer.Start();
        }
        catch { }
    }

    private static void OnTabSlideRender(object? sender, EventArgs e)
    {
        if (_tabT0 == 0L) return;
        long now = Stopwatch.GetTimestamp();
        if (_tabFirst == 0L)
        {
            _tabFirst = now;
        }
        else
        {
            double ms = (now - _tabLast) * 1000.0 / Stopwatch.Frequency;
            if (ms > 0.01 && ms < 2000.0)
            {
                _tabGaps.Add(ms);
                if (ms > 16.67) _tabGt16++;
                if (ms > 33.33) _tabGt33++;
                if (ms > 25.0 && _tabWorst.Count < 800) // keep ALL: the true max must appear in worst=
                {
                    _tabWorst.Add((_tabLast - _tabT0) * 1000.0 / Stopwatch.Frequency); // frame start rel ms
                    _tabWorst.Add(ms);
                }
            }
        }
        _tabLast = now;
    }

    private static void OnTabSlideWindowEnd(object? sender, EventArgs e)
    {
        if (_tabEndTimer != null) { _tabEndTimer.Stop(); _tabEndTimer.Tick -= OnTabSlideWindowEnd; }
        if (_tabT0 == 0L) return;
        long t0 = _tabT0, first = _tabFirst;
        _tabT0 = 0L;
        CompositionTarget.Rendering -= OnTabSlideRender;

        try
        {
            double firstMs = first != 0L ? (first - t0) * 1000.0 / Stopwatch.Frequency : 0.0;
            int n = _tabGaps.Count;
            if (n == 0)
            {
                MusicPlayer.Utilities.LogHelper.LogInfo(
                    $"PERF-TAB {_tabName}: firstFrameMs={firstMs:0.0} frames=0 (no render ticks)");
                return;
            }
            var sorted = new List<double>(_tabGaps);
            sorted.Sort();
            double sum = 0, max = sorted[^1];
            foreach (double d in sorted) sum += d;
            double avg = sum / n;
            double med = sorted[n / 2];
            double p95 = sorted[Math.Min(n - 1, (int)Math.Ceiling(0.95 * n) - 1)];
            int gc0 = System.GC.CollectionCount(0) - _tabGc0AtStart;
            int gc1 = System.GC.CollectionCount(1) - _tabGc1AtStart;
            int gc2 = System.GC.CollectionCount(2) - _tabGc2AtStart;
            // worst gaps as relMs>gapMs (frame-start offset from click)
            var pairs = new List<(double rel, double gap)>();
            for (int i = 0; i + 1 < _tabWorst.Count; i += 2)
                pairs.Add((_tabWorst[i], _tabWorst[i + 1]));
            pairs.Sort((a, b) => b.gap.CompareTo(a.gap));
            var wb = new System.Text.StringBuilder(" worst=");
            for (int i = 0; i < pairs.Count && i < 3; i++)
            {
                if (i > 0) wb.Append(',');
                wb.Append('@').Append(pairs[i].rel.ToString("0")).Append(':').Append(pairs[i].gap.ToString("0"));
            }
            MusicPlayer.Utilities.LogHelper.LogInfo(
                $"PERF-TAB {_tabName}: firstFrameMs={firstMs:0.0} frames={n} avgMs={avg:0.00} medMs={med:0.00} p95Ms={p95:0.00} maxMs={max:0.00} gt16={_tabGt16} gt33={_tabGt33} gcD={gc0}/{gc1}/{gc2}{wb}");
        }
        catch { }
        _tabGaps.Clear();
    }

    public static void StartDiagnostics() { }

    /// <summary>ms since the current tab-slide probe started; -1 when no probe.</summary>
    internal static double TabRelMs()
    {
        long t0 = _tabT0;
        if (t0 == 0L) return -1.0;
        return (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
    }

    // ── Interaction recency ────────────────────────────────────────
    // Heavy startup work (album-art warmup, artist face-crop re-process,
    // wallpaper thumbnails) used to fight the UI thread for CPU during the
    // first seconds after launch — exactly when the user opens views and
    // scrolls. These gates let that work wait for the user to pause instead.
    private static long _lastInteractionTicks; // 0 = never interacted

    internal static void MarkInteraction() =>
        Interlocked.Exchange(ref _lastInteractionTicks, Stopwatch.GetTimestamp());

    /// <summary>True while the user interacted within <paramref name="quietMs"/> (never interacted → false).</summary>
    internal static bool IsInteractingNow(int quietMs = 1500)
    {
        long ts = Interlocked.Read(ref _lastInteractionTicks);
        if (ts == 0L) return false;
        return (Stopwatch.GetTimestamp() - ts) * 1000.0 / Stopwatch.Frequency < quietMs;
    }

    /// <summary>
    /// Resumes only when the user has been quiet for <paramref name="quietMs"/> AND either
    /// already interacted at least once or <paramref name="minAgeMs"/> have elapsed since the
    /// call (so an untouched app still starts the warmup by itself). <paramref name="maxWaitMs"/>
    /// is a hard safety bound. The default 4s pause is deliberate: users typically start
    /// scrolling within a second or two of opening a view, and starting a CPU-heavy job
    /// right then is exactly what stalls those first frames.
    /// </summary>
    internal static async Task WaitUntilIdleForBackgroundAsync(
        int quietMs = 4000, double minAgeMs = 45000, int maxWaitMs = 300000)
    {
        try
        {
            long t0 = Stopwatch.GetTimestamp();
            while (true)
            {
                double ageMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
                if (ageMs >= maxWaitMs) return;

                long ts = Interlocked.Read(ref _lastInteractionTicks);
                bool allowed = ts != 0L || ageMs >= minAgeMs;
                bool quiet = ts == 0L ||
                    (Stopwatch.GetTimestamp() - ts) * 1000.0 / Stopwatch.Frequency >= quietMs;
                if (allowed && quiet) return;

                await Task.Delay(250).ConfigureAwait(false);
            }
        }
        catch { }
    }

    public static void ResetDiagnostics()
    {
        Interlocked.Exchange(ref _frameCount,       0);
        Interlocked.Exchange(ref _totalFrameUs,     0);
        Interlocked.Exchange(ref _maxFrameUs,       0);
        Interlocked.Exchange(ref _minFrameUs,       0);
        Interlocked.Exchange(ref _gt16Count,        0);
        Interlocked.Exchange(ref _gt33Count,        0);
        Interlocked.Exchange(ref _scrollFrameCount, 0);
        Interlocked.Exchange(ref _idleFrameCount,   0);
        _scrollPhaseActive = false;
    }

    public static string DumpDiagnostics()
    {
        long   fc    = Interlocked.Read(ref _frameCount);
        double avg   = fc > 0 ? (double)Interlocked.Read(ref _totalFrameUs) / fc : 0;
        long   maxUs = Interlocked.Read(ref _maxFrameUs);
        long   minUs = Interlocked.Read(ref _minFrameUs);
        long   gt16  = Interlocked.Read(ref _gt16Count);
        long   gt33  = Interlocked.Read(ref _gt33Count);
        long   sf    = Interlocked.Read(ref _scrollFrameCount);
        long   idle  = Interlocked.Read(ref _idleFrameCount);
        return $"SSC: frames={fc} scrollFrames={sf} idleFrames={idle} avgUs={avg:F0} maxUs={maxUs} minUs={minUs} gt16={gt16} gt33={gt33}";
    }

    internal static void BeginScrollPhase()
    {
        ResetDiagnostics();
        _scrollPhaseActive     = true;
        _scrollPhaseStartTicks = Stopwatch.GetTimestamp();
    }

    internal static string EndScrollPhase()
    {
        _scrollPhaseActive = false;
        return DumpDiagnostics();
    }

    private static void OnProfilingRender(object? sender, EventArgs e)
    {
        if (Interlocked.CompareExchange(ref _inRender, 1, 0) != 0) return;
        try
        {
            long elapsed   = _frameSw.ElapsedTicks;
            _frameSw.Restart();
            long elapsedUs = elapsed * 1_000_000L / Stopwatch.Frequency;

            Interlocked.Add(ref _totalFrameUs, elapsedUs);
            Interlocked.Increment(ref _frameCount);

            long prevMax;
            do { prevMax = Interlocked.Read(ref _maxFrameUs); }
            while (elapsedUs > prevMax &&
                   Interlocked.CompareExchange(ref _maxFrameUs, elapsedUs, prevMax) != prevMax);

            long prevMin;
            do { prevMin = Interlocked.Read(ref _minFrameUs); }
            while ((prevMin == 0 || elapsedUs < prevMin) &&
                   Interlocked.CompareExchange(ref _minFrameUs, elapsedUs, prevMin) != prevMin);

            if (elapsedUs > 16_667) Interlocked.Increment(ref _gt16Count);
            if (elapsedUs > 33_333) Interlocked.Increment(ref _gt33Count);

            if (_scrollPhaseActive)
            {
                double idleMs = (Stopwatch.GetTimestamp() - _scrollPhaseStartTicks)
                                * 1000.0 / Stopwatch.Frequency;
                if (idleMs > 300) _scrollPhaseActive = false;
            }

            if (_scrollPhaseActive)
                Interlocked.Increment(ref _scrollFrameCount);
            else
                Interlocked.Increment(ref _idleFrameCount);

            Profiler.OnRenderReady();
        }
        finally
        {
            Interlocked.Exchange(ref _inRender, 0);
        }
    }
}

// ══════════════════════════════════════════════════════════════════
//  RAPID-INPUT BURST PROFILER  (PERFTEST only; zero prod cost)
// ══════════════════════════════════════════════════════════════════
//
//  Collects one burst's worth of wheel/animation/frame data in memory,
//  computes distributions on flush (single log write) so the hot path
//  is never perturbed by per-frame I/O.
// ══════════════════════════════════════════════════════════════════
internal sealed class BurstProfiler
{
    private const int _maxEvents = 4096;
    private readonly List<BurstEvent> _events   = new(_maxEvents);
    private readonly List<double>     _frameUs  = new(2048);
    private readonly List<long>       _frameTicks = new(2048);
    private readonly List<int>        _frameGc0 = new(2048);
    private readonly List<int>        _frameGc1 = new(2048);
    private readonly List<long>       _frameDecN = new(2048);  // UiSlowAttrib.Count snapshot per frame
    private readonly List<long>       _frameDecMs = new(2048); // UiSlowAttrib.Ms snapshot per frame
    private readonly List<double>     _offsetSamples = new(8192);
    private readonly List<double>     _stepGapMs = new(2048);
    private readonly List<double>     _stepGapPos = new(2048);
    private long _paceSkips;
    private double _tkMaxMs;
    private long   _tkCount;
    private long   _tkGt8;
    private readonly List<double> _layMs = new(2048);

    private long _burstStartTicks;
    private long _lastWheelTicks;
    private long _lastRenderTicks;
    private bool _burstActive;

    private int  _animStarts;
    private int  _animReplaced;
    private int  _animCompleted;
    private int  _animCatchUp;
    private int  _animRestarts;      // always 0: the animation clock no longer exists
    private int  _maxRunning;
    private int  _runningNow;
    private long _wheelCount;
    private long _skippedWheels;
    private double _firstMoveMs = -1.0; // wheel → first motion step (input latency)

    private sealed class BurstEvent
    {
        public long   Ticks;
        public int    Delta;
        public double CurrentOffset;
        public double TargetOffsetAccum;   // TargetOffset after clamp (this event)
        public double AnimFrom;            // Anim.From (live offset at restart)
        public double AnimTo;              // Anim.To   (target at restart)
        public bool   RestartAllowed;
        public bool   WasRunning;          // an animation clock was active before this restart
    }

    public void StartBurst()
    {
        if (_burstActive) return;
        _burstActive        = true;
        _burstStartTicks    = Stopwatch.GetTimestamp();
        _lastWheelTicks     = _burstStartTicks;
        _lastRenderTicks    = 0L;
        _events.Clear();
        _frameUs.Clear();
        _frameTicks.Clear();
        _frameGc0.Clear();
        _frameGc1.Clear();
        _frameDecN.Clear();
        _frameDecMs.Clear();
        _offsetSamples.Clear();
        _stepGapMs.Clear();
        _stepGapPos.Clear();
        _paceSkips = 0;
        _tkMaxMs = 0; _tkCount = _tkGt8 = 0;
        _layMs.Clear();
        _gc0Start = System.GC.CollectionCount(0);
        _gc1Start = System.GC.CollectionCount(1);
        _gc2Start = System.GC.CollectionCount(2);
        _animStarts = _animReplaced = _animCompleted = _animCatchUp = _animRestarts = 0;
        _maxRunning = _runningNow = 0;
        _wheelCount = _skippedWheels = 0;
        _firstMoveMs = -1.0;
    }

    // Wheel → first motion step latency for this burst: how long input waits
    // for the render pump to wake and take its first step. Diagnostic for
    // the "lag after long idle" report.
    public void MarkFirstStep(long nowTicks)
    {
        if (!_burstActive || _firstMoveMs >= 0.0) return;
        _firstMoveMs = (nowTicks - _lastWheelTicks) * 1000.0 / Stopwatch.Frequency;
    }

    public void RecordWheel(int delta, double currentOffset, double targetAccum,
        double animFrom, double animTo, bool restartAllowed, bool wasRunning)
    {
        if (!_burstActive) return;
        _lastWheelTicks = Stopwatch.GetTimestamp();
        _wheelCount++;
        if (!restartAllowed) { _skippedWheels++; }

        if (_events.Count < _maxEvents)
        {
            _events.Add(new BurstEvent
            {
                Ticks = _lastWheelTicks,
                Delta = delta,
                CurrentOffset = currentOffset,
                TargetOffsetAccum = targetAccum,
                AnimFrom = animFrom,
                AnimTo = animTo,
                RestartAllowed = restartAllowed,
                WasRunning = wasRunning,
            });
        }
    }

    public void MarkAnimationStart(int increasing)
    {
        if (!_burstActive) return;
        _animStarts++;
        _runningNow += increasing;
        if (_runningNow > _maxRunning) _maxRunning = _runningNow;
    }

    public void MarkAnimationReplaced()
    {
        if (!_burstActive) return;
        // A wheel updated the target of an already-running motion — it is NOT a
        // restart, so no new start is counted and the single flight continues.
        _animReplaced++;
    }

    public void MarkAnimationCompleted(bool catchUpRestart)
    {
        if (!_burstActive) return;
        _animCompleted++;
        if (catchUpRestart) _animCatchUp++;
        _runningNow = System.Math.Max(0, _runningNow - 1);
    }

    public void SampleOffset(double offset)
    {
        if (!_burstActive || _offsetSamples.Count >= 16384)
            return;
        _offsetSamples.Add(offset);
    }

    public void RecordStepGap(double gapMs, double posAfter)
    {
        if (!_burstActive || _stepGapMs.Count >= 2048) return;
        _stepGapMs.Add(gapMs);
        _stepGapPos.Add(posAfter);
    }

    public void MarkPaceSkip()
    {
        if (!_burstActive) return;
        _paceSkips++;
    }

    // How late the pace one-shot fired past its scheduled deadline: a large
    // value means the UI thread was blocked by other work (layout, GC, log
    // IO, callbacks) and could not run the dispatcher queue in time — the
    // step then slips by the same amount regardless of render pacing.
    public void RecordTickLag(double lagMs)
    {
        if (!_burstActive) return;
        if (lagMs < 0) lagMs = 0;
        _tkCount++;
        if (lagMs > _tkMaxMs) _tkMaxMs = lagMs;
        if (lagMs > 8.0) _tkGt8++;
    }

    private int _gc0Start;
    private int _gc1Start;
    private int _gc2Start;

    public void RecordLayout(double layMs)
    {
        if (!_burstActive || _layMs.Count >= 2048) return;
        _layMs.Add(layMs);
    }

    public void OnRenderReady()
    {
        if (!_burstActive) return;
        long nowTicks = Stopwatch.GetTimestamp();
        if (_lastRenderTicks != 0L)
        {
            double frameUs = (nowTicks - _lastRenderTicks) * 1_000_000.0 / Stopwatch.Frequency;
            if (_frameUs.Count < 8192)
            {
                _frameUs.Add(frameUs);
                _frameTicks.Add(nowTicks);
                _frameGc0.Add(System.GC.CollectionCount(0));
                _frameGc1.Add(System.GC.CollectionCount(1));
                _frameDecN.Add(System.Threading.Interlocked.Read(ref MusicPlayer.Converters.UiSlowAttrib.Count));
                _frameDecMs.Add(System.Threading.Interlocked.Read(ref MusicPlayer.Converters.UiSlowAttrib.Ms));
            }
        }
        _lastRenderTicks = nowTicks;

        double idleMs = (nowTicks - _lastWheelTicks) * 1000.0 / Stopwatch.Frequency;
        // 700ms idle threshold (not 500ms) so the constant-velocity motion
        // normally settles (≤ ~300ms) BEFORE flush — lets "completed"
        // register cleanly and still keeps 1–3s sustained bursts in a
        // single SSCB session.
        if (idleMs >= 700.0) Flush();
    }

    private void Flush()
    {
        if (!_burstActive) return;
        _burstActive = false;
        var outBuf = new System.Text.StringBuilder(4096);

        double burstMs = (_lastWheelTicks - _burstStartTicks) * 1000.0 / Stopwatch.Frequency;
        outBuf.Append("SSCB burst_ms=").Append(burstMs.ToString("0.0"));
        outBuf.Append(" wheels=").Append(_wheelCount)
              .Append(" skipped=").Append(_skippedWheels)
              .Append(" rate_s=").Append(burstMs > 0 ? (_wheelCount * 1000.0 / burstMs).ToString("0.0") : "0")
              .Append(" starts=").Append(_animStarts)
              .Append(" replaced=").Append(_animReplaced)
              .Append(" restarts=").Append(_animRestarts)
              .Append(" completed=").Append(_animCompleted)
              .Append(" catchUp=").Append(_animCatchUp)
              .Append(" maxRunning=").Append(_maxRunning);
        if (_firstMoveMs >= 0.0)
            outBuf.Append(" firstMs=").Append(_firstMoveMs.ToString("0.0"));

        // ── frame pacing distribution (from render-ready intervals) ──
        if (_frameUs.Count > 1)
        {
            var sorted = _frameUs.ToArray();
            Array.Sort(sorted);
            double sum = 0; foreach (var f in sorted) sum += f;
            double avgUs = sum / sorted.Length;
            outBuf.Append(" frames=").Append(sorted.Length)
                  .Append(" fps=").Append((1_000_000.0 / avgUs).ToString("0.0"))
                  .Append(" avgUs=").Append(avgUs.ToString("0.0"))
                  .Append(" medUs=").Append(sorted[sorted.Length / 2].ToString("0.0"))
                  .Append(" p95Us=").Append(sorted[(int)(sorted.Length * 0.95)].ToString("0.0"))
                  .Append(" p99Us=").Append(sorted[(int)(sorted.Length * 0.99)].ToString("0.0"))
                  .Append(" maxUs=").Append(sorted[sorted.Length - 1].ToString("0.0"))
                  .Append(" gt16.67=").Append(CountGe(sorted, 16_667))
                  .Append(" gt20=").Append(CountGe(sorted, 20_000))
                  .Append(" gt25=").Append(CountGe(sorted, 25_000))
                  .Append(" gt33=").Append(CountGe(sorted, 33_333));
        }

        // ── pixels/frame distribution (offset deltas per sample) ──
        if (_offsetSamples.Count > 1)
        {
            var px = new double[_offsetSamples.Count - 1];
            double sum = 0;
            for (int i = 1; i < _offsetSamples.Count; i++)
            {
                double d = Math.Abs(_offsetSamples[i] - _offsetSamples[i - 1]);
                px[i - 1] = d; sum += d;
            }
            Array.Sort(px);
            double mean = sum / px.Length;
            double varS = 0; foreach (var p in px) { double v = p - mean; varS += v * v; }
            double sd = Math.Sqrt(varS / px.Length);
            outBuf.Append(" pxFrames=").Append(px.Length)
                  .Append(" pxMean=").Append(mean.ToString("0.00"))
                  .Append(" pxMed=").Append(px[px.Length / 2].ToString("0.00"))
                  .Append(" pxSD=").Append(sd.ToString("0.00"))
                  .Append(" pxMax=").Append(px[px.Length - 1].ToString("0.00"))
                  .Append(" pxMin=").Append(px[0].ToString("0.00"))
                  .Append(" sdMean=").Append((sd / (mean > 0.01 ? mean : 1.0)).ToString("0.00"));
        }

        // ── motion step cadence (raw gaps between accepted steps) ──
        if (_stepGapMs.Count > 0)
        {
            var gaps = _stepGapMs.ToArray();
            var pos  = _stepGapPos.ToArray();
            Array.Sort(gaps, pos);
            double gsum = 0; foreach (var g in gaps) gsum += g;
            int gt25 = 0, gt40 = 0, gt60 = 0;
            foreach (var g in gaps)
            {
                if (g > 25.0) gt25++;
                if (g > 40.0) gt40++;
                if (g > 60.0) gt60++;
            }
            outBuf.Append(" stepGaps=").Append(gaps.Length)
                  .Append(" sMed=").Append(gaps[gaps.Length / 2].ToString("0.0"))
                  .Append(" sP95=").Append(gaps[(int)(gaps.Length * 0.95)].ToString("0.0"))
                  .Append(" sMax=").Append(gaps[gaps.Length - 1].ToString("0.0"))
                  .Append(" sGt25=").Append(gt25)
                  .Append(" sGt40=").Append(gt40)
                  .Append(" sGt60=").Append(gt60)
                  .Append(" sSlow@");
            int shown = 0;
            for (int i = gaps.Length - 1; i >= 0 && shown < 3; i--, shown++)
            {
                if (shown > 0) outBuf.Append(',');
                outBuf.Append(gaps[i].ToString("0")).Append(':')
                      .Append(pos[i].ToString("0"));
            }
            outBuf.Append(" paceSkips=").Append(_paceSkips)
                  .Append(" paceMult=").Append(SmoothScrollHelper.PaceMultNow.ToString("0.00"))
                  .Append(" achieved=").Append(SmoothScrollHelper.AchievedGapMs.ToString("0.0"));
        }

        // ── stall attribution ──
        // tk*  : pace one-shot lateness (UI-thread availability).
        // gcD  : Gen0 collections during the burst (allocator churn).
        // fSlow: longest render-gap frames as <us>@<rel ms since burst start>
        //        (correlate with tk/GC/wheel+SSC timestamps).
        outBuf.Append(" tkMax=").Append(_tkMaxMs.ToString("0.0"))
              .Append(" tkGt8=").Append(_tkGt8)
              .Append('/').Append(_tkCount)
              .Append(" gcD=").Append(System.GC.CollectionCount(0) - _gc0Start)
              .Append('/').Append(System.GC.CollectionCount(1) - _gc1Start)
              .Append('/').Append(System.GC.CollectionCount(2) - _gc2Start);
        if (_layMs.Count > 0)
        {
            var lay = _layMs.ToArray();
            Array.Sort(lay);
            outBuf.Append(" layN=").Append(lay.Length)
                  .Append(" layMed=").Append(lay[lay.Length / 2].ToString("0.0"))
                  .Append(" layP95=").Append(lay[(int)(lay.Length * 0.95)].ToString("0.0"))
                  .Append(" layMax=").Append(lay[lay.Length - 1].ToString("0.0"))
                  .Append(" layGt16=").Append(CountGe(lay, 16.0))
                  .Append(" layGt25=").Append(CountGe(lay, 25.0));
        }
        if (_frameUs.Count > 3)
        {
            var idx = new int[_frameUs.Count];
            for (int i = 0; i < idx.Length; i++) idx[i] = i;
            Array.Sort(idx, (a, b) => _frameUs[b].CompareTo(_frameUs[a]));
            outBuf.Append(" fSlow@");
            for (int n = 0; n < 5 && n < idx.Length; n++)
            {
                if (n > 0) outBuf.Append(',');
                int i = idx[n];
                outBuf.Append(_frameUs[i].ToString("0")).Append('@')
                      .Append(MapMs(_frameTicks[i]).ToString("0"))
                      .Append("[g")
                      .Append(i > 0 ? _frameGc0[i - 1] : _frameGc0[i]).Append(':').Append(i > 0 ? _frameGc1[i - 1] : _frameGc1[i])
                      .Append("->")
                      .Append(_frameGc0[i])
                      .Append(':')
                      .Append(_frameGc1[i])
                      .Append("d+")
                      .Append(_frameDecN[i] - (i > 0 ? _frameDecN[i - 1] : _frameDecN[i]))
                      .Append(':')
                      .Append(_frameDecMs[i] - (i > 0 ? _frameDecMs[i - 1] : _frameDecMs[i]))
                      .Append(']');
            }
        }

        // ── per-event trace (target accumulation / restart proof) ──
        outBuf.Append(" events=[");
        for (int i = 0; i < _events.Count; i++)
        {
            var e = _events[i];
            if (i > 0) outBuf.Append('|');
            outBuf.Append('t').Append(MapMs(e.Ticks))
                  .Append('d').Append(e.Delta / 120.0)
                  .Append("c").Append(e.CurrentOffset.ToString("0"))
                  .Append("a").Append(e.TargetOffsetAccum.ToString("0"))
                  .Append('[').Append(e.AnimFrom.ToString("0")).Append("->").Append(e.AnimTo.ToString("0"))
                  .Append(']');
            if (e.RestartAllowed) outBuf.Append('R'); else outBuf.Append('s');
            if (e.WasRunning) outBuf.Append('W'); else outBuf.Append('w');
        }
        outBuf.Append(']');

        MusicPlayer.Utilities.LogHelper.LogInfo(outBuf.ToString());
    }

    private static int CountGe(double[] sorted, double threshold)
    {
        int idx = Array.BinarySearch(sorted, threshold);
        if (idx < 0) idx = ~idx;
        return sorted.Length - idx;
    }

    private static double MapMs(long ticks)
    {
        return (ticks - 0) * 1000.0 / Stopwatch.Frequency;
    }
}
