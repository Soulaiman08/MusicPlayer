# M2.5 — Measurement Infrastructure Recovery
## إصلاح نظام القياس وتجهيز التحقيق الميداني التفاعلي

**التاريخ:** 17 نوفمبر 2026  
**المرحلة:** بنية قياس فقط — **لا إصلاحات أداء إنتاجية، ولا M3، ولا commit/push/merge/تبديل فرع.**  
**مرجع المراجعة السابقة:** `docs/M25_ROOT_CAUSE_REVALIDATION.md`

---

## 1. هوية النسخة المُختبرة (Build Identity)

| العنصر | القيمة |
| :--- | :--- |
| Command | `dotnet build src/MusicPlayer/MusicPlayer.csproj -c Release --no-restore -t:Rebuild` |
| SDK | `8.0.425` |
| Start | `2026-11-17T19:20:57.8959055+01:00` |
| End | `2026-11-17T19:22:06.2506382+01:00` |
| Duration | 68.64 s |
| النتيجة | `0 Error`, `4 Warning` (CS4014 في `MainWindow.xaml.cs:7022` و`:7119` — تحذيرات سابقة غير مسبّبة بهذه المرحلة) |
| Git HEAD | `18126e72e489c7a6f76e018260b48fb325b15174` |
| الفرع | `ui-redesign` |

### الملفات التنفيذية

| الملف | الحجم (بايت) | mtime | SHA-256 |
| :--- | ---: | :--- | :--- |
| `MusicPlayer.exe` | 322,048 | 2026-11-17T19:22:00.5602674+01:00 | `F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766` |
| `MusicPlayer.dll` | 36,165,632 | 2026-11-17T19:22:00.5175135+01:00 | `A972A4685D9B22E26180795F5C538A1C43ED88B1A9EAD81DE2CDE6E738B0883C` |
| `MusicPlayer.pdb` | 396,356 | 2026-11-17T19:21:59.8784568+01:00 | `4B220934117D6F8F65B9D1BAF515A632E9449100F979FEF4B037ADB01D28CDE4` |

### حالة Git (لم تُعدّل)
```
 M src/MusicPlayer/ViewModels/MainViewModel.cs
 M src/MusicPlayer/Views/MainWindow.xaml.cs
 M src/MusicPlayer/Views/NavigationChoreography.cs
?? docs/GLOBAL_PERFORMANCE_SMOOTHNESS_AUDIT.md
?? docs/M25_ROOT_CAUSE_REVALIDATION.md
?? docs/PAGE_ENTER_ANIMATION_DIAGNOSIS_REPORT.md
?? docs/PAGE_ENTER_ANIMATION_FIX_REPORT.md
```
diff: 3 ملفات، 201 insert / 56 delete. تفاصيل الـ diff محفوظة في `perf-results/m25/git-diff-protected.patch`.

> **تصحيح لملاحظة سابقة:** في `M25_ROOT_CAUSE_REVALIDATION.md` ذُكر أن EXE أقدم من تعديلات Working Tree. التحقق الأدق أظهر أن أزمنة ملفات المصدر هي `01:01:18–19` بينما مخرجات البناء الأصلية `01:01:51–52`، أي أن الثنائيات كانت **أحدث** من المصادر. وإعادة البناء الكاملة الآن تؤكد أن `SHA-256` أعلاه يمثل الكود الحالي. لم يُجرَّ أي تعديل على الملفات الثلاثة المحمية.

### سجل البناء
- `perf-results/m25/build.log`, `perf-results/m25/rebuild.log`
- `perf-results/m25/build-manifest.json` — يحتوي على **99 ملف مصدر** مع SHA-256 و mtime لكل منها، إضافة إلى هوية الثنائيات وحالة Git.
- `perf-results/m25/git-status.txt`, `git-diff-stat.txt`, `git-diff-protected.patch`

---

## 2. قائمة الأدوات المتاحة وغير المتاحة (مقاسة)

