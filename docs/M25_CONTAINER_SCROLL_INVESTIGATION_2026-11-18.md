# M2.5 Container Scroll Investigation — 2026-11-18

## Status

**Incomplete / diagnosis not closed.** This round collected a live, tab-verified
size matrix for Tracks and Artists and tab-verified capture data for Albums.
Genres and Folders are not scrollable with either fixture. The existing verified
harness cannot prove Albums because it requires a scroll element name/AutomationId
containing the expected view, while the Albums surface has neither in UIA.
No presented-frame evidence is available.

## 1. Test Environment

| Item | Value |
|---|---|
| Branch | `ui-redesign` |
| HEAD | `18126e72e489c7a6f76e018260b48fb325b15174` |
| Git status before and during this round | 3 modified source files and existing untracked reports/results; no branches/tags/commits changed |
| Modified source files | `MainViewModel.cs`, `MainWindow.xaml.cs`, `NavigationChoreography.cs` |
| OS | Windows 10 Enterprise LTSC, build 19044 |
| Display | 1366x768 at 60 Hz, Intel HD active |
| DPI | 100%, 96 DPI |
| .NET SDK | 8.0.425 |
| .NET host/runtime | 8.0.11 / 8.0.22 or 8.0.31 runtimes installed |
| Release executable | `src\MusicPlayer\bin\Release\net8.0-windows10.0.19041.0\win-x64\MusicPlayer.exe` |
| EXE SHA-256 | `F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766` |
| DLL SHA-256 | `A972A4685D9B22E26180795F5C538A1C43ED88B1A9EAD81DE2CDE6E738B0883C` |
| Window sizes | Small outer 1116x650 / client 1100x642; Medium outer 1280x720 / client 1264x712; Maximized client 1366x738 |
| Wheel sequence | 25 wheel events at 35 ms, repeated 3 times for verified harness samples |
| Fixtures | `fixture201` / `fixture5000`, both isolated under `%LOCALAPPDATA%\Temp\opencode` |

No user library was used.

## 2. Fixture and Data Validity

| Fixture | Tracks | Artists | Albums | Genres | Managed folders |
|---|---:|---:|---:|---:|---:|
| fixture201 | 201 | 18 | 201 | 10 | 1 |
| fixture5000 | 5000 | 500 | 5000 | 10 | 1 |

| Container | 201 result | 5000 result |
|---|---|---|
| Tracks – Grid | Valid in Small, Medium, Maximized, Return Small | Valid in Small, Medium, Maximized, Return Small |
| Artists Tab | Valid in Small, Medium, Maximized, Return Small | Valid in Small, Medium, Maximized, Return Small |
| Albums Tab | Inconclusive: live scroll surface exists, but existing harness cannot prove identity and no independent before/after position proof was captured | Inconclusive: live scroll surface exists, but existing harness cannot prove identity and no independent before/after position proof was captured |
| Genres Tab | Not Scrollable: 10 genres only | Not Scrollable: 10 genres only |
| Folders Tab in All Songs | Not Scrollable: 1 managed folder only | Not Scrollable: 1 managed folder only |

## 3. Test Matrix

### 3.1 fixture5000 — Tracks Grid

Each value is UI-thread CPU for 25 wheel events; 3 repeats.

| Window state | UI CPU repeat values (ms) | Median (ms) | Process CPU median (ms) |
|---|---|---:|---:|
| Small | 1062, 625, 703 | 703 | 1172 |
| Medium | 812, 750, 719 | 750 | 1219 |
| Maximized | 734, 703, 688 | 703 | 1312 |
| Return Small | 641, 703, 578 | 641 | 1125 |

Observed scroll element: `SongsGridScroller`, viewport heights 427/497/523 px.
The sequence shows no monotonic Small-to-Maximized increase and returns to the
small-range median after returning to Small.

### 3.2 fixture5000 — Artists Grid

| Window state | UI CPU repeat values (ms) | Median (ms) |
|---|---|---:|
| Small | 891, 422, 375 | 422 |
| Medium | 406, 438, 422 | 422 |
| Maximized | 375, 344, 375 | 375 |
| Return Small | 453, 312, 359 | 359 |

Observed scroll element: `ArtistsSearchScroll`.
No Maximized-only increase or failure to recover in the measured 25-wheel run.

### 3.3 fixture201 — Tracks Grid

| Window state | UI CPU repeat values (ms) | Median (ms) |
|---|---|---:|
| Small | 594, 594, 562 | 594 |
| Medium | 719, 844, 672 | 719 |
| Maximized | 641, 750, 672 | 672 |
| Return Small | 641, 656, 625 | 641 |

The medium/Maximized medians are slightly above Small, but return Small remains
below Medium and there is no large Maximized-only separation.

### 3.4 fixture201 — Artists Grid

| Window state | UI CPU repeat values (ms) | Median (ms) |
|---|---|---:|
| Small | 109, 31, 62 | 62 |
| Medium | 125, 16, 62 | 62 |
| Maximized | 94, 94, 94 | 94 |
| Return Small | 62, 47, 78 | 62 |

