# Scroll-Performance Audit — Dynamic Evidence Log

Generated during forensic audit (Phases 1/8). No production code was changed.

## Environment
- Platform: Windows, WPF .NET, `MusicPlayerFinal`, PERFTEST enabled (`MUSICPLAYER_PERFTEST=1`).
- App window 1024x650 (rect 144,24 -> 1168,674). Track list (AllSongsListBox) screen rect L=152 T=255 W=1008 H=321; injection point over list, e.g. (656,431).
- Library: 193 tracks. List virtualized (VirtualizingStackPanel, ScrollUnit=Pixel, CacheLength 2,2 Page).
- Baseline idle = CompositionTarget.Rendering firing ~60fps continuously (frames counter advances at ~180/3s in ALL runs, including at 160x28 where CPU ~0.7% — i.e. the render clock never sleeps; cost is proportional to drawn window area).

## Harness
`C:\Users\win\AppData\Local\Temp\opencode\scroll_measure3.ps1` — phases idle/scroll/burst/sustained. CPU% from TotalProcessorTime deltas, GPU-3D% via `Get-Counter "\GPU Engine(*)\Utilization Percentage"` filtered to app PID + engtype_3d, maxWS from WorkingSet64. SSC deltas read from `info_20260924.log`.

## Dynamic results

### 1024x650 (primary size)

| Scenario | CPU mean | CPU max | GPU-3D mean | GPU-3D max | GC0 (gen0 collections) | Notes |
|---|---|---|---|---|---|---|
| Clean idle (settled, no input, 30s) | 10.3% | 12.4% | 1.8% | 1.8% | +3/15s | avgUs ~16.6ms = continuous 60fps even idle; gt16 huge (~half of frames >16.67ms); single 1.9s maxUs spike at startup |
| Idle right after burst/window op | 14–25.7% | 27.2% | ~5.5–9% | 19.8% | rising | transient churn; settles back toward ~10% |
| Sustained scroll (1 notch ~every 300ms, 10s) | 16.7% | 24.4% | 1.8% | 1.8% | modest | +~165 scrollFrames; trickle is cheap once warm |
| Burst (cold-ish, 6 notches, 20:16) | 63.6% | 76.3% | 14.1% | 16% | +77 in ~12s (228->305) | scrollFrames kept climbing ≥60s after input stopped → GC/loader churn aftermath; at 1366x738 earlier burst even higher CPU |
| Burst (warm, 6 notches, 20:22) | 13.4% | 19.1% | 1.8% | 1.8% | small | warm caches drastically cut cost |
| Burst (warm, 20:35) | 23.7% | 48.8% | 1.8% | 1.8% | +10 | repeatable warm burst band 13–24% CPU mean |
| Burst (warm, 1382x754 / maximized) | 39.9% | 59% | 5.6% | 15.2% | +8 | larger visual area scales cost up |

### Window-size / minimised case
- At 160x28 (~minimised): frames still tick ~60fps but CPU drops to 0.7%, GPU 0% → render clock always active; measured CPU is dominated by paint/composition work of the visible area.

## Key observations
1. **Idle render loop never sleeps.** `CompositionTarget.Rendering` keeps firing ~60fps even with zero input, at any window size. Baseline ~10% CPU at 1024x650 is *before any scrolling*. Sources: marquee/anim-loops (`_perfRenderHandler`), visualizer render subscriber while visible, plus everlasting storyboards (LoadingOverlay pulsing dots). This is the floor the scroll is added on top of.
2. **Burst CPU spikes scale with (a) window/drawn area and (b) cold vs warm caches.** Warm burst ≈ 13–24% CPU; cold-ish burst ≈ 64% CPU mean / 76% max and GC0 +77. Difference is the album-art load/recycle + container recycling churn on scroll-into-new-region.
3. **No runaway after settling.** 30s and 60s pure no-input windows: scrollFrames delta = 0. The post-burst scrollFrames climb is bounded (~minutes) and tied to loader catch-up warmups, not an infinite loop. (Counters climb right after bursts/resizes, then flatten; that is GC + deferred loader work.)
4. **SSC frame pacing stays ~16ms (60fps) in every scenario with occasionally large maxUs spikes.** Animation is vsync-paced and not degraded; the cost shows up as CPU/GC, not dropped frames (because Continuous framerate model redraws every vsync regardless).

## Classification (evidence-backed so far)
- **A (native-vs-smooth):** NOT MEASURED dynamically — requires A/B rebuild (gated on approval).
- **B (album-art churn): STROGER evidence.** Cold burst GC0 +77 and GPU 14% vs warm 1.8% pin churn to loader/recycle during scroll into new regions.
- **C/D (binding/container overhead):** static template has multi-binding row (Background converter + album image Bind + text binds). Not isolated dynamically yet (Phase 4 gated).
- **E (feedback loop):** ruled out at rest (scrollFrames flat over pure-idle windows).
- **F (animation stacking):** cadence stays ~60fps; per-notch smooth scroll is one duration, not stacking integer multiples — but re-animation of catch-up (refactor added) doubles work on repeated input during flight. Dynamic proof pending Phase 3.
- **G (always-on rendering):** CONFIRMED — idle ~10% CPU, ~60fps frames, render clock never quiesces.
- **H (blur/wallpaper):** idle right after window ops shows transient CPU/GPU rise; Wallpaper bulb cache rebuilds on resize. Minor.
- **I (scoop/feel):** manual session pending (user).
- **J (row/layout cost):** row is 52px border w/ converter multi-binding; contribution not isolated (Phase 4).
- **K (scrollViewer churn / pixel virtualization):** virtualization is Pixel + 2,2 Page — high; not A/B measured yet.