| الأداة | الحالة | ملاحظة |
| :--- | :--- | :--- |
| PowerShell 5.1 | ✅ متاح | `C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe` |
| .NET SDK 8.0.425 | ✅ متاح | `dotnet --version` |
| UI Automation Client/Types | ✅ متاح | استُخدم للتحقق من التبويب وعنصر التمرير |
| `GetThreadTimes` / `OpenThread` | ✅ متاح | تحققت منه باختبار spinning: 296.9ms CPU مقابل 309ms wall |
| `Get-Counter \GPU Engine(*)` | ✅ متاح | يتوفر مؤشر `engtype_3d` لكل PID على حدة |
| `wpr.exe` | ⚠️ موجود لكن **غير صالح بدون admin** | `-start CPU` فشل بخطأ `0xc5585011` (Failed to enable the policy to profile system performance) |
| WPR profiles GPU / DesktopComposition | ⚠️ مُعرَّفة (`wpr -providers` يعرض Dwm/DXGI/Win32k) | لا يمكن تشغيلها في الجلسة الحالية |
| `tracerpt.exe` | ✅ موجود | لكن لا يوجد ETL مُلتقط لتحليله |
| PresentMon | ❌ غير موجود | لا في PATH ولا في المسارات المعروفة |
| xperf | ❌ غير موجود | — |
| WPA | ❌ غير موجود | حتى مع ETL لا توجد أداة تحليل بصرية |
| dotnet-trace / dotnet-counters | ❌ غير موجود | — |
| صلاحية Administrator | ❌ `False` | المستخدم `DESKTOP-DO10R93\win` |

**النتيجة:** لا توجد حالياً أداة تثبت **الإطارات المعروضة فعلياً** (Present Events). لذلك كل ما يتعلق بـ FPS النهائي يبقى `N/A`.

---

## 3. تقرير جودة أداة القياس — العيوب والإصلاحات

### 3.1 تفعيل التبويبات (`Invoked=False` في 16/20 عينة)

**السبب المُشخَّص:** الهارنس القديم بحث عن أزرار بـ `Name = 'TRACKS' / 'ARTISTS' / ...`، لكن هذه الأسماء **غير معروضة في شجرة UIA**. فحص مباشر أظهر أن أزرار الشريط الجانبي تُعرَّف بـ `HelpText` (`All Songs`, `Folders`, `Favorites`, `Recent Plays`, ...) وأن أزرار المحتوى ليس لها `Name`/`AutomationId` موثوق.

**الإصلاح في `tools/perf/MeasureScrollVerified.ps1`:**
1. تفعيل عبر `HelpText` (`InvokePattern`) أو عبر إحداثيات يحددها المشغّل بعد تأكيد بصري.
2. **إثبات مستقل للصفحة الفعلية:** هوية عنصر التمرير الظاهر (`AutomationId`/`Name`) يجب أن تطابق الصفحة المتوقعة (مثال `songs` ⇒ `AllSongsListBox`).
3. البحث عن عنصر يدعم `ScrollPattern` بدلاً من افتراض `ControlType.ScrollViewer` (التطبيق يعرض `ControlType.List`).
4. سجل `NAV` يُستخدم كدليل إضافي إن وُجد، وليس كشرط (لأنه يظهر فقط مع PERFTEST).
5. **أي فشل ⇒ العينة تُستبعد كاملة** وتُكتب كـ `valid=False` مع السبب.

**التحقق:** تشغيل تجريبي على `songs / list / maximized` أعطى `2/2` عينات صالحة:
```
1,True,True,UIA-HelpText:All Songs,verified,,AllSongsListBox,64,150,1302,480,6,312.9,219,69.99,266,46.9,rendering_callbacks_only,0,...
2,True,True,UIA-HelpText:All Songs,verified,,AllSongsListBox,64,150,1302,480,6,301.1,234,77.71,312,46.9,rendering_callbacks_only,0,...
```
المحاولة الأولى قبل الإصلاح أعطت `INVALID: tab element 'TRACKS' not found` — أي أن منطق الاستبعاد يعمل فعلاً.

### 3.2 قياس CPU

**السبب المُشخَّص:** `RunGlobalAuditSuite.ps1` استخدم `$proc.TotalProcessorTime` (كل خيوط العملية) داخل نافذة تتضمن `Start-Sleep 2200ms` بعد آخر عجلة، بدقة 15.6ms.

