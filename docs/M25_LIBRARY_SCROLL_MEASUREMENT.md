# M2.5 — Container Scroll Measurement on the User Library

**Date:** 2026-11-18
**Build:** same instrumented `Release` binary as the fixture round
`src\MusicPlayer\bin\Release\net8.0-windows10.0.19041.0\win-x64\MusicPlayer.exe`
(DLL SHA-256 `9A2634D3B877CEF28A95456859879BB03F2309296AAE12F3B393C0A49805C6E5`)
**Data source:** the real user library (no `MUSICPLAYER_DATA_DIR` override)
**Status:** measurement only — **no production change**

This is the same protocol as `docs/M25_CONTAINER_SCROLL_FULL_INVESTIGATION.md`,
re-run against the real library instead of an isolated fixture.

---

## 1. What is in the library

Read directly from `%APPDATA%\MusicPlayer\music.db` in read-only mode:

| Entity | Rows |
|---|---:|
| Tracks | 201 |
| Distinct artists | 145 |
| Distinct albums | 150 |
| Distinct genres | 3 (`Arabic Pop`, empty, `<unknown>`) |
| Enabled managed folders | 1 (`C:\Users\win\Music\User Music`) |

Container item counts actually realised by the app during the run: **201 tracks,
142 artists, 149 albums**.

### Scrollability

| View | Scrollable here? | Evidence |
|---|---|---|
| Tracks – Grid | ✅ | 201 items, 24–36 containers realised |
| Artists Tab | ✅ | 142 items, 30–34 containers realised |
| Albums Tab | ✅ | 149 items, 30–36 containers realised |
| Genres Tab | ❌ | `view reports VerticallyScrollable=false` (3 genres) |
| Folders Tab | ❌ | `view reports VerticallyScrollable=false` (1 folder) |

Genres and Folders could not be measured on this library for a structural
reason, not a tooling one: with 3 genres and 1 managed folder there is nothing to
scroll. Those two views remain covered by the fixture round (180/240 genres,
220/320 folders). The UIA probe confirms the surfaces exist and are on-screen;
they simply report `VerticallyScrollable=false`.

---

## 2. Matrix coverage

Tracks/Artists/Albums × Small / Medium / Maximized / Return-to-Small × 5 reps.

**60/60 valid, 60/60 probe rows matched, GPU sampler `ok` in every sample.**
Wheel messages received: min 22, median 25, max 25 (of 25 injected).

Movement proof (median UIA vertical scroll percent):

| View | Small | Medium | Maximized | Return |
|---|---:|---:|---:|---:|
| songs | 53.1 | 68.9 | 75.3 | 57.3 |
| artists | 94.1 | 100.0 | 100.0 | 93.4 |
| albums | 83.6 | 97.6 | 99.3 | 83.3 |

---

## 3. UI-thread CPU per burst — median / P95 / max (ms)

| view | size | median / P95 / max |
|---|---|---:|
| songs | small | 828 / 944 / 969 |
| songs | medium | 875 / 962 / 984 |
| songs | maximized | 750 / 843 / 859 |
| songs | return-small | 859 / 875 / 875 |
| artists | small | 594 / 1,079 / 1,188 |
| artists | medium | 578 / 578 / 578 |
| artists | maximized | 422 / 484 / 500 |
| artists | return-small | 656 / 656 / 656 |
| albums | small | 594 / 719 / 750 |
| albums | medium | 625 / 687 / 703 |
| albums | maximized | 500 / 528 / 531 |
| albums | return-small | 641 / 669 / 672 |

UI busy %: songs 66.6 → 66.9 → 61.6 → 66.6; artists 48.5 → 46.9 → 34.2 → 51.2;
albums 48.0 → 51.5 → 42.2 → 51.6.

Excluding repetition 1 (first-activation check):

| view | small | medium | maximized | return-small |
|---|---:|---:|---:|---:|
| songs | 789 | 812 | 734 | 867 |
| artists | 562 | 570 | 422 | 632 |
| albums | 570 | 625 | 508 | 625 |

**Finding 1:** on the real library the UI thread does **not** work harder at
Maximized. Maximized is the cheapest of the four states in all three views
(750 vs 828, 422 vs 594, 500 vs 594). The Artists/Small P95 is inflated by a
single 1,188 ms first-activation sample; the other four reps were 516–641 ms.

**Finding 2 (new, needs confirmation):** **Return-to-Small is consistently the
most expensive state**, 4–12% above a steady Small window in all three views
(songs 828 → 859, artists 594 → 656, albums 594 → 641; excluding rep1:
789 → 867, 562 → 632, 570 → 625). This is the state a user is in immediately
after un-maximizing. It was not visible in the fixture round (where Return was
at or below Small). With n=5 per cell and Return always running right after
Maximized, treat it as a lead for a dedicated test, not a conclusion.

