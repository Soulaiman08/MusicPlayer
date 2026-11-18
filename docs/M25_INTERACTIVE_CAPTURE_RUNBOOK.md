# M2.5 — دليل تشغيل جلسة الالتقاط التفاعلية

## الغرض

هذا الدليل يشرح كيف تجري **جلسة تمرير فعلية بيد المستخدم** مع التقاط قياسات موثوقة حولها.

**الأدوات:**
- `tools/perf/CaptureInteractiveScroll.ps1` — يلتقط السياق والـ CPU/Zمن حول جلسة المستخدم.
- `tools/perf/MeasureScrollVerified.ps1` — يقيس نبضات تمرير آلية مع **تحقق إلزامي من التبويب**.

**قاعدة ذهبية:** الأداة **لا تتمرّر نيابة عنك**، ولا تحكم على السلاسة البصرية، ولا تغيّر إعدادات البرنامج. أنت من يحرّك الفأرة، والأداة تسجّل فقط.

---

## 0. قبل البدء — هوية النسخة

```
EXE : src\MusicPlayer\bin\Release\net8.0-windows10.0.19041.0\win-x64\MusicPlayer.exe
SHA256: F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766
DLL SHA256: 83A8B745CB93B5A262CA8EB0A629005993E67BFB44096A99417A455366B1D29B
 rebuilt: 2026-11-18T15:43:57+01:00  (dotnet build -c Release -r win-x64 --no-restore -t:Rebuild)
```

لا تستخدم أي EXE أقدم من هذا المعرّف. لا تعديل على الملفات المحمية الثلاثة، ولا commit/push/merge/تبديل فرع.

---

## 1. تجهيز المكتبة

- `fixture201`: `C:\Users\win\AppData\Local\Temp\opencode\fixture201\data`
- `fixture5000`: `C:\Users\win\AppData\Local\Temp\opencode\fixture5000\data`

مرّر `MUSICPLAYER_DATA_DIR` إلى البرنامج عند الإقلاع. الأداة تفعل ذلك تلقائياً مع `-LaunchApp`.

---

## 2. جلسة المستخدم — وضع التسجيل المستمر (`Start` / `Stop`)

لجلسة المستخدم اليدوية على مكتبة التطبيق الافتراضية، ابدأ من مجلد المشروع:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\CaptureInteractiveScroll.ps1 `
  -Action Start `
  -LaunchApp `
  -UseDefaultLibrary `
  -EnableScrollProbe `
  -ScenarioName 'manual-small-medium-max' `
  -IntervalMs 50 `
  -OutDir 'perf-results\interactive'
```

ما يحدث:
1. تتحقق الأداة من عدم وجود نسخة MusicPlayer مفتوحة، ثم تقلّع نسخة Release دون تغيير المكتبة الافتراضية أو إعدادات warmup.
2. تطبع `CAPTURE STARTED` مع PID و UI Thread ID وحجم منطقة العميل ومسار `capture-state.json`.
3. **أنت** تغيّر حجم النافذة وتفتح الواجهات وتنفذ التمرير والحركات التي تريد تسجيلها؛ الأداة لا ترسل أي إدخال.
4. بعد أن ترسل أنك انتهيت، أوقف التسجيل بواسطة `-Action Stop -StateFile '<المسار المطبوع>'`.
5. يُحفظ `capture-manifest.json` و `ui-thread-samples.csv` و `scroll-bursts.csv` و `scroll-frames.csv` في مجلد الجلسة. يبقى التطبيق مفتوحًا بعد إيقاف العينات الخارجية.

مثال الإيقاف:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\CaptureInteractiveScroll.ps1 `
  -Action Stop `
  -StateFile 'perf-results\interactive\<timestamp>-manual-small-medium-max\capture-state.json'
```

`scroll-bursts.csv` يسجل دفعات `ScrollChanged` الفعلية، هوية ScrollViewer/ItemsControl
ونوع اللوحة، حجم النافذة وحالتها، offset، وعدد/فواصل WPF rendering callbacks.
`scroll-frames.csv` يسجل توقيت كل callback وoffset المقابل أثناء الدفعة.
هذا معدل callbacks وليس FPS معروضًا؛ `presentedFps` يظل `NA` بدون PresentMon أو
أحداث Present مفككة. ملفات الالتقاط ترتبط عبر UTC. CPU/RAM وحجم النافذة يُسجّلون
كل 50ms، وGPU 3D الخاص بالعملية يُحدّث كل 250ms.

يتطلب `-EnableScrollProbe` إطلاق نسخة جديدة لأن متغيرات البيئة تُقرأ عند بدء
التطبيق. وضع `Start` لا يقتل نسخة موجودة؛ إذا كان التطبيق مفتوحًا، أغلقه طبيعيًا
قبل بدء جلسة جديدة.

### وضع جلسة Enter (`Session`)

للاستخدامات الأخرى يمكن تشغيل وضع `Session` القديم؛ عند ضغط Enter ينهي هذا الوضع
الجلسة التي أطلقها. لا تستخدمه إذا أردت إبقاء التطبيق مفتوحًا بعد الالتقاط.

### فحص القدرات (بدون أي تشغيل)

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\CaptureInteractiveScroll.ps1 -Action Capabilities
```