**الإصلاح:**
- UI Thread ID يُؤخذ من `GetWindowThreadProcessId(hwnd)` (خيط مالك النافذة = خيط WPF).
- زمن CPU الخاص به يُقاس بـ `GetThreadTimes` (kernel+user) على مقبض ذلك الخيط فقط.
- `process_cpu` يُسجَّل كعمود منفصل كمؤشر مستقل فقط.
- نافذة القياس تنتهي عند آخر حدث عجلة — **بدون أي سكون لاحق**.
- تُسجَّل الفترات الحقيقية بين العينات (`actual_interval_ms`) بدلاً من افتراض الفاصل المُعدّ.

**التحقق من الأولية:** اختبار spin: `deltaCpuMs=296.9` مقابل `wallMs=309` ⇒ الأداة تقيس CPU فعلياً.

### 3.3 قياس الإطارات

**الإصلاح:** فصل صريح في كل مخرجات:
- `frameEvidence = rendering_callbacks_only` (عدد خطوط `SSCB burst_ms=` من السجل).
- `presentedFrames = UNAVAILABLE (no PresentMon / decoded ETW)`.
- `finalFps = N/A (not computed from callbacks)`.

**لم يُنتج أي رقم FPS نهائي.** لا تُسمَّى callbacks «إطارات معروضة».

---

## 4. الأدوات المُنشأة

| الملف | الغرض |
| :--- | :--- |
| `tools/perf/CaptureInteractiveScroll.ps1` | التقاط حول جلسة بشرية: أوضاع `Session`, `Start`, `Stop`, `Probe`, `Capabilities`, `Worker` |
| `tools/perf/MeasureScrollVerified.ps1` | قياس نبضات تمرير مع تحقق إلزامي من التبويب + CPU لخيط الواجهة |
| `docs/M25_INTERACTIVE_CAPTURE_RUNBOOK.md` | دليل تشغيل الجلسة الميدانية والبروتوكول |
| `perf-results/m25/build-manifest.json` | هوية البناء + 99 ملف مصدر مُجزّأة |
| `perf-results/m25/capabilities.json` | مخرجات فحص القدرات |
| `perf-results/m25/probes/**` | أمثلة سجلات خام (manifest + CSV) |
| `perf-results/m25/verified-test/**` | مثال CSV لعينات صالحة/غير صالحة |

---

## 5. أمثلة سجلات خام وتكلفة أدوات القياس (Instrumentation Overhead)

### 5.1 عينات صالحة من الأداة المصححة
الملف: `perf-results/m25/verified-test/verified-20261117-194102-songs-list-maximized.csv`

| Repeat |.valid | Method | UI Thread CPU | Wall | UI Busy | Process CPU | Longest sampled |
| ---: | :--- | :--- | ---: | ---: | ---: | ---: | ---: |
| 1 | ✅ | UIA-HelpText:All Songs | 219 ms | 312.9 ms | 69.99% | 266 ms | 46.9 ms |
| 2 | ✅ | UIA-HelpText:All Songs | 234 ms | 301.1 ms | 77.71% | 312 ms | 46.9 ms |

هذه أول أرقام في التحقيق تُنسَب فعلاً إلى **خيط الواجهة** أثناء نبضة تمرير محددة دون سكون لاحق.

### 5.2 مقارنة Instrumentation Disabled / Enabled (خمول فقط، بلا تمرير)

| الحالة | عينات | median interval | Σ interval | UI CPU | متوسط UI | Process CPU | متوسط Process | max UI |
| :--- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Disabled (لا env) | 63 | 77.9 ms | 9342.6 ms | 32 ms | 0.34% | 10533 ms | 112.74% | 2.88% |
| Enabled (`MUSICPLAYER_PERFTEST=1` فقط) | 95 | 75.0 ms | 8953.4 ms | 0 ms | 0% | 5345 ms | 59.7% | 0% |
| Enabled + warmup suppressed (الإعداد القديم) | 75 | 62.7 ms | 9180.1 ms | 0 ms | 0% | 832 ms | 9.06% | 0% |