---

## 4. GPU per-PID utilisation — median / P95 / max of the mean, and per-burst max

| view | size | mean median / P95 / max | per-burst max (median) | DWM mean |
|---|---|---:|---:|---:|
| songs | small | 23.4 / 26.4 / 27.1 | 31 | 1.4 |
| songs | medium | 31.9 / 33.8 / 33.9 | 40 | 1.8 |
| songs | maximized | 38.7 / 40.5 / 40.5 | 46 | 2.0 |
| songs | return-small | 24.4 / 25.2 / 25.3 | 32 | 1.4 |
| artists | small | 31.7 / 34.3 / 35.0 | 42 | 1.9 |
| artists | medium | 35.7 / 37.8 / 38.0 | 47 | 2.0 |
| artists | maximized | 36.9 / 38.6 / 38.8 | 47 | 2.0 |
| artists | return-small | 27.9 / 29.1 / 29.2 | 37 | 1.6 |
| albums | small | 28.3 / 29.9 / 30.1 | 36 | 1.7 |
| albums | medium | 32.5 / 35.9 / 35.9 | 43 | 1.9 |
| albums | maximized | 36.3 / 38.6 / 38.6 | 45 | 2.0 |
| albums | return-small | 29.1 / 29.2 / 29.2 | 37 | 1.7 |

**Finding 3:** GPU utilisation rises with window size in all three views and
falls back when the window shrinks — identical to the fixture result. DWM stays
at 1.4–2.0%. This is the expected fill/composition cost of a larger surface and
is not by itself a defect.

---

## 5. Composition cadence and containers

| view | size | render callbacks | max render gap ms | realised / total |
|---|---|---:|---:|---:|
| songs | small | 107 | 44.3 | 24 / 201 |
| songs | medium | 79 | 45.2 | 36 / 201 |
| songs | maximized | 83 | 48.3 | 36 / 201 |
| songs | return-small | 108 | 41.4 | 24 / 201 |
| artists | small | 96 | 41.1 | 34 / 142 |
| artists | medium | 86 | 46.5 | 30 / 142 |
| artists | maximized | 82 | 43.8 | 30 / 142 |
| artists | return-small | 90 | 38.3 | 34 / 142 |
| albums | small | 91 | 42.2 | 36 / 149 |
| albums | medium | 85 | 46.4 | 30 / 149 |
| albums | maximized | 80 | 44.4 | 30 / 149 |
| albums | return-small | 91 | 48.8 | 36 / 149 |

**Finding 4:** the buffer shape is the same as on the fixtures. Composition
callbacks fall at Maximized for all three virtualizing views (107 → 83,
96 → 82, 91 → 80) while the max gap does not grow (44.3 → 48.3 ms, 41.1 →
43.8 ms, 42.2 → 44.4 ms). Callbacks are **not** frames; no FPS is derived.

**Finding 5:** working set 494–514 MB, GPU dedicated memory 108–111 MB, and
**IO read delta = 0 in all 60 samples**. No image re-read or memory growth occurs
inside a burst at any size.

---

## 6. Captured-screen content cadence (supplementary)

30/30 valid. Same caveats as before: GDI capture at ~60 Hz, a content-cadence
proxy, not presented frames.

| view | size | changes | p95 gap | max gap median | max gap worst | over 50 ms | unchanged % |
|---|---|---:|---:|---:|---:|---:|---:|
| songs | small | 43 | 66.6 | 99.7 | 283.3 | 48 | 48.5 |
| songs | maximized | 55 | 33.9 | 50.0 | 83.2 | 3 | 45.2 |
| artists | small | 56 | 48.7 | 66.7 | 83.3 | 14 | 45.2 |
| artists | maximized | 50 | 50.1 | 50.5 | 83.5 | 19 | 52.0 |
| albums | small | 55 | 52.3 | 66.1 | 83.9 | 27 | 47.3 |
| albums | maximized | 51 | 50.0 | 50.4 | 51.2 | 20 | 49.6 |

**Finding 6:** on the real library the captured-screen tail is again **not worse
at Maximized**. Tracks is markedly better (p95 66.6 → 33.9 ms; worst
283.3 → 83.2 ms; gaps over 50 ms 48 → 3). Artists is flat; Albums is slightly
better at Maximized.

---

## 7. Library vs Fixture A (same 201 tracks)

