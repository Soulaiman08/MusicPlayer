# M2.5 — Scroll Repair Report

**Status:** The transform-based `VirtualizingBalancedPanel` experiment remains
visually unconfirmed. A later user test isolated `WallpaperAlpha` as a direct
trigger: scrolling is smooth at zero and stutters at nonzero values. The latest
Release build replaces the full-window translucent scrim with an equivalent
same-resolution wallpaper bitmap composite. Visual acceptance of this repair
is still pending.

## 1. Issue and evidence

The user reports that fast scrolling is smooth in a small window but visibly
stutters when maximized, across multiple scroll surfaces. The existing
investigation did not identify sustained image decoding or disk activity during
steady-state scrolling, and did not provide presented-frame measurements.

The strongest direct code-path lead for Tracks Grid, Artists, and Albums was
that `VirtualizingBalancedPanel.SetVerticalOffset` invalidated measure on every
offset change. The smooth-scroll path updates the offset repeatedly; each
measure recalculates the realized range, measures its containers, and runs the
existing cleanup. This work grows with the number of realized cards, which is
larger in a wider viewport. This is a plausible layout-cost mechanism, not a
proven root cause.

The user reports that the frame cadence stabilizes immediately after the final
scroll-animation frame, while the small window remains smooth. This supports
looking at per-frame work while the offset changes. The first bounded change
avoided Measure when the realized range was sufficient, but still called
Arrange on every motion frame; the user reports that this did not remove the
stutter.

The user further clarifies that the whole application appears to stutter while
scrolling in the maximized window, not only the active list, and that this
happens during motion rather than at idle or during the maximize transition.
The window/root `SizeChanged` handlers mark the glass sample dirty, but rebuilds
are coalesced by a 200ms timer and the bitmap render is dispatched to a worker;
this resize path does not explain repeated work on every settled scroll frame.
The full-window wallpaper and glass layers remain shared composition work, but
the available evidence does not establish them as the bottleneck.

The list/table surfaces, normal Folders page, and the Genres / All Songs
Folders panels use separate scrolling/layout paths. This change is limited to
the three surfaces using `VirtualizingBalancedPanel`; it does not claim to fix
the other paths.

## 2. Bounded change

In `SetVerticalOffset`, the panel calculates the target visible-plus-cache
range and compares it with its currently realized, contiguous container range:

- If the target range is already realized, update the panel's vertical
  `TranslateTransform` by the difference between the last arranged offset and
  the current offset. This moves the existing visual tree without arranging
  every card on each animation frame.
- If any target item is not realized, or range/generator state is unavailable,
  invalidate Measure so virtualization can realize and clean up.
- `ArrangeOverride` arranges at the current offset and resets the translation,
  keeping the transform from accumulating across layout passes.
- Scroll-owner notification and offset clamping remain unchanged.

No container-generation or cleanup code, recycling behavior, scroll timing,
scroll-distance, images, Glass/Blur, wallpaper, animation, or other protected
areas were changed. The added focused tests cover in-range, boundary-crossing,
non-contiguous, empty-range, and transform-direction behavior.

## 3. Files changed

- `src/MusicPlayer/Views/VirtualizingBalancedPanel.cs` — translate the panel
  between layout passes while preserving Measure for virtualization changes.
- `tests/MusicPlayer.Tests/MusicPlayer.Tests/VirtualizingBalancedPanelTests.cs`
  — verify the range-coverage decision.
- This report.

## 4. Release build and binary identity

Target command:
`dotnet build src\MusicPlayer\MusicPlayer.csproj -c Release -r win-x64 --no-restore`

The final Release build succeeded with **0 warnings and 0 errors** after closing
the running test instance. The required executable is:
`src\MusicPlayer\bin\Release\net8.0-windows10.0.19041.0\win-x64\MusicPlayer.exe`.

EXE SHA-256:
`F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766`

DLL SHA-256:
`BE8C220CC5D440D96271AC4AC83AE1F5AE5A24D9F1C2235AD2693A6FEB17DB54`

## 5. Automated tests

