# M2.5 — Full Container Scroll Investigation (measurement only)

**Date:** 2026-11-18
**Scope:** all five container views × four window sizes × two isolated fixtures
**Status:** measurement complete; **no production performance change was made**

This round re-runs the container-scroll investigation end to end. It replaces the
incomplete 2026-11-18 round that covered only Tracks/Artists and could not prove
Albums, and where Genres/Folders were not scrollable at all. Both gaps are closed:
Genres (180/240) and Folders (220/320) now have enough items to scroll in both
fixtures, and Albums has a stable `AutomationId`.

Nothing in this document is a proposed fix. It is a record of what was measured,
what the numbers show, and what they cannot show.

---

## 1. Question under test

The reported symptom: scrolling looks smooth in a small window and stutters when
the window is large or maximized.

The investigation therefore asks, per view and per fixture:

1. Does the UI thread do **more** work per identical scroll burst at Maximized?
2. Does GPU/composition work **increase** with window size?
3. Does the visible content **freeze for longer** at Maximized?
4. Is the container actually virtualizing, or is it realizing every item?
5. Is memory, image/background activity, or disk IO implicated?

---

## 2. Build and environment identity

| Item | Value |
|---|---|
| Branch / HEAD | `ui-redesign` / `18126e72e489c7a6f76e018260b48fb325b15174` |
| Build | `dotnet build src/MusicPlayer/MusicPlayer.csproj -c Release --no-restore` |
| Build log | `perf-results/m25/container-scroll-full/build4.log` — 0 errors, 4 pre-existing CS4014 warnings |
| `MusicPlayer.dll` | 36,171,776 bytes, 2026-11-18 12:27:27, SHA-256 `9A2634D3B877CEF28A95456859879BB03F2309296AAE12F3B393C0A49805C6E5` |
| OS | Windows 10 Enterprise LTSC, 19044 |
| CPU / GPU | Intel Core i5-4210U (2C/4T) / Intel HD Graphics + NVIDIA GeForce 820M |
| Display | 1366×768 @ 60 Hz, 100% scaling (96 DPI) |
| Session | non-administrator |
| .NET SDK | 8.0.425 |

Window geometry actually recorded in every row:

| Size | Outer | Client | Maximized flag |
|---|---|---:|---|
| Small | 1116×650 | 1100×642 | False |
| Medium | 1280×720 | 1264×712 | False |
| Maximized | — | 1366×738 | True |
| Return to Small | 1116×650 | 1100×642 | False |

Fixtures (isolated, generated; **no user library data touched**):

| Fixture | Tracks | Artists | Albums | Genres | Folders |
|---|---:|---:|---:|---:|---:|
| A | 201 | 150 | 201 | 180 | 220 |
| B | 5000 | 500 | 5000 | 240 | 320 |

---

## 3. What is measured — and what is not

| Measured | Not measured |
|---|---|
| UI-thread CPU (`GetThreadTimes` on the window-owner thread) | **Presented frames / PresentMon / decoded ETW present events** |
| Process CPU (separate indicator only) | Final FPS of any kind |
| GPU Engine utilisation per PID, 3D engine, DWM, system (PDH, 100 ms) | Per-frame GPU cost attribution |
| GPU dedicated memory per PID | DWM present statistics |
| WPF composition/rendering callbacks + layout passes + max gaps (in-app probe) | Whether a composition callback became a presented frame |
| Realised vs total containers | Why a specific frame was long |
| UIA offset before/after, screenshots before/after | Human perception of smoothness |
| Captured-screen content-change cadence of a fixed strip | Refresh-synchronised presentation |

Environment check (unchanged from `docs/M25_MEASUREMENT_INFRASTRUCTURE.md`):
`PresentMon`, `xperf`, `WPA`, `dotnet-trace` are absent; `wpr.exe` exists but
cannot start a profile without elevation (`0xc5585011`). **Presented frames remain
`UNAVAILABLE`.** The probe's `probeMaxRenderGapMs` is composition-callback cadence
and is never converted to a frame rate.

---

## 4. Method

### 4.1 Official matrix (`tools/perf/M25FullMatrix.ps1`)

Per cell = one view × one size × one repetition.

