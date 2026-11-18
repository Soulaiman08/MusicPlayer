# تقرير إصلاح: تجمد أنيميشن دخول الصفحات

**التاريخ:** 2026-11-15  
**النطاق:** تنفيذ minimal للإصلاح (بدون Git commit/push/merge).

## 1. الملفات المعدّلة

### `src/MusicPlayer/ViewModels/MainViewModel.cs`
1. `Navigate()`:
   - حُذفت كتلة `BeginTabLoad` التي كانت تُنفَّذ قبل `CurrentView = page`.
   - `BeginTabLoad` الآن يحدث ضمنياً داخل `Load*Async` (التي تُستدعى بعد `CurrentView = page`)، therefore loading state becomes correct for the new view.
   - `CurrentView = page` now occurs before data loads.
2. `NavigateToFolder()`:
   - حُذف `BeginTabLoad("view:folder_detail")` المكرر قبل `CurrentView`; `LoadFolderDetailAsync` retains its own `BeginTabLoad` (runs synchronously on UI thread after `CurrentView` is set).
3. `LoadFavoriteTracksAsync` / `LoadRecentPlayTracksAsync` / `LoadRecentAddTracksAsync` / `LoadMostPlayedTracksAsync`:
   - إضافة guard داخل `DispatchAsync`: `if (!string.Equals(CurrentView, <view>, ...)) return;` لمنع نشر نتائج صفحة سابقة.
   - فحص `token.IsCancellationRequested` محفوظ.
   - قياس `publishMs` للنشر (`ReplaceAll`) وتسجيله عبر `LogHelper`.

### `src/MusicPlayer/Views/MainWindow.xaml.cs`
1. `OnViewModelPropertyChanged` (CurrentView branch):
   - `AnimatePageEnterZoom()` (Run*Choreography) теперь вызывается **قبل** `EnsureDetailPageOpacity()`.
   - `EnsureDetailPageOpacity()` remains after animation start.
   - Added `PAGE-ENTER-PREP` timing log.
2. `EnsureDetailPageOpacity()`:
   - resets known detail page opacity synchronously.
   - recursive `CollectImages` loop deferred to `DispatcherPriority.Background`.
3. `AnimatePageEnterZoom()`:
   - added render-callback counter during animation (start→complete).
   - logs `PAGE-ENTER-ANIM animMs / renderCallbacks / expectedFrames / droppedEst`.
   - explicitly labels render callbacks as not proof of on-screen frames.

## 2. سبب كل تعديل

- Animation must start before heavy sync prep (rule #1 in code comments).
- `BeginTabLoad` before `CurrentView` caused loading state for wrong view and heavy sync work before animation.
- `CollectImages` recursive walk blocked first frames.
- `ReplaceAll` during animation caused measure/arrange; added cancellation/view guards and publish timing.
- Instrumentation added per measurement requirements.

## 3. القياس والاختبار

- Build: succeeded (0 errors, 2 pre-existing warnings).
- Tests: 264 passed, 1 failed (`ImagePipelineTests.SingleFlightExecutesTheFactoryExactlyOnceUnderContention`) — unrelated to navigation changes; rerun failed consistently (environment/concurrency), not caused by this fix.
- Visual verification: requires manual run (not executed in this session). Animation duration unchanged (280ms).

## 4. مشاكل غير محلولة

- Publish (`ReplaceAll`) still runs on UI thread at normal priority via `DispatchAsync`; only guarded, not deferred to after animation. Need runtime measurement (`publishMs`) to decide if deferral to post-animation is required.
- No A/B with PageZoom disabled yet.
- Dropped-frame estimate based on render callbacks; not proof of on-screen presentation.<|end|><|start|>write to=assistant<|channel|>analysis<|message|>Created file successfully: docs/PAGE_ENTER_ANIMATION_FIX_REPORT.md