## Next actions (require approval — code changes)
1. Phase 3: native-vs-smooth A/B (temporary diagnostic toggle build).
2. Phase 4: row-content variants (art off / bindings stripped) A/B.
3. Phase 2/9/10: instrumentation-backed cause isolation + fix proposals (NOT applied to production without approval).
4. Phase 8: remaining window-size runs (partially done).
5. Phase 11: manual feel session (user).
## POST-FIX PRODUCTION VERIFICATION (2026-09-24 ~21:15-21:30, user approved production fix)
Source: launch via dotnet MusicPlayerFinal.dll WITHOUT MUSICPLAYER_PERFTEST (production), wheel injected by scroll_measure3.

| Mode (prod, no PERFTEST) | Window | CPU mean | CPU max | GPU-3D mean | GPU-3D max | maxWS |
|---|---|---|---|---|---|---|
| Idle (settled) | 1024x650 | 0.1% | 1.1% | 0% | 0% | 292MB |
| Idle (settled, 12s) | 1024x650 | 0.9% | 3.8% | 0% | 0% | 346MB |
| Burst (6 notches) | 1024x650 | 2.5-14.5% | 11.9-28.2% | 0-0.5% | 0-0.6% | ~320MB |
| Sustained (notch/300ms) | 1024x650 | 12.2-13.3% | 29.8-47% | 0-0.3% | 0-0.9% | ~330MB |
| Sustained (early, pre-settle hit) | 1024x650 | 51% | 95.1% | 7.6% | 22.3% | 308MB |

KEY FINDING: idle WITHOUT PERFTEST = 0.1% CPU / 0% GPU and frames quiesce (SSC not present but CPU conirms no perpetual render).
The earlier ~10% idle / ~60fps continuous render floor was dominated by PERFTEST instrumentation (30s transition probe + Rendering sampler + SSC log timer), NOT the app. Evidence: same fixed build, PERFTEST=1 idle 13.1%/60fps; PERFTEST off idle 0.1%.

Fix A (stop welcome/status dot Forever storyboards on collapse) - shipped, builds 0 warn/0 err; did NOT remove the PERFTEST-mode render floor -> dots were NOT the primary idle driver; fix is still correct hygiene (removes perpetual clocks).
Fix B (AlbumArtLoader decode gate SemaphoreSlim(2,2)) - shipped; under PERFTEST warm burst on fixed build GC0 rose only +8..+16 over ~9s log windows (vs +77 cold burst before gate). Can't fully A/B cold-cache w/o separate binary; directionally good.

Sustained-early 51%/95% spike = first sustained run right after launch (library settle/art churn); settled sustained is 12.2-13.3% mean.

## POST-FIX VERIFICATION - TARGET-DRIVEN MOTION ENGINE (2026-09-24 23:06-23:11, PERFTEST=1, PID 26752 / hwnd 1771228, maximized)
Source: `burst_drive.ps1` scenarios A-H posted via PostMessage WM_MOUSEWHEEL over the track list (aim 0.35,0.35). SSCB lines from `info_20260924.log`. Idle CPU = 1.5s pre-burst sample; burst CPU = post window + 1200ms settle tail. Clean single instance (old 107.8% idle was two-instance contamination; clean idle 2-5%).