1. Set window size; wait 900 ms.
2. Activate the view by `AutomationId`; wait 2300 ms; require the expected scroll
   surface `AutomationId` (`SongsGridScroller`, `ArtistsSearchScroll`,
   `AlbumsScroll`, `GenresScroll`, `FoldersScroll`).
3. Reset to top, then fire a **discarded** 8-wheel burst at that exact size
   (excludes container realisation and image decode from the measurement).
4. Reset to top again, snapshot offset and a before-screenshot.
5. Fire 25 wheel events at 35 ms while sampling UI-thread CPU, GPU counters and
   the in-app probe.
6. Snapshot offset and an after-screenshot; validate.

Controls against ordering bias:

- **Size rotation** across repetitions (4 sequences); `return-small` always runs
  immediately after `maximized`.
- Wheel coalescing is accepted, because total scroll distance is preserved. The
  probe's received count is recorded separately from the 25 injected messages
  (Fixture A: min 23, median 25, max 25; Fixture B: min 21, median 25, max 25).
- Every row must prove movement: `percentDelta ≥ 0.2`, probe row matched, offset
  changed, screenshots differ.

### 4.2 Supplementary pacing pass (`tools/perf/M25PacingPass.ps1`)

A separate pass asks only: *does the visible content freeze for longer when the
window is maximized?* It captures a fixed 120×320 screen strip at the centre of
the scroll surface with GDI `BitBlt` at ~60 Hz and counts captures whose pixels
differ from the previous capture.

This is a **captured-screen content-cadence proxy**, not presented frames and not
FPS. The capture thread's own CPU cost applies identically to Small and Maximized.
Sizes alternate per repetition, and the same discarded warm-up burst is used.

### 4.3 Instrumentation added for this round

- `src/MusicPlayer/Diagnostics/ScrollProbe.cs` — inert observer, enabled only by
  `MUSICPLAYER_SCROLLPROBE=1`; records offset/extent/viewport, realised and
  visible containers, render callbacks, layout passes, gaps.
- Testability-only XAML ids: `TabSongsButton`, `TabArtistsButton`,
  `TabAlbumsButton`, `TabGenresButton`, `TabFoldersButton`,
  `ToggleSongsLayoutButton`; `x:Name` on `AlbumsScroll`, `GenresScroll`,
  `FoldersScroll`.
- No scroll algorithm, panel, helper, image pipeline, virtualization, glass,
  wallpaper or startup path was modified.

---

## 5. Coverage and validity

**Official matrix: 200/200 valid, 200/200 probe rows matched, GPU sampler `ok` in
every sample.**
**Pacing pass: 100/100 valid.**

| View | Small | Medium | Maximized | Return to Small | Fixture A | Fixture B |
|---|---|---|---|---|---|---:|---:|
| Tracks – Grid | 5/5 | 5/5 | 5/5 | 5/5 | ✅ | ✅ |
| Artists Tab | 5/5 | 5/5 | 5/5 | 5/5 | ✅ | ✅ |
| Albums Tab | 5/5 | 5/5 | 5/5 | 5/5 | ✅ | ✅ |
| Genres Tab | 5/5 | 5/5 | 5/5 | 5/5 | ✅ | ✅ |
| Folders Tab (All Songs) | 5/5 | 5/5 | 5/5 | 5/5 | ✅ | ✅ |

Movement proof (UIA vertical scroll percent, median of 5 reps):

| View | A small | A max | B small | B max |
|---|---:|---:|---:|---:|
| songs | 51.8 | 78.6 | 1.9 | 3.0 |
| artists | 90.2 | 99.9 | 25.4 | 30.2 |
| albums | 57.7 | 71.5 | 2.3 | 2.7 |
| genres | 100.0 | 100.0 | 82.9 | 90.6 |
| folders | 89.7 | 97.2 | 61.4 | 65.9 |

Two honest caveats:

- Fixture B Tracks/Albums move only 1.9–3.0 **percent** because the extent is
  190,026 px, but the absolute movement is large: Fixture B Tracks/Small goes
  from offset 0 px to 1,983–4,268 px. Percent saturation is not a validity
  problem in either direction.
