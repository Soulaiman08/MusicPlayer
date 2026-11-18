# تقرير تشخيص: أنيميشن دخول الصفحة (Page Enter Animation)

**التاريخ:** 2026-11-15  
**النطاق:** مراجعة مسار التنقل (Home / Folders / Favorites / Recent / Recent Add / Most Played / Back)  
**الملفات الأساسية:**
- `src/MusicPlayer/ViewModels/MainViewModel.cs`
- `src/MusicPlayer/Views/MainWindow.xaml.cs`
- `src/MusicPlayer/Views/NavigationChoreography.cs`
- `src/MusicPlayer/Diagnostics/NavigationProbe.cs`

**ملاحظة:** لم يتم تعديل أي ملف في هذا التقرير.

---

## 1. ملخص السبب (Root Cause المثبت)

أنيميشن دخول الصفحة (280ms, `MotionTokens.PageEnter`) **يعمل زمنيًا**، لكن WPF لا يعرض إطاراته تدريجيًا because the UI Thread is occupied by synchronous preparation work that runs **before and during** the animation window:

1. `OnCurrentViewChanged` (داخل setter `CurrentView`) выполняет `ApplyPendingLibrarySortForScope`, تفريغ البحث (`SearchResults.Clear()` etc.), `NotifyDedicatedDisplayedTracks`, `HasNoMusic`, `IsCurrentTabLoading` — all synchronous on UI thread.
2. `OnViewModelPropertyChanged(CurrentView)` (code-behind) performs `EnsureDetailPageOpacity()` (recursive visual tree walk + `CollectImages`) **synchronously**, then `BeginTracksSurfaceProbe` for "songs".
3. The async data loaders (`LoadFavoriteTracksAsync`, etc.) publish collections via `DispatchAsync(ReplaceAll)` **during** the 280ms animation, causing measure/arrange and container generation on the UI thread mid-animation.
4. `BeginTabLoad` is called **before** `CurrentView = page` in `NavigateToFolder`, and inside `Navigate` before `CurrentView = page` for empty collections — setting loading state & raising `HasNoMusic` synchronously before animation.

Therefore the first presented frame is delayed, and subsequent frames are starved by `ReplaceAll` / layout work during the animation. The animation's time completes, but frames are skipped → user perceives an instant jump.

---

## 2. إجابات الأسئلة (1–7)

### Q1. هل توجد أعمال متزامنة على UI Thread قبل بدء Page Enter Animation؟
**نعم.** في `MainViewModel.CurrentView` setter (line ~1919) → `OnCurrentViewChanged` (line ~9990) runs synchronously:
- `ApplyPendingLibrarySortForScope(CurrentSortScope())` (line 10001)
- `IsSearching = false` + `SearchQuery = string.Empty` + `SearchResults.Clear()` + `Filtered*.Clear()` (lines 10003–10014)
- `OnPropertyChanged` × many (lines 10016–10020)
- `NotifyDedicatedDisplayedTracks(oldView)` + `(value)` (lines 10026–10027)
- `HasNoMusic`, `IsCurrentTabLoading` (lines 10029–10031)

Then in `MainWindow.OnViewModelPropertyChanged` (line ~6479, CurrentView branch):
- `UpdateAppWindowTitleBarDragRegions()`
- `BeginTracksSurfaceProbe(...)` for "songs" (line 6483)
- `ClearColumnSelection()`
- `_navGen = NavGate.NewNavigation()`
- `EnsureDetailPageOpacity()` (line 6497) — recursive `CollectImages`
- `_heroTransitionPending`/`_isBackTransitioning` guards
- `RunSongsEntryChoreography()` / `RunPageZoomChoreography()` → `AnimatePageEnterZoom()` (line 6499–6505)
- `NavigationProbe.ScheduleLayoutAndCompletion(Dispatcher)` (line 6506)

So `AnimatePageEnterZoom` is called **after** all of the above synchronous work.

### Q2. هل يتم تجهيز الصفحة أو تحديث Collections أو Bindings أثناء الحركة؟
**نعم.** After `CurrentView = page`, `Navigate()` calls `LoadFavoriteTracks(token)` etc. (lines 751–773), which:
- DB query on `Task.Run` (background) — fine.
- But then `DispatchAsync(ReplaceAll(tracks))` on UI thread (e.g. line 7519) happens **during** the 280ms animation, mutating a collection bound to a `ListBox`/`ItemsControl`, triggering container generation + measure/arrange on UI thread.
- `BeginTabLoad`/`EndTabLoad` raise `HasNoMusic`/`IsCurrentTabLoading` on UI thread (via `BeginTabLoad` line 7503, `EndTabLoad` in `finally`).

### Q3. هل يبدأ الأنيميشن قبل أن تصبح الصفحة جاهزة للرسم؟
الأنيميشن (transform/opacity) يبدأ synchronously inside `AnimatePageEnterZoom` after `EnsureDetailPageOpacity`. But the incoming view's `Visibility` becomes `Visible` via `EqualityToVisibilityConverter` binding **before** `CurrentView` PropertyChanged reaches the view? Actually converter re-evaluates when `CurrentView` changes → incoming Grid becomes Visible during the same property change notification chain, before animation. So layout invalidation of incoming view happens, then animation begins. Measure/arrange of incoming view may occur **during** first animation frame → first-frame delay.

