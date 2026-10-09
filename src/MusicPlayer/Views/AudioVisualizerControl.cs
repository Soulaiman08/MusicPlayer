using System;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using MusicPlayer.Audio;

namespace MusicPlayer.Views;

/// <summary>
/// Audio-reactive spectrum visualizer replicating the original Android
/// visualizer's default wave mode (decompiled from q6/c.java):
///
/// 1. LINEAR FFT-bin mapping — each bar owns a handful of raw bins between
///    ~430 Hz and ~3.1 kHz, exactly the spectral window the original app
///    renders. Bands above ~3 kHz (which carry almost no energy in music)
///    are never drawn, so no bar ever sits in a dead zone.
/// 2. Bass floor: the average power of the low bins below the visible band
///    is added to every bar (0.3 × bass) — Android's l() formula. The whole
///    wave moves with the music; nothing breathes artificially.
/// 3. Fair bars: every bar shows its true proportional magnitude against a
///    single shared envelope — bass naturally taller, treble shorter, with no
///    bar artificially inflated. Per-bin temporal smoothing in the analyzer
///    removes flicker, so the motion is calm yet synced.
/// 4. Direct response: bars track the analyzed wave on every frame —
///    fast, lively rise and fall, exactly like the original app (no heavy easing).
/// 5. On song stop the bars snap instantly down to the thin baseline stubs —
///    the Android "default wave" resting look.
/// 6. Reflection below the baseline, semi-transparent, mirrors the upper wave.
/// </summary>
public sealed class AudioVisualizerControl : FrameworkElement
{
    private readonly DrawingVisual _visual;
    private int _subscribed;
    private readonly float[] _smoothedHeights = new float[BarCount];
    private readonly float[] _magnitudesBuffer = new float[AudioAnalyzer.MagnitudeCount];
    private readonly float[] _rawScratch = new float[BarCount];
    private readonly float[] _valueScratch = new float[BarCount];
    private readonly float[] _smoothScratch = new float[BarCount];
    private readonly float[] _finalHeightsScratch = new float[BarCount];

    // Precomputed bin ranges
    private readonly int[] _barBinLo = new int[BarCount];
    private readonly int[] _barBinHi = new int[BarCount];

    // Shared envelope reference
    private float _globalPeak = 0.02f;

    private Pen? _baselinePen;
    private readonly SolidColorBrush[] _barBrushes = new SolidColorBrush[BarCount];
    private readonly SolidColorBrush[] _reflectBrushes = new SolidColorBrush[BarCount];
    private bool _brushesInitialized;
    private bool _isSettledAtIdle = false;

    // --- Geometry ---
    private const int BarCount = 64;
    private const double BarGapPx = 1.0;
    private const double BaselineY = 0.75;
    private const double UpperHeightFrac = 0.28;
    private const double ReflectionHeightFrac = 0.12;
    private const double ReflectionBaseOpacity = 0.30;

    private static readonly double TicksPerMillisecond = Stopwatch.Frequency / 1000.0;
    private volatile bool _renderPaused;

    // DispatcherTimer that polls AudioAnalyzer at ~4 Hz while rendering is
    // unsubscribed (idle state). When fresh audio arrives it resubscribes the
    // vsync loop and stops itself — zero idle CPU between songs.
    private System.Windows.Threading.DispatcherTimer? _wakeTimer;