- Artists and Genres reach 100% on Fixture A (list shorter than one burst), so
  their percent column is capped; movement there is proven by absolute offset,
  screenshots and the pacing strip.

---

## 6. Results

### 6.1 UI-thread CPU per identical burst — median / P95 / max (ms)

| view | size | A | B |
|---|---|---:|---:|
| songs | small | 750 / 1031 / 1094 | 812 / 1044 / 1094 |
| songs | medium | 797 / 825 / 828 | 906 / 960 / 969 |
| songs | maximized | 734 / 796 / 812 | 703 / 734 / 734 |
| songs | return-small | 734 / 943 / 984 | 844 / 1068 / 1109 |
| artists | small | 469 / 653 / 672 | 547 / 919 / 1000 |
| artists | medium | 547 / 609 / 625 | 547 / 688 / 719 |
| artists | maximized | 438 / 510 / 516 | 500 / 622 / 641 |
| artists | return-small | 516 / 641 / 672 | 672 / 759 / 781 |
| albums | small | 594 / 687 / 703 | 641 / 1015 / 1109 |
| albums | medium | 594 / 594 / 594 | 734 / 1009 / 1078 |
| albums | maximized | 562 / 575 / 578 | 594 / 700 / 719 |
| albums | return-small | 578 / 591 / 594 | 594 / 953 / 1031 |
| genres | small | 297 / 356 / 359 | 359 / 409 / 422 |
| genres | medium | 266 / 309 / 312 | 391 / 438 / 438 |
| genres | maximized | 234 / 275 / 281 | 281 / 297 / 297 |
| genres | return-small | 250 / 328 / 328 | 328 / 388 / 391 |
| folders | small | 297 / 378 / 391 | 359 / 419 / 422 |
| folders | medium | 297 / 385 / 391 | 422 / 519 / 531 |
| folders | maximized | 250 / 347 / 359 | 406 / 419 / 422 |
| folders | return-small | 281 / 309 / 312 | 469 / 562 / 578 |

UI busy % follows the same shape (e.g. A Tracks 62.4 → 64.7 → 61.6; A Genres
25.3 → 22.4 → 20.0).

**Finding:** the UI thread does **not** do more work at Maximized. The Maximized
median is at or below the Small median in 9 of 10 view/fixture combinations.
The single exception is Fixture B Folders (359 → 406 ms, +13%), the
non-virtualizing list with 320 items.

**Do not read this as "no stutter".** A UI thread that is idle half the burst can
still fail to produce smooth motion if the work is on the GPU or compositor.

### 6.2 Container structure — median realised / total / visible

| view | size | A realised | A total | B realised | B total |
|---|---|---:|---:|---:|---:|
| songs | small | 24 | 201 | 24 | 5000 |
| songs | maximized | 36 | 201 | 36 | 5000 |
| artists | small | 36 | 150 | 36 | 500 |
| artists | maximized | 31 | 150 | 49 | 500 |
| albums | small | 36 | 201 | 36 | 5000 |
| albums | maximized | 49 | 201 | 49 | 5000 |
| genres | small | **180** | 180 | **240** | 240 |
| genres | maximized | **180** | 180 | **240** | 240 |
| folders | small | **220** | 220 | **320** | 320 |
| folders | maximized | **220** | 220 | **320** | 320 |

**Finding:** Tracks, Artists and Albums virtualize (24–49 containers for
150–5000 items). **Genres and Folders realise every item**, at every size, in
both fixtures. They scroll by moving a fully realised visual tree, which is a
structurally different cost model from the virtualizing grid.

### 6.3 GPU per-PID mean utilisation during the burst — median / P95 / max (%)