### Q4. هل توجد عمليات Measure/Arrange أو إعادة إنشاء عناصر تؤخر عرض أول إطار؟
**نعم:**
- `EnsureDetailPageOpacity` walks `ContentGrid.Children` and `CollectImages` (recursive) — UI thread.
- `NotifyDedicatedDisplayedTracks` raises `OnPropertyChanged(Displayed*Tracks)` → bindings re-evaluate.
- `ReplaceAll` during animation → `VirtualizingStackPanel` container generation.
- `BeginTracksSurfaceProbe` for "songs" subscribes to `CompositionTarget.Rendering`, `LayoutUpdated`, `StatusChanged` — adds overhead.

### Q5. هل هناك تعارض بين تغيير CurrentView وتحديث CurrentTab؟
**جزئيًا.** `Navigate("folders")` sets `CurrentView = "folders"` but does not change `CurrentTab`. `CurrentTab` setter (line 1323) calls `ApplyPendingLibrarySortForScope` + `PublishFilteredForCurrentTab`. These are not triggered by `CurrentView` change, so sort/filter for folders tab may be stale until tab switch. Also `IsTrackListView` depends on `CurrentView == "songs"`; switching to "folders" view makes `IsTrackListView` false, hiding songs list. No direct conflict, but `CurrentSortScope()` maps `CurrentView` to scopes, so sort pending flags can be consumed at wrong scope if `CurrentView` changes mid-load.

### Q6. هل يمكن أن تكون الحركة تعمل زمنيًا، لكن WPF لا يعرض إطاراتها بسبب انشغال Dispatcher؟
**نعم — هذا هو السبب الأساسي.** `CompositionTarget.Rendering` callbacks are queued on the Dispatcher; if UI thread is busy with `ReplaceAll`/measure/arrange, rendering callbacks are delayed → dropped frames. Animation clock continues (wall-clock based), so it "completes" but frames skip.

### Q7. هل هناك اختلاف بين مسار التنقل في Home ومسار Folder Details ومسار Back؟
**نعم:**
- **Home → favorites/recent/etc via `Navigate()`**: `BeginTabLoad` only if collection empty (lines 713–736); `CurrentView = page`; then `Load*` async. Animation runs.
- **Folder details via `NavigateToFolder`**: `BeginTabLoad("view:folder_detail")` **always** (line 5658) before `CurrentView`; `LoadFolderDetail` (may be sync? check) — heavier prep. Also no `_viewHistory` consecutive-duplicate guard for `folder_detail`? It has guard.
- **Back via `GoBack()`**: sets `CurrentView = target` directly, **no data load, no BeginTabLoad**. Animation runs but `EnsureDetailPageOpacity` still. Back should be smoother (no collection publish), but detail page opacity reset still blocks.

---

## 3. خطة القياس (Measurement Plan)

Use `NavigationProbe` (extend, do not change duration). Add timestamps:

| Metric | Definition | Source |
|---|---|---|
| Click → CurrentView change | T0 → T2 (`inputRespMs`) | already `RecordViewModelStateUpdated` |
| CurrentView change → Animation start | T2 → T_animStart | Add `RecordAnimationStarted()` called at entry of `AnimatePageEnterZoom` |
| Animation start → First render callback | T_animStart → T_firstRender | Count `CompositionTarget.Rendering` callbacks; record first |
| UI thread busy duration | Sum of sync segments: (T1→T2) + EnsureDetailPageOpacity + collection publish (T5→T6) | already partial; add EnsureDetailPageOpacity timing |
| Actual animation end | T_animEnd | Hook `fade.Completed` → `RecordAnimationCompleted()` |
| Dropped frames | Expected = `PageEnter.TotalMilliseconds / 16.67`; Dropped = Expected - RenderingCallbackCount during [animStart, animEnd] | Count in a dedicated `Rendering` handler scoped to animation |

**Important:** `CompositionTarget.Rendering` alone is not proof the frame reached the screen. Treat it as "render callback", and additionally:
- Compare against `Animation.Completed` (clock end).
- Optionally use DWM present ETW or `RenderTargetBitmap` snapshot at animation mid-point (expensive, test-only).
- Report frame count + dropped count, not just T8.

Existing `NavigationProbe.ScheduleLayoutAndCompletion` uses `CompositionTarget.Rendering` as T8 — keep but relabel as "first render callback", not "presented".

---

## 4. خطة الإصلاح (Minimal Architectural Fix)

**Principles:** don't delete animation, don't change duration, keep design quality.

### A. Move animation start to be FIRST
In `MainWindow.OnViewModelPropertyChanged` (CurrentView branch), reorder so that:
1. `NavGate.NewNavigation()`
2. `AnimatePageEnterZoom()` (synchronously, before any heavy work)
3. Then `EnsureDetailPageOpacity()` (lightweight resets only; defer `CollectImages` recursive walk to `DispatcherPriority.Background` after animation)
4. Then `BeginTracksSurfaceProbe` / `ClearColumnSelection`