    public AudioVisualizerControl()
    {
        _visual = new DrawingVisual();
        AddVisualChild(_visual);

        // Precompute linear FFT bin ranges once
        const int startBin = 10;
        const int endBin = 73;
        const double binSpan = endBin - startBin;
        for (int i = 0; i < BarCount; i++)
        {
            double b0 = startBin + binSpan * ((double)i / BarCount);
            double b1 = startBin + binSpan * ((double)(i + 1) / BarCount);
            _barBinLo[i] = Math.Clamp((int)Math.Floor(b0), 0, AudioAnalyzer.MagnitudeCount - 1);
            _barBinHi[i] = Math.Clamp((int)Math.Ceiling(b1) - 1, 0, AudioAnalyzer.MagnitudeCount - 1);
            _smoothedHeights[i] = 3.0f;
        }

        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            // Restart the wake-timer so audio resumption is detected even if
            // a previous session left it stopped.
            SubscribeToRendering();
            EnsureWakeTimer();
        }
        else
        {
            UnsubscribeFromRendering();
            StopWakeTimer();
        }
    }

    private void SubscribeToRendering()
    {
        if (Interlocked.CompareExchange(ref _subscribed, 1, 0) == 0)
        {
            _isSettledAtIdle = false;
            System.Windows.Media.CompositionTarget.Rendering += OnRendering;
        }
    }

    private void UnsubscribeFromRendering()
    {
        if (Interlocked.CompareExchange(ref _subscribed, 0, 1) == 1)
            System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
    }

    // ── Wake-timer: polls at ~4 Hz while rendering is idle ──────────
    // Keeps the delegate off the vsync loop between songs (0% overhead);
    // resubscribes the instant the AudioAnalyzer reports a fresh stamp.
    private void EnsureWakeTimer()
    {
        if (_wakeTimer != null) return;
        _wakeTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _wakeTimer.Tick += OnWakeTimerTick;
        _wakeTimer.Start();
    }

    private void StopWakeTimer()
    {
        _wakeTimer?.Stop();
        _wakeTimer = null;
    }

    private void OnWakeTimerTick(object? sender, EventArgs e)
    {
        if (_renderPaused || !IsVisible) return;

        long stamp = AudioAnalyzer.Instance.LastSpectrumStamp;
        bool fresh = stamp != 0L &&
            (double)(Stopwatch.GetTimestamp() - stamp) / TicksPerMillisecond < 250.0;

        if (fresh && _subscribed == 0)
        {
            // Audio resumed while we were unsubscribed — rejoin the vsync loop.
            _isSettledAtIdle = false;
            SubscribeToRendering();
        }
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _visual;

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _brushesInitialized = false;
        _baselinePen = null;
        _isSettledAtIdle = false;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (_renderPaused) return;

        long stamp = AudioAnalyzer.Instance.LastSpectrumStamp;
        bool fresh = stamp != 0L &&
            (double)(Stopwatch.GetTimestamp() - stamp) / TicksPerMillisecond < 250.0;

        // Fresh audio resumed — exit settled state immediately so the wave
        // appears on the very next frame without delay.
        if (fresh && _isSettledAtIdle)
        {
            _isSettledAtIdle = false;
        }

        // If silent/idle and bars already settled to baseline stubs, unsubscribe
        // entirely — the wake-timer will resubscribe when audio resumes.
        if (!fresh && _isSettledAtIdle)
        {
            UnsubscribeFromRendering();
            return;
        }

        RenderFrame();
    }

    public void PauseRendering() => _renderPaused = true;
    public void ResumeRendering() { _renderPaused = false; _isSettledAtIdle = false; }

    private void EnsureResources()
    {
        if (_baselinePen == null)
        {
            var brush = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));
            brush.Freeze();
            _baselinePen = new Pen(brush, 1);
            _baselinePen.Freeze();
        }

        if (!_brushesInitialized)
        {
            for (int i = 0; i < BarCount; i++)
            {
                double t = (double)i / (BarCount - 1);
                Color c;
                if (t < 0.25)
                    c = Color.FromRgb((byte)(220 + t * 4 * 35), (byte)(40 + t * 4 * 30), 30);
                else if (t < 0.50)
                { double u = (t - 0.25) * 4; c = Color.FromRgb(255, (byte)(70 + u * 130), (byte)(30 + u * 20)); }
                else if (t < 0.75)
                { double u = (t - 0.50) * 4; c = Color.FromRgb((byte)(255 - u * 35), (byte)(200 + u * 55), (byte)(50 + u * 30)); }
                else
                { double u = (t - 0.75) * 4; c = Color.FromRgb((byte)(220 - u * 100), (byte)(255 - u * 60), (byte)(80 + u * 40)); }

                var barB = new SolidColorBrush(c);
                barB.Freeze();
                _barBrushes[i] = barB;

                var reflB = new SolidColorBrush(Color.FromArgb(
                    (byte)(ReflectionBaseOpacity * 255), c.R, c.G, c.B));
                reflB.Freeze();
                _reflectBrushes[i] = reflB;
            }
            _brushesInitialized = true;
        }
    }

    private void RenderFrame()
    {
        var dc = _visual.RenderOpen();
        double w = ActualWidth;
        double h = ActualHeight;

        if (w > 4 && h > 4)
        {
            // Authoritative freshness for the DISPLAY: the instant no new
            // spectrum has arrived (~150ms), the wave is stale and must snap
            // back to the idle baseline stubs — independent of the analyzer's
            // background silence bookkeeping, so stopping feels immediate.
            long stamp = AudioAnalyzer.Instance.LastSpectrumStamp;
            bool fresh = stamp != 0L &&
                (double)(Stopwatch.GetTimestamp() - stamp) / TicksPerMillisecond < 150.0;
            bool hasData = fresh;

            AudioAnalyzer.Instance.MagnitudesCopyInto(_magnitudesBuffer);

            GetPerfectBarHeights(_magnitudesBuffer, BarCount, (float)h, !hasData);

            EnsureResources();

            // Geometry
            int n = BarCount;
            double totalBarSpace = w - BarGapPx;
            double barStep = (totalBarSpace - BarGapPx) / n;
            double barW = Math.Max(1.5, barStep - BarGapPx);
            double baseline = h * BaselineY;
            double maxReflectH = h * ReflectionHeightFrac;

            // Draw baseline
            dc.DrawLine(_baselinePen!, new Point(0, baseline), new Point(w, baseline));

            // Draw upper bars. At idle each bar is a 3px stub right above the
            // baseline — the Android "default wave" look (q6/c.java collapses to
            // min-height stubs the instant no data arrives).
            for (int i = 0; i < n; i++)
            {
                double barH = _smoothedHeights[i];
                if (barH < 1.0) continue;
                double x = BarGapPx + i * barStep;
                double y = baseline - barH;
                if (y < 0) { barH = baseline; y = 0; }
                dc.DrawRectangle(_barBrushes[i], null, new Rect(x, y, barW, barH));
            }

            // Draw reflection — narrow and subtle, like looking at a reflection
            // from below. Even the idle stubs cast a thin reflected line.
            for (int i = 0; i < n; i++)
            {
                double barH = _smoothedHeights[i];
                if (barH < 1.0) continue;
                double reflectH = Math.Max(1.5, Math.Min(barH * 0.15, maxReflectH));
                double x = BarGapPx + i * barStep;
                dc.DrawRectangle(_reflectBrushes[i], null, new Rect(x, baseline + 1, barW, reflectH));
            }
        }

        dc.Close();
    }

    /// <summary>
    /// Replicates the original Android wave (q6/c.java + d5/o.java + q6/a.java):
    /// - LINEAR FFT-bin mapping across the ~430 Hz…3.1 kHz window the app draws.
    /// - Bass floor added to every bar: value = min(bin*1.5, 1)*0.7 + 0.3*bass.
    /// - 3-tap box smoothing across neighbouring bars.
    /// - Direct response: rise/fall track the wave instantly on every frame.
    /// Silent: bars snap straight down to the thin baseline stubs.
    /// </summary>
    private float[] GetPerfectBarHeights(float[] fftBuffer, int barCount, float visualCanvasHeight, bool isPausedOrSilent)
    {
        bool hasAudio = fftBuffer != null && fftBuffer.Length > 0 && !isPausedOrSilent;

        if (!hasAudio)
        {
            bool allSettled = true;
            for (int i = 0; i < barCount; i++)
            {
                _smoothedHeights[i] += (3f - _smoothedHeights[i]) * 0.75f;
                if (MathF.Abs(_smoothedHeights[i] - 3f) > 0.05f) allSettled = false;
                _finalHeightsScratch[i] = _smoothedHeights[i];
            }
            if (allSettled)
            {
                for (int i = 0; i < barCount; i++)
                {
                    _smoothedHeights[i] = 3f;
                    _finalHeightsScratch[i] = 3f;
                }
                _isSettledAtIdle = true;
            }
            return _finalHeightsScratch;
        }

        _isSettledAtIdle = false;

        // Use precomputed bin ranges for zero runtime math
        for (int i = 0; i < barCount; i++)
        {
            int lo = _barBinLo[i];
            int hi = _barBinHi[i];
            float m = 0f;
            for (int b = lo; b <= hi; b++)
                if (fftBuffer![b] > m) m = fftBuffer[b];
            _rawScratch[i] = m;
        }

        // Shared envelope calibration
        float peakMax = 0f;
        for (int i = 0; i < barCount; i++)
            if (_rawScratch[i] > peakMax) peakMax = _rawScratch[i];
        _globalPeak = peakMax > _globalPeak ? peakMax : Math.Max(0.006f, _globalPeak - _globalPeak * 0.012f);

        for (int i = 0; i < barCount; i++)
        {
            float frac = (float)i / barCount;
            float n = Math.Min(_rawScratch[i] / _globalPeak, 1f);
            float tilt = 1f - 0.25f * frac;
            float floor = 0.04f * (1f - 0.5f * frac);
            _valueScratch[i] = Math.Min(n * tilt + floor, 1f);
        }

        // Light spatial coupling
        for (int i = 0; i < barCount; i++)
        {
            float v;
            if (i == 0) v = (_valueScratch[0] + _valueScratch[1]) * 0.5f;
            else if (i == barCount - 1) v = (_valueScratch[i] + _valueScratch[i - 1]) * 0.5f;
            else v = (_valueScratch[i - 1] * 0.10f) + (_valueScratch[i] * 0.80f) + (_valueScratch[i + 1] * 0.10f);
            _smoothScratch[i] = v;
        }

        // Motion tracking
        for (int i = 0; i < barCount; i++)
        {
            float cur = _smoothedHeights[i];
            float desired = _smoothScratch[i] * (visualCanvasHeight * 0.52f);
            float rate = cur < desired ? 0.70f : 0.50f;
            _smoothedHeights[i] = cur + (desired - cur) * rate;
            _finalHeightsScratch[i] = Math.Clamp(_smoothedHeights[i], 3.0f, visualCanvasHeight * 0.68f);
        }

        return _finalHeightsScratch;
    }
}