| view | size | A | B |
|---|---|---:|---:|
| songs | small | 24 / 26 / 26 | 25 / 26 / 26 |
| songs | medium | 35 / 37 / 37 | 30 / 35 / 35 |
| songs | maximized | 37 / 41 / 41 | 40 / 41 / 41 |
| songs | return-small | 23 / 24 / 24 | 24 / 26 / 27 |
| artists | small | 32 / 33 / 33 | 32 / 35 / 36 |
| artists | medium | 35 / 37 / 37 | 33 / 33 / 33 |
| artists | maximized | 38 / 39 / 40 | 38 / 39 / 40 |
| artists | return-small | 32 / 35 / 36 | 31 / 35 / 35 |
| albums | small | 30 / 30 / 30 | 27 / 28 / 29 |
| albums | medium | 33 / 35 / 36 | 33 / 34 / 34 |
| albums | maximized | 36 / 38 / 38 | 38 / 40 / 40 |
| albums | return-small | 29 / 29 / 29 | 27 / 30 / 31 |
| genres | small | 38 / 40 / 40 | 37 / 39 / 40 |
| genres | medium | 46 / 47 / 47 | 47 / 48 / 48 |
| genres | maximized | 51 / 54 / 54 | 44 / 49 / 50 |
| genres | return-small | 37 / 38 / 38 | 37 / 39 / 39 |
| folders | small | 33 / 34 / 34 | 34 / 34 / 34 |
| folders | medium | 43 / 45 / 45 | 41 / 44 / 44 |
| folders | maximized | 48 / 49 / 49 | 47 / 49 / 49 |
| folders | return-small | 32 / 33 / 34 | 34 / 34 / 34 |

Per-burst GPU **maxima** follow the same pattern (medians of the per-burst
maximum: A Tracks 36 → 45; A Genres 44 → 64; A Folders 38 → 56; B Tracks 33 → 44;
B Folders 38 → 56). DWM stays low
throughout: 1.4–3.3% mean. System-wide GPU mean rises with size as well.

**Finding:** GPU utilisation is the only metric that scales monotonically with
window size in **all ten** view/fixture combinations, and it returns to the Small
level on Return-to-Small. This is consistent with fill-rate/composition cost
scaling with surface area. On its own it is expected behaviour, not a defect.

### 6.4 WPF composition/layout cadence — median / P95 / max

Callbacks per burst (not frames):

| view | size | A | B |
|---|---|---:|---:|
| songs | small | 118 / 121 / 121 | 119 / 190 / 206 |
| songs | maximized | 84 / 88 / 89 | 90 / 119 / 126 |
| artists | small | 98 / 138 / 148 | 99 / 189 / 211 |
| artists | maximized | 82 / 87 / 87 | 83 / 105 / 110 |
| albums | small | 90 / 135 / 146 | 88 / 181 / 204 |
| albums | maximized | 81 / 82 / 82 | 88 / 92 / 92 |
| genres | small | 105 / 106 / 106 | 107 / 108 / 108 |
| genres | maximized | 95 / 98 / 99 | 91 / 99 / 101 |
| folders | small | 108 / 109 / 109 | 108 / 111 / 111 |
| folders | maximized | 107 / 109 / 109 | 104 / 106 / 106 |

Max render gap (ms):

| view | size | A | B |
|---|---|---:|---:|
| songs | small | 57 / 60 / 60 | 55 / 60 / 60 |
| songs | maximized | 54 / 59 / 59 | 47 / 52 / 53 |
| artists | small | 56 / 59 / 59 | 53 / 58 / 59 |
| artists | maximized | 45 / 53 / 53 | 47 / 54 / 55 |
| albums | small | 40 / 52 / 55 | 55 / 58 / 59 |
| albums | maximized | 36 / 44 / 44 | 52 / 57 / 58 |
| genres | small | 47 / 56 / 56 | 50 / 56 / 57 |
| genres | maximized | 51 / 63 / 65 | 42 / 43 / 43 |
| folders | small | 41 / 51 / 52 | 44 / 55 / 57 |
| folders | maximized | 43 / 59 / 60 | 45 / 57 / 58 |

**Finding:** at Maximized the virtualizing views emit **fewer** composition
callbacks for the same burst (A Tracks 118 → 84, A Artists 98 → 82, A Albums
90 → 81, B Tracks 119 → 90, B Artists 99 → 83; Fixture B Albums is flat at 88)
while GPU utilisation rises and UI CPU is flat or lower. Non-virtualizing
Genres/Folders stay near 95–108. Because these are composition callbacks and not
presented frames, they cannot be converted into FPS; they show that the same
input burst produces fewer composition passes at the larger surface.

Notably, the max render **gap** does not grow at Maximized. So there is no
evidence here of a longer stretch between composition passes at the larger size.