### 3.5 Albums — existing interactive captures

| Fixture | Small max UI busy | Medium max UI busy | Maximized max UI busy | Return Small max UI busy |
|---|---:|---:|---:|---:|
| fixture5000 | 93.35% | 108.47% | 76.49% | 89.01% |
| fixture201 | 94.46% | 89.51% | 35.12% | 78.71% |

These are surrounding CaptureInteractiveScroll samples, not verified-harness
samples. They cannot prove presented FPS or independent content movement and
are retained only as inconclusive UI-thread evidence.

## 4. Size Comparison

- Tracks–Grid 5000: median UI CPU is 703 ms Small, 750 ms Medium, 703 ms
  Maximized, and 641 ms after returning Small. No size-driven pattern is
  demonstrated.
- Tracks–Grid 201: median UI CPU is 594 ms Small, 719 ms Medium, 672 ms
  Maximized, and 641 ms after return. This is variability across size states,
  not a clear Maximized-only bottleneck.
- Artists–5000: median UI CPU is 422 ms Small, 422 ms Medium, 375 ms Maximized,
  and 359 ms after return.
- Artists–201: 62 ms Small, 62 ms Medium, 94 ms Maximized, 62 ms after return.
  The Maximized median is 32 ms above Small, but only 94 ms absolute and there
  is full recovery after return.

## 5. Root-Cause Analysis

### 5.1 Evidence ranking

| Rank | Hypothesis | Result | Evidence |
|---|---|---|---|
| Proven only by exclusion | Presented FPS / GPU bottleneck | **Not testable** | PresentMon and decoded ETW unavailable; no dropped/presented-frame counter |
| Not supported | Realized-container count spike at Maximized | **Unproven** | Existing tool did not collect realized-container counts; IDs showed stable visible scroll surfaces |
| Not supported | Window height alone causes large UI CPU | **Unproven** | Valid Tracks/Artists samples do not show monotonic Maximized CPU increase |
| Not supported | Failure to return after Small | **Not observed** | Tracks and Artists valid medians returned to Small-range values |
| Infrastructure gap | Layout cost / realization / virtualization | **Inconclusive** | No Measure/Arrange or realized-container instrumentation was available |
| Infrastructure gap | GPU fill rate or composition/effects | **Inconclusive** | No reliable GPU or presented-frame measurement |
| Infrastructure gap | Images/decode/background activity | **Inconclusive** | No image decode or background task trace |

No hypothesis is promoted to a proven root cause.

### 5.2 Hypothesis details

- **Layout cost:** opposed by the lack of monotonic UI CPU increase in valid
  Tracks/Artists sequences, but not excluded because Measure/Arrange counters
  were unavailable.
- **Virtualization/realization:** no realized-container count was collected.
- **Rendering/fill rate:** no presented frame or reliable GPU counter was
  available, so this is unknown.
- **Composition/effects:** the executable's existing window properties were not
  changed and no dynamic A/B comparison was performed.
- **Images:** fixtures use local fixture art, but no image decode trace was
  measured.
- **UI-thread contention:** Tracks/Artists UI CPU and process CPU were separated,
  but no contention trace or Measure/Arrange trace was available.
- **Background activity:** no direct concurrency trace was collected.
- **Window/display behavior:** all states used the same display, refresh rate,
  process, and fixture; size reversal recovered in valid rows.

## 6. Limitations

- No PresentMon or decoded-ETW presented-frame evidence.
- No WPR admin capture, GPU workload measurement linked directly to scroll
  events, or Measure/Arrange counters.
- Albums has an unnamed/ID-less scroll surface that the existing verified
  harness rejects, so its albums matrix is not `Valid`.
- Genres (10 entities) and Folders (1 managed folder) are not vertically
  scrollable with enough content in either fixture.
- No independent before/after element position record was added to Albums
  captures, so they are not treated as proof of real movement.
- No 201/5000 Slow/Continuous/End-to-Top matrix was completed for every tab.

## 7. Next Recommendation

Do not apply a fix yet. The smallest evidence-producing follow-up is an
instrumentation-only session that:

1. Adds a stable, read-only AutomationId or display name for Albums and
   optionally Genres/Folders.
2. Collects realized containers, visible item count, and Measure/Arrange duration
   around the existing 25-wheel sequence.
3. Reuses the mandated Small/Medium/Maximized/Return-Small matrix for Tracks,
   Artists, and Albums in both fixtures.
4. Uses PresentMon or a decoded-present ETL if a permitted build/administrator
   environment becomes available.

No production XAML, ViewModel, protected warmup, scroll helper, panel, or fixture
code was changed in this investigation.

## Raw Data

- Verified harness: `perf-results/m25/container-scroll-20261118/fixture201/verified/`
- Verified harness: `perf-results/m25/container-scroll-20261118/fixture5000/verified/`
- Interactive captures: `perf-results/m25/container-scroll-20261118/fixture201/captures/`
- Interactive captures: `perf-results/m25/container-scroll-20261118/fixture5000/captures/`