---

## 3. ماذا يسجّل الالتقاط؟

`capture-manifest.json` يحتوي على:
- وقت البدء/الانتهاء، اسم الـ EXE ومساره و SHA-256.
- PID و UI Thread ID (مأخوذ من `GetWindowThreadProcessId` للنافذة).
- عنوان النافذة وحجم منطقة العميل.
- حالة GPU (وحدات التحكم، الشاشة، مؤشر `engtype_3d` **خاص بعملية MusicPlayer فقط**).
- قائمة الأدوات المتاحة/غير المتاحة وصلاحية Administrator.
- حالة ETW/WPR (إذا طُلب `-EnableEtw`) ومخرجاته أو سبب الفشل.
- ملاحظة صريحة: **لم يُجرَّ أي اختبار بصري ولا تمرير من قِبل الأداة**.

`ui-thread-samples.csv` يحتوي على:
- `utc_time`, `t_rel_ms`, `actual_interval_ms` (الفاصل الحقيقي بين العينات، لا الفاصل المُعدّ).
- `ui_thread_cpu_delta_ms`, `ui_busy_pct` — من `GetThreadTimes` على خيط الواجهة.
- `process_cpu_delta_ms` — مؤشر مستقل فقط، **ليس** زمن UI Thread.
- Working Set و Private Bytes وحجم نافذة العميل ومؤشر GPU 3D للعملية.
- GPU 3D يُحدّث كل 250ms، و`gpu_sample_age_ms` يوضح عمر آخر عينة بين التحديثات.

`scroll-bursts.csv` يسجل callback rate وفواصلها المئوية/العظمى و`ScrollChanged`
أثناء حركة كل دفعة؛ هذه callbacks وليست FPS للشاشة أو إثباتًا لإطارات معروضة.

> تنبيه: `GetThreadTimes` مقيد بدقة ساعة Windows (~15.6ms). استخدم المجاميع (`totalUiCpuMs`, `meanUiBusyPct`) بدلاً من العينات الفردية عند انخفاض الحمل.

---

## 4. قياس نبضات التمرير آلياً (`MeasureScrollVerified`)

هذه أداة **قياس ميكانيكي**، وليست بديلاً عن إحساس المستخدم. تستخدم فقط عندما تريد أرقاماً قابلة للمقارنة لنفس عدد التكات.

```powershell
# تأكد أن البرنامج يعمل أولاً، ثم:
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\MeasureScrollVerified.ps1 `
  -ExpectedView songs -ViewMode list -WindowSize maximized `
  -Repeats 5 -WheelCount 25 -WheelDelayMs 35 `
  -DataDir 'C:\Users\win\AppData\Local\Temp\opencode\fixture5000\data' `
  -OutDir 'perf-results\m25\verified'
```

### شروط قبول العينة (إلزامية)
1. تفعيل التبويب عبر UIA (`HelpText`) أو نقرة إحداثيات يحددها المشغّل بعد رؤية الشاشة.
2. **إثبات مستقل للصفحة الفعلية:** هوية عنصر التمرير الظاهر (`AutomationId`/`Name`) يجب أن تطابق الصفحة المتوقعة. مثال: songs ⇐ `AllSongsListBox`.
3. وجود عنصر ظاهر يدعم `ScrollPattern` (قد يكون `ControlType.List` وليس `ScrollViewer`).
4. سجل `NAV` يُستخدم كدليل إضافي إن وُجد، ولا يُشترط (لأنه يظهر فقط مع PERFTEST).

**أي فشل في أي شرط ⇒ تُستبعد العينة بالكامل وتُسجَّل كـ `valid=False` مع السبب.** لا تُستخدم عينات غير صالحة في أي متوسط.

### خريطة التفعيل المعروفة