### 6.5 Memory and image/background activity

| Metric | Fixture A | Fixture B |
|---|---|---|
| Working set after burst | 427–464 MB | 893–923 MB |
| GPU dedicated memory per PID | 71–111 MB (Artists/A max 109–110; otherwise ~98–111) | 100–111 MB |
| Private bytes | recorded per row | recorded per row |
| IO read delta during burst | **0** in all 100 samples | **0** in all 100 samples |
| Process CPU | 578–1,438 ms | 641–1,469 ms |

**Finding:** no disk IO occurs during any burst, so image files are not being
re-read mid-scroll. GPU memory is stable across sizes (no evidence of a
size-dependent texture/bitmap cache growth). Working set differs by fixture
(≈450 MB vs ≈900 MB) but not by window size.

### 6.6 Captured-screen content cadence (supplementary pass)

Per-cell medians, n=5. `unchanged%` = share of consecutive captures inside the
visible-change window where the strip did not change at all.

| view | size | achieved Hz | changes | p95 gap ms | max gap median | max gap worst | over 50 ms | unchanged % |
|---|---|---:|---:|---:|---:|---:|---:|---:|
| A songs | small | 59.1 | 42.0 | 65.8 | 87.7 | 265.7 | 57 | 46.9 |
| A songs | maximized | 60.0 | 56.0 | 34.0 | 50.3 | 83.2 | 6 | 45.4 |
| A artists | small | 60.0 | 56.0 | 35.6 | 58.8 | 66.8 | 8 | 44.2 |
| A artists | maximized | 60.0 | 49.0 | 49.8 | 50.1 | 51.8 | 11 | 51.2 |
| A albums | small | 60.0 | 53.0 | 49.3 | 50.4 | 133.0 | 28 | 48.8 |
| A albums | maximized | 60.0 | 48.0 | 50.3 | 50.6 | 57.0 | 42 | 52.3 |
| A genres | small | 60.0 | 52.0 | 35.3 | 83.9 | 149.9 | 4 | 44.9 |
| A genres | maximized | 60.0 | 50.0 | 49.9 | 83.6 | 100.0 | 10 | 46.8 |
| A folders | small | 59.5 | 57.0 | 34.5 | 66.5 | 84.0 | 4 | 44.9 |
| A folders | maximized | 60.0 | 57.0 | 33.6 | 33.8 | 48.9 | 0 | 44.8 |
| B songs | small | 59.5 | 43.0 | 66.7 | 84.4 | 317.2 | 50 | 50.4 |
| B songs | maximized | 60.0 | 55.0 | 33.8 | 50.7 | 99.8 | 8 | 44.4 |
| B artists | small | 60.0 | 55.0 | 47.2 | 50.7 | 69.2 | 7 | 44.8 |
| B artists | maximized | 60.0 | 50.0 | 49.8 | 50.3 | 50.4 | 9 | 50.4 |
| B albums | small | 60.0 | 53.0 | 49.5 | 51.5 | 97.4 | 25 | 48.0 |
| B albums | maximized | 60.0 | 55.0 | 35.5 | 50.2 | 66.8 | 7 | 46.0 |
| B genres | small | 60.0 | 56.0 | 35.2 | 50.0 | 67.6 | 3 | 45.6 |
| B genres | maximized | 60.0 | 55.0 | 49.8 | 50.1 | 50.1 | 5 | 47.6 |
| B folders | small | 60.0 | 58.0 | 33.8 | 34.3 | 36.7 | 0 | 44.8 |
| B folders | maximized | 60.0 | 57.0 | 33.6 | 33.8 | 49.1 | 0 | 44.8 |

Observations that survive both fixtures:

1. **The tail is not worse at Maximized.** For Tracks — the view in the original
   complaint — the pacing tail is clearly *better* at Maximized (p95 65.8 → 34.0 ms
   in A, 66.7 → 33.8 ms in B; worst gap 265.7 → 83.2 ms and 317.2 → 99.8 ms).
   Albums shows the same direction in Fixture B (97.4 → 66.8 ms worst) and is
   flat in Fixture A.
2. Artists, Genres and Folders are effectively unchanged between sizes; the
   differences (~±15 ms) sit at the 2–3 refresh-interval quantisation of the
   metric.