**الاستنتاج الصادق:** المقارنة **غير حاسمة**. الفروق كبيرة لكنها تتحرك في الاتجاه المعاكس لما يُتوقع من «تكلفة instrumentation»، والسبب هو أن إعداد الهارنس القديم كان يجمّع بين `PERFTEST=1` وتعطيل Warmup؛ وحده تعطيل Warmup غيّر Process CPU من ~112% إلى ~9%. أي أن **متغير Warmup كان يهيمن على المقارنة**. كما أن UI CPU في الخمول يقع تحت أرضية دقة 15.6ms في معظم العينات.

**القرار:** لا تُخفَّض تكلفة الأداة ولا تُعتمد كـ Baseline بناءً على هذه المقارنة؛ يلزم تكرار المقارنة **أثناء تمرير فعلي** وبنفس حالة المكتبة ونفس عدد التكات، مع عزل `MUSICPLAYER_PERFTEST` وحده (وهو ما يفعله `-EnablePerfTest` في الأداة الجديدة).

---

## 6. حدود ما تستطيع الأداة إثباته

| تستطيع إثباته | لا تستطيع إثباته |
| :--- | :--- |
| هوية EXE/Zمن البناء/SHA-256 | الإطارات المعروضة فعلياً |
| PID و UI Thread ID وحجم النافذة | Present Time / DWM Present Events |
| UI Thread CPU أثناء نبضة محددة | FPS نهائي من Rendering callbacks |
| Process CPU كمؤشر مستقل | إسناد التكلفة إلى AncestorType/Converter/Glass |
| صلاحية التبويب قبل كل قياس | الإحساس البصري بالسلاسة |
| GPU 3D Engine لعملية MusicPlayer (عينات) | سبب الاختناق عند Maximized |
| اكتشاف عينات غير صالحة واستبعادها | محاكاة جلسة بشرية (لا تمرّر الأداة عن المستخدم) |

**رقمياً:** لم يُنتج أي `finalFps`، ولم يُصنَّف أي `rendering callback` كإطار معروض، ولم يُقدَّر أي رقم مفقود.

---

## 7. الفرضيات ما زالت محفوظة

لا يُعلن عن أي من الآتي كسبب مثبت في هذه المرحلة:

- DWM Transparency
- AncestorType bindings
- CircleClipConverter
- Glass
- Wallpaper

لم يُغيَّر أي XAML أو كود إنتاج. لم تُمسّ المناطق المحمية:
`InitializeAsync`، `WarmUpAndRefreshAsync`، `SmoothScrollHelper`، `WallpaperWarmupLoop`، `UpgradeArtistCache`، مسار تنظيف الحاويات الذي أغلق مشكلة الفجوات، `VirtualizingBalancedPanel`، `BalancedItemsPanel`. لم تُخفَّض جودة الصور، ولم يُعطَّل الزجاج أو الخلفيات.

---

## 8. التسليم

✅ تقرير جودة أداة القياس (هذا الملف + القسم 3)  
✅ سكربت الالتقاط `tools/perf/CaptureInteractiveScroll.ps1`  
✅ سكربت القياس المصحح `tools/perf/MeasureScrollVerified.ps1`  
✅ دليل الجلسة التفاعلية `docs/M25_INTERACTIVE_CAPTURE_RUNBOOK.md`  
✅ ملف EXE وهوية البناء (القسم 1 + `build-manifest.json`)  
✅ أمثلة سجلات خام (`perf-results/m25/probes`, `perf-results/m25/verified-test`, `capabilities.json`)  
✅ قائمة الأدوات المتاحة وغير المتاحة (القسم 2)  
✅ حدود ما تستطيع الأداة إثباته (القسم 6)

---

## 9. نقطة التوقف

**توقف هنا.** لم تبدأ مرحلة M3، ولم تُطبَّق إصلاحات إنتاجية، ولم تُنفَّذ تجارب A/B على الفرضيات. الخطوة التالية هي مراجعتك لنظام القياس، ثم جلسة التمرير الفعلية وفق `M25_INTERACTIVE_CAPTURE_RUNBOOK.md`، وبعدها فقط يمكن اختبار كل فرضية على حدة.

---
*كل قيمة في هذا التقرير إمّا مقاسة مباشرةً في هذه الجلسة أو منسوبة صراحةً إلى ملف خام محفوظ. ما لا يوجد رقم له سُجّل «غير متاح» أو `N/A`.*