| الصفحة | HelpText / المعرّف المتوقع |
| :--- | :--- |
| songs | `All Songs` ⇒ `AllSongsListBox` |
| folders | `Folders` |
| favorites | `Favorites` |
| recent | `Recent Plays` |
| artists / albums / genres / recent_add / most_played | لا معرّف UIA موثوق ⇒ استخدم `-ClickX`/`-ClickY` بعد تأكيد بصري، مع إثبات هوية العنصر |

### أعمدة الناتج المهمة
- `uiThreadCpuMs`, `uiBusyPct` — زمن خيط الواجهة أثناء نبضة التمرير **فقط** (بدون سكون بعدها).
- `processCpuMs` — مؤشر مستقل، لا يُنسب إلى UI Thread.
- `frameEvidence = rendering_callbacks_only` — **ليست إطارات معروضة**.
- `presentedFrames = UNAVAILABLE`، `finalFps = N/A` — لأن PresentMon/ETW Present غير متاحين.

---

## 5. بروتوكول الجولة الميدانية (للمشغّل البشري)

| البعد | القيم |
| :--- | :--- |
| الصفحات (بالترتيب) | All Songs List، All Songs Grid، Artists Grid، Albums Grid |
| أحجام النافذة | Small (أصغر حجم مسموح)، Medium 1280×720، Maximized |
| المكتبات | `fixture201`، ثم `fixture5000` (الأولوية للأخيرة) |
| نمط التمرير | Slow، Medium، Fast، Continuous 30s، نهاية←بداية، تبديل تبويب←عودة |
| الحالة | Cold و Warm |
| التكرار | ≥5 لكل خلية، مع تغيير ترتيب التشغيل لتجنّب تحيّز الكاش |
| التوثيق البصري | تسجيل شاشة إن توفر، مع طابع زمني مطابق لبداية/نهاية الالتقاط |

> لا تُنتج الأداة «إحساساً بصرياً». ملاحظة الاختناق عند Maximized تبقى مدخلاً بشرياً يجب تسجيله يدوياً في التقرير النهائي.

---

## 6. مقارنة تكلفة القياس نفسها

```powershell
# بدون instrumentation
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\CaptureInteractiveScroll.ps1 `
  -Action Probe -LaunchApp -ProbeSeconds 10 -ScenarioName overhead-off -OutDir perf-results\m25\probes

# مع PERFTEST فقط (بدون SuppressWarmup)
powershell -NoProfile -ExecutionPolicy Bypass -File tools\perf\CaptureInteractiveScroll.ps1 `
  -Action Probe -LaunchApp -EnablePerfTest -ProbeSeconds 10 -ScenarioName overhead-on -OutDir perf-results\m25\probes
```

> `-EnablePerfTest` يضبط **فقط** `MUSICPLAYER_PERFTEST=1`. أضف `-SuppressWarmup` فقط إذا أردت محاكاة إعدادات الهارنس القديم، لكن هذا يغيّر سلوك الإقلاع ولذلك **لا يصلح كـ A/B للـ instrumentation وحدها**.

قارن `meanUiBusyPct` و`meanProcessBusyPct` و`rows`/`actualIntervalMedianMs`. إذا تغيّرت الأرقام بدرجة مؤثرة، خفّض `-IntervalMs` أو أزل المؤثر قبل اعتماد أي Baseline.

---

## 7. حدود ما تستطيع الأداة إثباته

| يستطيع إثباته | لا يستطيع إثباته |
| :--- | :--- |
| هوية EXE/Zمن البناء | الإطارات المعروضة فعلياً على الشاشة |
| PID/UI Thread ID/حجم النافذة | Present Time أو DWM Present Events (بدون PresentMon/ETW مُفكَّك) |
| CPU الحقيقي لخيط الواجهة أثناء نبضة محددة | FPS نهائي من Rendering callbacks |
| CPU العملية كمؤشر مستقل | إسناد CPU إلى AncestorType/Converter/Glass تحديداً |
| صلاحية التبويب قبل القياس | الإحساس البصري بالسلاسة |
| GPU 3D Engine لعملية MusicPlayer (عينات) | سبب الاختناق عند التكبير (ما زال فرضية) |

---

## 8. قواعد حماية أثناء الجلسة

- لا تعديل على `MainViewModel.cs` / `MainWindow.xaml.cs` / `NavigationChoreography.cs`.
- لا لمس `InitializeAsync` / `WarmUpAndRefreshAsync` / `SmoothScrollHelper` / `WallpaperWarmupLoop` / `UpgradeArtistCache` / مسار تنظيف الحاويات.
- لا XAML إنتاجي، لا خفض جودة الصور، لا تعطيل الزجاج.
- لا commit / push / merge / تبديل فرع.
- لا إعلان عن سبب مثبت قبل اكتمال A/B المعزول.