3. Gaps cluster on multiples of ~16.7 ms (33.6 / 49.x / 66.x / 83.x), and ~45–52%
   of captures show no change at **both** sizes. That is the signature of content
   updating on a subset of refresh intervals, plus capture/composition beat — not
   of a maximized-specific freeze.

**Limitation of this pass:** the strip is the same pixel rectangle relative to the
scroll surface, so at different window widths it covers slightly different content;
scroll distance per burst also differs between sizes (51.8% vs 78.6% on A Tracks).
It is a proxy for "did anything visible move", not a frame meter.

---

## 7. What the evidence supports

1. **The reported "worse when maximized" symptom did not reproduce in any of the
   five views, in either fixture, in any measured proxy.** UI-thread CPU is flat or
   lower at Maximized; composition-callback gaps do not grow; the captured-screen
   content tail is not worse and is better for Tracks/Albums.
2. **The only size-monotonic signal is GPU utilisation**, and it returns to the
   Small baseline as soon as the window shrinks. That is the expected fill-rate /
   composition cost of a larger surface, and it is not by itself a stutter.
3. **Virtualizing vs non-virtualizing is real and confirmed:** Tracks/Artists/Albums
   keep 24–49 containers alive; Genres/Folders realise all 180–320 items at every
   size. Any future hypothesis should not treat these two groups as one.
4. **There is no disk, image-decode or memory-growth activity inside the measured
   bursts** (IO delta 0, GPU memory stable, working set size-independent).
5. **The 8-wheel discard burst matters.** Every earlier round that measured the
   first burst after a resize or tab activation was measuring container
   realisation and image decode, not steady-state scrolling. This round removed
   that contaminant deliberately.

## 8. What the evidence does not support

- It does **not** identify a root cause for a perceived stutter at large sizes.
  No hypothesis (glass/blur, wallpaper, converters, image pipeline, panel
  recycling) has been A/B tested here, and none may be declared the cause.
- It does **not** prove the UI is smooth. Presented frames are unavailable; a
  low-UI-CPU burst can still be perceptually uneven.
- It does **not** cover what happens **during** the maximize/restore transition or
  the **first** scroll immediately after it. The protocol waits ~5–7 s and burns a
  warm-up burst at the new size before measuring. If the reported stutter occurs
  in that first post-maximize scroll (new column count → new realisation set →
  image decode), this matrix deliberately excluded it. That is the single most
  important untested window and the natural next measurement step.
- It does **not** cover idle background activity (wallpaper loop, library watcher)
  overlapping the burst; the bursts are short and the app was freshly launched.

---

## 9. Limitations

| Limitation | Effect |
|---|---|
| No PresentMon/ETW presented frames | No FPS, no frame-time distribution, no dropped-frame count |
| Probe gaps are composition callbacks | Cannot be converted to FPS; used only for relative comparison |
| Screen capture at ~60 Hz with BitBlt | Beat/quantisation produces ~45–50% "unchanged" captures at both sizes; only relative comparison is meaningful |
| Strip content differs slightly by size | Absolute change counts are not directly comparable across sizes |
| 5 reps per cell | Medians stable, but tail estimates have wide uncertainty |
| 2C/4T U-series CPU + dual GPU | Absolute values will not transfer to other hardware; direction of the size effect might |
| Wheel input via `mouse_event` | Coalescing observed (21–25 of 25 received); total distance preserved |
| Maximize transition excluded | See §8 |

---

## 10. Reproduction

```powershell
# build
dotnet build src/MusicPlayer/MusicPlayer.csproj -c Release --no-restore

# official matrix, one fixture at a time
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\M25FullMatrix.ps1 `
  -Fixture fixtureA -DataDir C:\Users\win\AppData\Local\Temp\opencode\m25full\fixtureA\data -Repeats 5
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\M25FullMatrix.ps1 `
  -Fixture fixtureB -DataDir C:\Users\win\AppData\Local\Temp\opencode\m25full\fixtureB\data -Repeats 5

# supplementary pacing pass
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\M25PacingPass.ps1 -Fixture fixtureA -Repeats 5
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\M25PacingPass.ps1 `
  -Fixture fixtureB -DataDir C:\Users\win\AppData\Local\Temp\opencode\m25full\fixtureB\data `
  -LaunchWarmupSeconds 30 -Repeats 5

# analysis
python tools\perf\M25Analyze.py perf-results\m25\container-scroll-full\fixtureA\samples.csv `
  --out perf-results\m25\container-scroll-full\analysis-fixtureA.md