| Metric (median) | Library | Fixture A |
|---|---:|---:|
| Tracks UI CPU small / max | 828 / 750 | 750 / 734 |
| Artists UI CPU small / max | 594 / 422 | 469 / 438 |
| Albums UI CPU small / max | 594 / 500 | 594 / 562 |
| Tracks GPU mean small / max | 23.4 / 38.7 | 24.1 / 37.5 |
| Render callbacks Tracks small / max | 107 / 83 | 118 / 84 |
| Working set (MB) | ≈500 | ≈450 |

The real library costs a little more UI CPU on Tracks (+10% at Small) and Artists
(+27%, but Fixture A has 8 more artists and the library Artists/Small cell has a
1,188 ms outlier). GPU and composition cadence are essentially identical. The
direction of every size effect is the same.

---

## 8. What this changes in the conclusion

Nothing in the main conclusion changes:

- The "worse when maximized" symptom **does not reproduce** on the real library
  either — not in UI CPU, not in composition gaps, not in GPU per burst, not in
  captured-screen content cadence.
- GPU utilisation is again the only size-monotonic signal, and it returns to
  baseline on shrink.
- Genres and Folders cannot be evaluated against this library (3 genres,
  1 folder); their evidence remains the fixture round, where they realise all
  items.

One new lead is worth recording: **Return-to-Small was the most expensive state
in all three library views** (+4 to +12% UI CPU over Small). It is measured ~5–7 s
after the restore and immediately after the Maximized cell, so it may be residual
post-resize work — or ordering. It deserves its own test before any attribution.

---

## 9. Limitations

Same as the fixture round, plus:

| Limitation | Effect |
|---|---|
| Real album art is decoded here (fixture art is synthetic) | Slightly higher absolute UI CPU is expected; comparisons are still within-run |
| Only 3 views scrollable | Genres/Folders not measured on this library |
| Return-to-Small always follows Maximized | The "most expensive state" lead is confounded with order |
| No presented frames | Still no FPS and no proof of perceived smoothness |
| 2C/4T machine | Absolute values do not transfer to other hardware |

**Data safety:** the harness only injected wheel events and read counters. No
audio file was modified (newest file in `User Music` is from 2026-11-11). The app
itself rewrote its own `music.db` at 14:09 on exit — normal session/settings
persistence, not a harness write.

---

## 10. Reproduction

```powershell
# matrix (real library, no data-dir override)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\M25FullMatrix.ps1 `
  -Fixture library -UseUserLibrary -Views songs,artists,albums -Repeats 5 -LaunchWarmupSeconds 25

# scrollability check for the two short views
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\M25FullMatrix.ps1 `
  -Fixture library-unscrollable -UseUserLibrary -Views genres,folders -Sizes small -Repeats 1

# pacing pass
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\M25PacingPass.ps1 `
  -Fixture library -UseUserLibrary -Views songs,artists,albums -LaunchWarmupSeconds 25 -Repeats 5

# analysis
python tools\perf\M25Analyze.py perf-results\m25\container-scroll-full\library\samples.csv `
  --out perf-results\m25\container-scroll-full\analysis-library.md
python tools\perf\M25PacingAnalyze.py perf-results\m25\container-scroll-full\library\pacing\pacing-20261118-140502.csv `
  --out perf-results\m25\container-scroll-full\analysis-pacing-library.md
```

Two harness fixes were needed for the real library and are test-tooling only:

- `-UseUserLibrary` (both harnesses): launch without `MUSICPLAYER_DATA_DIR`.
- `Wait-ById`: poll for a tab's scroll surface for up to 10 s, because real-library
  tab content is created lazily and briefly lagged the tab click.
- `$sizesForRep = @(...)`: a single-element `-Sizes` was unwrapped to a string, so
  `[0]` indexed the first character and the harness took the wrong branch.

---

## 11. Artifacts

| Path | Content |
|---|---|
| `perf-results/m25/container-scroll-full/library/samples.csv` | 60 official rows |
| `perf-results/m25/container-scroll-full/analysis-library.md` | per-cell medians/spreads |
| `perf-results/m25/container-scroll-full/library/pacing/pacing-20261118-140502.csv` | 30 pacing rows |
| `perf-results/m25/container-scroll-full/analysis-pacing-library.md` | pacing analysis |
| `perf-results/m25/container-scroll-full/library/logs/` | probe rows |
| `perf-results/m25/container-scroll-full/library/screens/` | 120 before/after screenshots |
| `perf-results/m25/container-scroll-full/library-unscrollable/samples.csv` | 2 rows proving Genres/Folders are not scrollable |
| `perf-results/m25/container-scroll-full/run-library-matrix.log`, `run-library-unscrollable.log`, `run-library-uia-genres.log` | run logs |

**Status: awaiting approval. No production performance change has been made.**