Target command:
`dotnet test tests\MusicPlayer.Tests\MusicPlayer.Tests\MusicPlayer.Tests.csproj -c Release --no-restore --filter FullyQualifiedName~VirtualizingBalancedPanelTests`

The final targeted panel test run passed **10/10**.

## 6. Small / maximized comparison

| State | Before | After |
|---|---|---|
| Small | User reports smooth | Offset changed; visual smoothness not verified |
| Medium | Not visually verified | Not visually verified |
| Maximized | User reports visible stutter | Not visually verified |
| Maximized then return to Small | Not verified | Not verified |

The final Release executable was launched. In Tracks Grid, changing the
accessible scrollbar to offset 600 in both the small and maximized states
changed the realized song rows, confirming offset and virtualization activity.
Computer Use could not capture a screenshot, and wheel actions did not yield an
observable state change. No visual before/after comparison, rapid-wheel
smoothness acceptance, or claim about presented frames is made. No rendering
callback is treated as a presented frame.

## 7. Interface coverage

No rapid-scroll acceptance test was completed in this run.

- Group A (Tracks List, normal Folders, Most Played, Recent Add, Favorites,
  Recent Play): not verified.
- Group B: Tracks Grid had an accessibility-confirmed offset/realized-row
  change in the real library at small and maximized sizes, but no visual
  smoothness check. Artists and Albums were present but not scrolled; Genres
  and All Songs Folders were not verified.

The implementation applies only to Group B's Tracks Grid, Artists, and Albums
where `VirtualizingBalancedPanel` is used. It does not cover Group A or the
separate `BalancedItemsPanel` surfaces.

## 8. Regressions and limitations

The targeted tests and Release build pass, and the existing cleanup path is
unchanged. Scroll gaps, clipped final rows, scrollbar feel, image quality, and
small-window performance could not be visually assessed. The current change
remains an unconfirmed performance experiment until the same-device
small/maximized comparison shows improvement without regressions.

## 9. Final verification

Final targeted test result: **10 passed, 0 failed**.

Final Release build result: **succeeded, 0 warnings, 0 errors**.

EXE SHA-256: `F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766`

DLL SHA-256: `BE8C220CC5D440D96271AC4AC83AE1F5AE5A24D9F1C2235AD2693A6FEB17DB54`

## 10. Interactive telemetry follow-up — 2026-11-18

The user manually exercised the Release app at normal and maximized sizes. The
capture did not send input or alter scroll speed. Environment-gated diagnostics
now record per-scroll `ScrollChanged` bursts, raw WPF rendering callback times
and offsets, layout-update counts, scroll surface, window size, and correlated
process/UI-thread resource samples. The GPU Engine counter is read directly
every 250ms so it does not block the 50ms CPU/memory sampler.

Release rebuild:
`dotnet build src\MusicPlayer\MusicPlayer.csproj -c Release -r win-x64 --no-restore -t:Rebuild`

Result: **0 errors, 4 CS4014 warnings** in the existing
`MainWindow.xaml.cs:7023` and `:7120` call sites (reported for both WPF temp
project and main project). No repair or protected scroll code was changed by
this instrumentation.

EXE SHA-256: `F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766`

DLL SHA-256: `83A8B745CB93B5A262CA8EB0A629005993E67BFB44096A99417A455366B1D29B`

Capture files are in
`perf-results/interactive/20261118-154916-manual-small-medium-max/`.
The 22m58s session contains 18,650 resource samples, 14 scroll bursts, and
6,162 WPF rendering callbacks. The callback file and external resource samples
are correlated with UTC timestamps. The process working set rose over the
session, so raw memory differences also include cache/startup growth.

### Matched surface observations

| Surface | Normal window | Maximized |
|---|---|---|
| Tracks Grid (`SongsGridScroller`) | 2 bursts, 58.9–62.2 callback/s; median callback gap 16.36ms; 16.3% of gaps >25ms | 1 burst, 50.5 callback/s; median gap 18.93ms; 35.8% >25ms |
| Artists (`ArtistsSearchScroll`) | 2 bursts, 53.4–56.0 callback/s; median gap 16.70ms; 10.9% >25ms | 2 bursts, 40.9–42.1 callback/s; median gap 24.04ms; 47.8% >25ms |