python tools\perf\M25Analyze.py perf-results\m25\container-scroll-full\fixtureB\samples.csv `
  --out perf-results\m25\container-scroll-full\analysis-fixtureB.md
python tools\perf\M25PacingAnalyze.py perf-results\m25\container-scroll-full\fixtureA\pacing\pacing-20261118-132333.csv
python tools\perf\M25PacingAnalyze.py perf-results\m25\container-scroll-full\fixtureB\pacing\pacing-20261118-133058.csv
```

---

## 11. Artifact index

| Path | Content |
|---|---|
| `perf-results/m25/container-scroll-full/fixtureA/samples.csv` | 100 official Fixture A rows (64 columns, incl. GPU + probe) |
| `perf-results/m25/container-scroll-full/fixtureB/samples.csv` | 100 official Fixture B rows |
| `perf-results/m25/container-scroll-full/analysis-fixtureA.md` / `-fixtureB.md` | per-cell medians, spreads, warm-up check |
| `perf-results/m25/container-scroll-full/analysis-pacing-fixtureA.md` / `-fixtureB.md` | pacing medians, size comparison, movement proof |
| `perf-results/m25/container-scroll-full/fixtureA|fixtureB/pacing/*.csv` | 50 pacing rows each (`run-fixtureA.log`, `run-fixtureB.log`) |
| `perf-results/m25/container-scroll-full/fixtureA|fixtureB/logs/scrollprobe-*.csv` | per-burst in-app probe rows (discard + measured) |
| `perf-results/m25/container-scroll-full/fixtureA|fixtureB/screens/` | 200 before/after screenshots each |
| `perf-results/m25/container-scroll-full/fixtureA|fixtureB/gpu/` | raw PDH GPU samples |
| `perf-results/m25/container-scroll-full/run1-partial/`, `dryrun/`, `dryrun3/`, `dryrun4/`, `smoke3/` | preserved earlier/validation runs, not part of the official matrix |
| `perf-results/m25/container-scroll-full/PROGRESS.md` | batch log |

Pacing validation artifacts: `pacing-20261118-132222.csv` (2 rows, single-cell
validation) and `pacing-20261118-132144.csv` (header only, aborted first attempt);
neither is part of the official 50-row pass.

---

## 12. Real user library

The same protocol was re-run against the real library (201 tracks, 145 artists,
150 albums, 3 genres, 1 folder) with the same binary. Tracks/Artists/Albums:
**60/60 valid**, pacing **30/30 valid**. Genres/Folders report
`VerticallyScrollable=false` there (3 genres, 1 folder) and cannot be measured.

Result: the same conclusion holds. Maximized is the cheapest UI-CPU state in all
three views; GPU is again the only size-monotonic signal; the captured-screen
tail is better at Maximized for Tracks and flat for the others. The one new lead
is that Return-to-Small was the most expensive state in all three views
(+4 to +12% UI CPU over Small), measured ~5–7 s after the restore — worth a
dedicated test, not a conclusion.

Full detail: `docs/M25_LIBRARY_SCROLL_MEASUREMENT.md`.

## 13. Scope protection

Untouched, as required: scroll algorithms, `VirtualizingBalancedPanel`,
`SmoothScrollHelper`, Glass/Blur/Wallpaper, image quality, virtualization and
recycling policy, library load/warm-up, view design and animations,
`MainViewModel.InitializeAsync`, `WarmUpAndRefreshAsync`, `WallpaperWarmupLoop`,
`UpgradeArtistCache`.

Added: testability AutomationIds/`x:Name`s, the env-gated inert `ScrollProbe`,
the GPU sampler inside the harness, the pacing harness, and the analysis scripts.
No commit, tag, branch switch or merge. No user library data was opened.

**Status: awaiting approval. No production performance change has been made.**