Rationale: Rule #1 in `AnimatePageEnterZoom` comment: "Callers must invoke this SYNCHRONOUSLY with the CurrentView change, so the FIRST presented frame is already at 1.05/dim."

### B. Defer collection publish out of animation window
In `LoadFavoriteTracksAsync` etc., after `Task.Run` DB load:
- Check `token.IsCancellationRequested` before publish (already).
- Schedule `DispatchAsync(ReplaceAll)` at `DispatcherPriority.Background` **or** defer until animation completes.
- Better: in `AnimatePageEnterZoom`, capture the animation's `Completed` event and invoke a queued "publish pending collections" callback. But loaders don't know about animation. Alternative: keep `ReplaceAll` but move to `DispatcherPriority.Background` so rendering (DispatcherPriority.Render) takes precedence. WPF priority order: Render > Background, so background dispatch won't starve render.

### C. Move BeginTabLoad out of pre-animation path
- In `Navigate()`: call `CurrentView = page` first (triggers animation), then `BeginTabLoad` + `Load*`. Currently BeginTabLoad is called before CurrentView (lines 713–736). Move after.
- In `NavigateToFolder`: move `BeginTabLoad("view:folder_detail")` to after `CurrentView = "folder_detail"`.

### D. Cancel stale publishes
- Ensure every loader captures `token` from `BeginNavigationToken()` (already) and checks token before **and** inside `DispatchAsync`. Also check `NavGate.IsCurrent(_navGen)`? Add generation check in publish callback to avoid publishing to a superseded view. Already have `_navigationCts`; also add `_navGen` comparison.

### E. Background prep where possible
- `ApplyPendingLibrarySortForScope` in `OnCurrentViewChanged`: if sort needed, run on `Task.Run` then publish. But sort mutates collections; simpler: defer to `DispatcherPriority.Background`.
- `EnsureDetailPageOpacity`'s `CollectImages` recursive walk: move to `DispatcherPriority.Background` (after animation starts). The opacity resets (fe.Opacity = 1) on known elements can stay synchronous.

### F. Back path parity
- Make `GoBack()` also run `AnimatePageEnterZoom` synchronously (already via PropertyChanged). Ensure no data loads. Add the same measurement hooks.

### G. Measurement instrumentation (test-only, guarded by `_enabled`)
- Add to `NavigationProbe`: `RecordAnimationStarted`, `RecordFirstRenderCallback`, `RecordAnimationCompleted`, `RecordUiBusySegment`, `IncrementDroppedFrames`.
- In `MainWindow`: scope a `Rendering` handler between anim start and anim end; count callbacks; compute dropped frames.

---

## 5. الاختبار (Test Plan)

1. **Baseline (PERFTEST=1, PageZoom enabled):** Run navigation scenario for each view; collect NAV-SUMMARY + new anim metrics. Record dropped frames.
2. **A/B (PERFTEST_NO_PAGEZOOM=1):** disable page zoom; compare dropped frames & totalMs. (User hasn't run yet — required.)
3. **Regression:** Unit tests for `BeginNavigationToken` cancellation; verify stale `ReplaceAll` does not publish after navigation. Add test: navigate A→B while A's load in flight → A's collection not updated after B shown.
4. **Visual:** manual check that animation still 280ms, opacity 0.55→1.0, scale 1.05→1.0, no design/image quality change.
5. **Frame count check:** dropped frames should drop to ~0 after fix.

---

## 6. الملفات والدوال المسؤولة (Summary)

| File | Function | Role |
|---|---|---|
| `MainViewModel.cs` | `CurrentView` setter + `OnCurrentViewChanged` | Sync prep before animation |
| `MainViewModel.cs` | `Navigate`, `NavigateToFolder`, `GoBack` | Orchestration, BeginTabLoad placement, data loads |
| `MainViewModel.cs` | `LoadFavoriteTracksAsync` etc. | UI-thread `ReplaceAll` during animation |
| `MainWindow.xaml.cs` | `OnViewModelPropertyChanged` (CurrentView branch) | EnsureDetailPageOpacity + AnimatePageEnterZoom ordering |
| `MainWindow.xaml.cs` | `EnsureDetailPageOpacity` | Recursive visual walk (heavy) |
| `MainWindow.xaml.cs` | `AnimatePageEnterZoom` | Animation start |
| `NavigationChoreography.cs` | `MotionTokens.PageEnter` | Duration (unchanged) |
| `NavigationProbe.cs` | `ScheduleLayoutAndCompletion` | T7/T8 measurement (render callback, not present) |

---

## 7. ملاحظات

- لا تغيير في مدة الأنيميشن أو حذفه.
- لا تغيير في جودة التصميم/الصور/الحركات الأخرى.
- التعديل minimal: reorder calls + defer heavy work + background priority for publishes.
- لا تعديل تم على المشروع.