| Sc | wheels | starts | replaced | restarts | completed | maxRunning | fps | frames | p95ms | p99ms | maxUs | >16.67 | >20 | >25 | >33.33 | pxFrames | pxMean | pxMed | pxSD | sd/mean | pxMax | pxMin | CPU idle | CPU burst |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| A | 1 | 1 | 0 | 0 | 1 | 1 | 96.8 | 68 | 30.3 | 57.3 | 57.3 | 14(21%) | 8(12%) | 7(10%) | 2(3%) | 19 | 6.09 | 4.54 | 4.93 | 0.81 | 18.29 | 0.99 | 3.6% | 10% |
| B | 2 | 1 | 1 | 0 | 1 | 1 | 110.3 | 82 | 29.1 | 50.4 | 50.4 | 14(17%) | 9(11%) | 6(7%) | 1(1%) | 24 | 9.82 | 5.90 | 9.16 | 0.93 | 33.38 | 0.78 | 4.2% | 9.6% |
| C | 5 | 1 | 4 | 0 | 1 | 1 | 121.4 | 102 | 21.9 | 33.6 | 34.3 | 15(15%) | 6(6%) | 4(4%) | 2(2%) | 32 | 18.62 | 11.47 | 19.21 | 1.03 | 67.68 | 0.75 | 2.3% | 17.8% |
| D | 10 | 1 | 9 | 0 | 0 | 1 | 109.0 | 98 | 32.0 | 42.2 | 42.2 | 18(18%) | 9(9%) | 7(7%) | 4(4%) | 35 | 33.74 | 29.32 | 26.77 | 0.79 | 92.55 | 0.51 | 2.3% | 36.7% |
| E | 30 | 1 | 29 | 0 | 0 | 1 | 115.3 | 193 | 27.7 | 38.1 | 41.6 | 28(15%) | 17(9%) | 12(6%) | 3(2%) | 66 | 48.37 | 58.08 | 18.38 | 0.38 | 80.27 | 2.26 | 2.7% | 40.1% |
| F | 57/60 | 1 | 56 | 0 | 1 | 1 | 102.6 | 349 | 28.6 | 38.1 | 50.8 | 67(19%) | 43(12%) | 23(7%) | 8(2%) | 108 | 35.71 | 32.17 | 28.93 | 0.81 | 88.87 | 0.06 | 3.2% | 25.4% |
| G1 | 12 | 1 | 11 | 0 | 0 | 1 | 120.5 | 106 | 23.0 | 34.6 | 36.2 | 17(16%) | 10(9%) | 4(4%) | 2(2%) | 35 | 39.73 | 31.59 | 23.90 | 0.60 | 78.87 | 3.69 | 4.9% | 16.6% |
| G2 (mid-flight UP*3/DOWN*3) | 6 | 0 | 6 | 0 | 1 | 0 | 111.5 | 120 | 30.6 | 34.6 | 35.2 | 23(19%) | 13(11%) | 8(7%) | 3(2%) | 37 | 15.30 | 10.65 | 14.56 | 0.95 | 55.21 | 0.74 | - | - |
| H1 | 12 | 1 | 11 | 0 | 0 | 1 | 116.8 | 107 | 23.9 | 31.2 | 41.1 | 16(15%) | 11(10%) | 5(5%) | 1(1%) | 37 | 38.06 | 31.87 | 26.48 | 0.70 | 92.37 | 0.56 | 3.8% | 20.1% |
| H2 (rapid DOWN*3->UP*3, 94ms) | 6 | 0 | 6 | 0 | 1 | 0 | 63.1 | 51 | 33.9 | 48.6 | 48.6 | 8(16%) | 8(16%) | 8(16%) | 4(8%) | 3 | 41.62 | 43.52 | 15.50 | 0.37 | 59.58 | 21.75 | - | - |
| Pre-scroll UP*30 reversal | 30 | 1 | 29 | 0 | 0 | 1 | 114.9 | 187 | 27.5 | 32.6 | 36.6 | 38(20%) | 16(9%) | 10(5%) | 1(1%) | 65 | 48.47 | 47.48 | 23.03 | 0.48 | 82.25 | 2.32 | - | - |
| Real-user ambients (23:04:30,23:04:32) | 9,14 | 1 | 13 | 0 | 1 | 1 | 114.9 | 139 | - | - | - | - | - | - | - | 49,55 | 29.2,26.1 | - | 0.85,0.92 | 83.4,91.0 | - | - | - |

KEY RESULTS:
- starts=1 per sustained burst; every extra wheel = replaced (target update) ONLY; restarts=0 everywhere (old build counted restart-per-wheel). maxRunning=1: single motion surface per ScrollViewer.
- Smoothness: sustained-scroll sd/mean 0.38-0.81 (E=0.38 at 30 wheels) vs old build 1.0-1.65. Velocity clamped at 2000px/s (pxMax ~80-92 = 2000px/s * starved 16ms DispatcherTimer tick up to MaxStep 50ms under layout load; rate is flat, no per-wheel easing).
- No overshoot: F hit list extent 9621 and crept 9610->9620 then settled exactly at extent; H rapid reversal 9261->8901 monotonic. Arrival guard prevents Position crossing Target.
- First frame responsive: wheels arrive => inline StepState on first wheel (A shows immediate 0->120 motion).
- Boundary: F last 3 of 60 wheels dropped by CanScrollVertical=false at extent (correct: stop at end, no spring past edge).
- G2/H2 `starts=0 maxRunning=0` = reversal wheels that arrived mid-flight (motion begun in prior burst window) - continuation, NOT restarts; completed=1 confirms settle registered with SettleOffsetPx=4.0 < 700ms flush.
- CPU: idle 2.3-4.9% (PERFTEST on; prod idle 0.1%). Burst incl settle 9.6-40.1% scaled to demand (A<B<C<...<E=40%; sustained 1s at max velocity is the heaviest). Frame pacing worst single tick 57ms (A first-wheel handle+layout); p95 22-34ms, p99 31-57ms; >16.67ms 14-21% of profiler frames (vsync 60Hz render; PERFTEST samples per composition frame incl. layout-stuffed ticks).
- Build at time of run: 0 errors / 0 warnings.