These are WPF `CompositionTarget.Rendering` callbacks, **not displayed FPS**.
The lower callback rate and larger share of long callback gaps in Maximized are
directionally consistent with the reported stutter. The gesture durations and
speeds were user-driven rather than mechanically matched, and callback timing
cannot establish presentation timing.

During the matched bursts, sampled UI-thread busy time was not consistently
saturated: Tracks Grid averaged about 49–60% in Normal and 58% Maximized;
Artists averaged about 32–37% in Normal and 27–33% Maximized. The highest
sampled 3D engine use was about 34–37% for Normal Tracks Grid and 54% for its
Maximized burst; Artists peaked around 41–43% in Normal and 43–48% Maximized.
These GPU readings are not near full utilization and do not prove a GPU cause.
They do show that the slower callback cadence does not coincide with a
consistently saturated UI thread in the matched bursts. The capture therefore
narrows the next investigation toward work at or beyond WPF composition/
presentation, but does not identify a root cause.

### Capture limits

- The session recorded Normal (1116×650 in the app probe) and Maximized
  (1382×754); it did not include a distinct Medium-size phase.
- Only the Tracks Grid and Artists bursts had reliable same-view/same-scroll
  surface pairs. Some other bursts occurred during tab transitions, where the
  current-tab label and active scroll surface disagreed; realized-container
  counts were unavailable for most of those rows and were excluded from the
  matched comparison.
- PresentMon was not installed and WPR required unavailable administrator
  privileges. Presented FPS and DWM present intervals remain unavailable.
- The user’s “done” message ended the capture but did not separately record a
  visual smoothness rating. No claim that the maximized stutter is fixed is
  made.

## 11. WallpaperAlpha composition repair — 2026-11-18

The user isolated `WallpaperAlpha`: the application scrolls smoothly when its
value is zero and stutters when it is nonzero. The earlier wallpaper A/B
shortcut did not toggle the image, so that attempt is invalid and is not used
as evidence.

`MainWindow.xaml` applied `WallpaperAlphaOpacity` to a black `Border` covering
the entire window. The user’s direct comparison identifies this full-window
translucent layer as the trigger. The repair removes that layer and composites
its black tint into the decoded display bitmap once, off the UI thread, at the
source’s original pixel dimensions. Alpha zero uses the original bitmap. The
separate Glass backdrop pipeline and its alpha handling are unchanged. A
generation guard prevents a slow composite from replacing a newer wallpaper
or alpha value.

Files changed for this repair:

- `src/MusicPlayer/Views/MainWindow.xaml` — remove the full-window opacity
  scrim.
- `src/MusicPlayer/Views/MainWindow.xaml.cs` — apply the selected alpha to the
  display wallpaper bitmap and keep the Glass cache update path.
- `src/MusicPlayer/Utilities/WallpaperAlphaCompositor.cs` — preserve source
  pixel dimensions and composite the black tint into premultiplied BGRA pixels.
- `tests/MusicPlayer.Tests/MusicPlayer.Tests/WallpaperAlphaCompositorTests.cs`
  — verify pixel composition, dimensions, and the zero-alpha fast path.

Targeted Release tests passed **2/2**:
`dotnet test tests\MusicPlayer.Tests\MusicPlayer.Tests\MusicPlayer.Tests.csproj -c Release -r win-x64 --no-restore --filter FullyQualifiedName~WallpaperAlphaCompositorTests`

Release rebuild succeeded with **0 errors** and **4 existing CS4014 warnings**
in the app's fire-and-forget shutdown call sites. `git diff --check` passed.
The exact Release executable was launched and reported responsive.

EXE SHA-256:
`F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766`

DLL SHA-256:
`BCC6FCA323A5C64699B05856574F74324C24C9724A4C58C97BD02A5449F2816E`

Visual comparison of the repaired Release build at the user's normal
nonzero alpha setting is pending. Until the user confirms smooth scrolling in
both Small and Maximized, this report does not claim complete resolution.
