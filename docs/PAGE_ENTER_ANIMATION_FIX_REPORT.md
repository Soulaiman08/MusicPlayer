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

## 5. إعادة التحقق على نسخة Release الحالية — 2026-11-18

### هوية النسخة

تم تشغيل الملف الموجود في:
`src/MusicPlayer/bin/Release/net8.0-windows10.0.19041.0/win-x64/`

| الملف | SHA-256 | وقت تعديل الملف (UTC) |
|---|---|---|
| `MusicPlayer.exe` | `F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766` | 2026-11-18 17:07:11 |
| `MusicPlayer.dll` | `BCC6FCA323A5C64699B05856574F74324C24C9724A4C58C97BD02A5449F2816E` | 2026-11-18 17:07:11 |

### مراجعة مسارات النشر الحالية

- `Navigate` يغيّر `CurrentView` أولًا؛ يعالج `MainWindow` هذا التغيير ويبدأ حركة الدخول المتزامنة، ثم يبدأ تحميل الصفحة. مدة `MotionTokens.PageEnter` هي 280ms.
- المجموعات المخصصة لـFavorites وRecent وRecent Add وMost Played تُجهّز وتُرتّب خارج UI thread، ثم تنشر عبر `DispatchAsync` بنداء `ReplaceAll` واحد. حارس Cancellation Token وحارس `CurrentView` موجودان داخل callback. لذلك نتيجة تحميل صفحة سابقة من هذه المسارات لا تُنشر بعد إلغاء تنقلها.
- Folder Details يجلب البيانات في `Task.Run` ثم ينفذ `ReplaceAll` واحدًا على UI thread بعد فحص الـtoken. `GoBack` يبدأ token تنقل جديدًا ويلغي العمل السابق.
- `ReplaceAll` يصدر Reset دائمًا، حتى إن كانت القائمة فارغة أو مساوية للقائمة الحالية. هذا يثبت حدوث إشعار CollectionChanged، لكنه لا يقيس زمن Layout الناتج عنه.
- مسارات Albums وArtists وGenres تستخدم مسارات نشر أخرى؛ بعضها لا يستقبل navigation token. لا يوجد دليل في هذه الجولة على أن تحديث المجموعة المخفية منها يسبب تقطعًا، لذلك لم أغيرها.

### نتيجة التشغيل والقياس

| المشاهدة | النافذة | عدد العناصر | مدة callback الذي ينفذ النشر |
|---|---|---:|---:|
| دخول Favorites | Maximized، حدود النافذة 1382×754 | 7 | 0.84ms |
| دخول Favorites | عادية، حدود النافذة 1116×650 | 7 | 0.90ms |

سُجل `ContentGrid.Opacity=0.85` في `arrived-favorites` ثم `Opacity=1.00` في `settled-p800`. وسُجل نشر Favorites في الثانية نفسها التي سُجل فيها الوصول إلى الصفحة؛ هذا يتفق مع وقوع النشر أثناء نافذة الحركة، لكنه لا يحدد التوقيت بدقة كافية لإثبات التداخل مع إطاراتها. كما أن `publishMs` يقيس جسم callback فقط ولا يشمل Layout/Measure/Arrange اللاحق.

النسخة المعروضة للمكتبة أظهرت 201 مسارًا و7 Favorites وبطاقة واحدة للمجلدات. لم تُفعّل `MUSICPLAYER_PERFTEST`، لذلك لم تتوفر أزمنة `PAGE-ENTER-PREP` أو ملخصات `NAV-SUMMARY` في هذه الجولة. لا تُعامل callbacks على أنها إطارات معروضة.

### الاختبار البصري وما لم يكتمل

- أمكن التحقق من شجرة عناصر الواجهة وحمولة Favorites في النافذتين، لكن أداة Computer Use فشلت في التقاط صورة للنافذة؛ لذلك **لم أتمكن من تأكيد السلاسة بصريًا**.
- لم تكتمل حالات A→B السريع، الرجوع أثناء التحميل، Recent Add، Most Played، أو Folder Details. تعذر تفعيل بطاقات Home وفتح بطاقة المجلد عبر عناصر الوصول المتاحة، ولم أستخدم إحداثيات تخمينية أو وسيلة تحكم بديلة.
- لم يُشغّل Build أو Unit Tests في هذه الجولة؛ لم يتغير كود الإنتاج.

### الحكم والإجراء

النتيجة الحالية تؤكد أن callback نشر Favorites قد يقع قرب حركة الدخول، لكنها **لا تثبت أن النشر أو Layout الناتج عنه هو سبب التقطّع**. زمن النشر المقاس أقل من 1ms لقائمة من 7 عناصر، ولا يوجد قياس مستقل لكلفة Layout أو دليل بصري قبل/بعد. لذلك لم أؤجل `ReplaceAll` ولم أضف تعديلًا إنتاجيًا تخمينيًا. تبقى فرضية أن نشر مجموعات أكبر أو Layout الناتج عنه يسبب التقطّع غير محسومة حتى يتوفر اختبار بصري وقياس توقيت/كلفة Layout على الحالات المطلوبة.<|end|><|start|>write to=assistant<|channel|>analysis<|message|>Created file successfully: docs/PAGE_ENTER_ANIMATION_FIX_REPORT.md