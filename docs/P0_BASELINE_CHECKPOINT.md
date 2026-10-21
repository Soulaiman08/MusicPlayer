# P0 — Baseline & Safe Checkpoint (وثيقة مرجعية)

> **التاريخ:** 2026-10-20 | **الحالة:** منفذة ومتحقق منها | **آخر تحديث:** نفس اليوم
> هذه الوثيقة هي مرجع الرجوع الآمن قبل أي عمل على إعادة تصميم الواجهة (خارطة P0–P7).
> **قاعدة العمل:** لا تُحذف ولا تُستبدل أي ملفات؛ القفزات بين الحالات تتم عبر Git فقط.

---

## 1. آلية الحفظ الآمنة المعتمدة (Safe Checkpoint Mechanism)

بحسب حالة المستودع (فرع `main` نظيف ومحدّث مع `origin/main` + عمل غير محفوظ في شجرة العمل)،
الآلية المعتمدة هي **فرع مخصص + commit + tag**:

| عنصر | القيمة |
|---|---|
| الفرع الجديد لكل أعمال إعادة التصميم | `ui-redesign` |
| نقطة الحفظ (Commit) | `328c3d5476850c860fe95fa4b82e00fe35ee5e71` |
| Tag المرجعي | `p0-baseline-20261020` (annotated) |
| حالة `main` بعد الحفظ | **لم تتغير** — لا يزال عند `5d3c759 "EEE"` |
| حالة الشجرة بعد الحفظ | `working tree clean` |
| عمليات مدمرة | **صفر** — لم يُنفَّذ أي أمر reset/clean/checkout مدمر؛ **لم يُدفع** إلى `origin` |

### الملفات المحفوظة في نقطة الحفظ (7 ملفات، +1998/−445)

| الملف | التغيير |
|---|---|
| `src/MusicPlayer/App.xaml` | +1 (تسجيل `ColumnHandleVisibilityConverter`) |
| `src/MusicPlayer/Converters/Converters.cs` | +41 (المحول أعلاه) |
| `src/MusicPlayer/Themes/APKTheme.xaml` | +117 (`APK_QueueListBox` منفصل + قالب `APK_VirtualListBox` بدون inset الـ1px) |
| `src/MusicPlayer/Views/MainWindow.xaml` | 838 سطراً متغيراً (مشهد المرحلة 3 للجداول) |
| `src/MusicPlayer/Views/MainWindow.xaml.cs` | 814 سطراً (+/−) (Header sync مشتركة + قوائم الأعمدة) |
| `src/MusicPlayer/Views/Controls/TrackTable.cs` | **جديد** (149 سطراً) — `TrackTableState` + attached property |
| `src/MusicPlayer/Views/Controls/TrackTableHeader.cs` | **جديد** (483 سطراً) — الرأس المشترك |

> ملاحظة: ملف `src/MusicPlayer/Views/MainWindow.xaml.bak` **مُتتبَّع سابقاً وغير مطابق لـ HEAD** —
> لا يُستخدم كنقطة رجوع إطلاقاً؛ النقاط الفعلية: `git` وعلامة `p0-baseline-20261020`.

---

## 2. حالات الرجوع (Rollback Recipes)

```powershell
# (أ) العودة لمحتوى ما-قبل-إعادة-التصميم تماماً (= 5d3c759):
git checkout main

# (ب) العودة للعمل على إعادة التصميم:
git checkout ui-redesign

# (ج) إلغاء كل أعمال إعادة التصميم نهائياً (مدمرة — نداء من المستخدم بنفسه):
git checkout main
git branch -D ui-redesign
git tag -D p0-baseline-20261020

# (د) نقل الحفظ إلى main لاحقاً عند الاعتماد النهائي:
git checkout main; git merge ui-redesign
```

فروع/تذاكر أمان قديمة سارية أيضاً:
`checkpoint-perf-fix-20261016` (فرع)، `backup-pre-optimization-20261017` (فرع).

---

## 3. البناء المرجعي (Reference Build)

| التوقيت | النتيجة | المدة | ملاحظة |
|---|---|---|---|
| قبل الحفظ (شجرة عمل dirty) | **Build succeeded — 0 أخطاء، 0 تحذيرات** | 4.31s | `dotnet build -c Debug` |
| بعد الحفظ (شجرة نظيفة `328c3d5`) | **Build succeeded — 0 أخطاء، 0 تحذيرات** | 20.74s | نفس الأمر؛ المحتوى متطابق |

بيئة البناء: .NET SDK `8.0.425` — TFM `net8.0-windows10.0.19041.0` — WPF — PerMonitor HDPI.
`MusicPlayer.exe` موجود في `bin/Debug/net8.0-windows10.0.19041.0/`. سكربت التشغيل: `run.ps1`
(يبني باسم `MusicPlayerFinal` إلى `bin/RunFinal` ويطلق عبر `dotnet`)، ومتاح كذلك `tools/`.
تصرّف البناء المعروف: إذا كان التطبيق مشغلاً يفشل نسخ الـ exe (MSB3021) — تحقّق
بـ `Get-Process *MusicPlayer*` قبل البناء.

---

## 4. ما مُثبت في هذه النقطة وما بقي غير مثبت

### مُثبت (Verified)
1. حالة Git قبل/بعد الحفظ (جداول أعلاه) + نظافة شجرة العمل + عدم مساس `main`.
2. البناء المرجعي 0/0 قبل وبعد الحفظ.
3. سلامة خط الصوت: `git diff` نظيف بالكامل على `Audio/` و`DSP/` و`Services/PlaybackService.cs`.
4. أدلة التقارير التقنية القديمة (سلاسة التمرير، قياسات الأداء) موجودة في:
   `FINAL_VALIDATION_REPORT.md` + `AUDIT_EVIDENCE_MEASUREMENTS.md` (خط أساس P7).
5. جرد الواجهات الموث في التقرير المرجعي (40 مفتاح CurrentView + اللوحات) — منهجياً سليم.

### غير مُثبت (بانتظار تشغيل فعلي)
1. **لا لقطات مرجعية بعد** — قرار تشغيل التطبيق لالتقاطها معلّق على موافقة المستخدم.
2. حالة الجداول المرئية والتزامن (رأس/صفوف/مقابض) بعد تعديلات المرحلة 3 — **قُرئت كودياً فقط**،
   لم تُشغَّل بصرية.
3. حجم مكتبة أيام المستخدم الحالية وأداء التمرير الفعلي عليها (تُقاس عند التشغيل، خط الأساس المرجعي
   كان 193 على قدماً من 2026-09/10).
4. أي اختبار وظيفي زمني (audio/lyrics/queue drag/hero) — **لم يُجرَّب أي منهم في هذه الجلسة**.

سجل سابق مكثّف إن احتُجت: `%AppData%\Roaming\MusicPlayer\Logs\info_YYYYMMDD.log`
(arrived/settled، HeroTransition، [Wallpaper]، PERF anim-loop، WARMUP).

---

## 5. قائمة «Out-of-Scope Findings» المعروفة (سجل فقط — لا إصلاح ضمن إعادة التصميم)

1. **Queue drag — الصف الفارغ بعد ~4 ثوانٍ**: بلا إصلاح جذري في الكود الحالي؛
   `CalculateExpectedDisplacement` يحمل ثابتاً صلباً `60.0px` (MainWindow.xaml.cs:4141) بينما
   ارتفاع الصف الفعلي `48px` (MainWindow.xaml:5492). التفاصيل: `QUEUE_DRAG_BUG_REPORT.md`.
2. **إعداد LyricsSpeedOffset** موجود ومحفوظ (SettingsService.cs:156) لكنه **لا يُطبق** على توقيت
   الكلمات (معامل `offsetMs` في `LyricParser.GetLineAtTime/GetLineIndex` كود ميت).
3. **Crossfade / Gapless**: `CrossfadeDuration`/`GaplessPlayback` معلنة في `AudioEngine.cs:54-55`
   وغير منفذة فعلياً (flag only؛ تغيير المسار يعيد بناء السلسلة).
4. إعدادات ميتة متبقية داخل الإعدادات (BT-lyrics/headset/desktop-lyrics…) — وثّقت لاحقاً في
   `SESSION_HANDOFF.md §14`؛ إعادة التصميم لا تعالجها إلا بموافقة منفصلة.

---

## 6. البوابات والخطوات التالية

| البوابة | الحالة الراهنة |
|---|---|
| Gate 1 (خطة قبل التنفيذ) | **مُرّت** — التقرير المرجعي اعتمد مرجعاً للتخطيط |
| P0 (نقطة الحفظ) | **منفذة في هذه الوثيقة** |
| **مراجعة المستخدم لنقطة الأمان واعتمادها** | **توقف هنا — مراجعة واعتماد المستخدم مطلوبان قبل مواصلة P1/P2** |
| P1 (Design System — لا تعديل واجهات) | الخطة للعرض؛ **لا تبدأ قبل موافقة المستخدم** |
| P2 (PlaylistDetail prototype) | **موقوفة على مراجعة + اعتماد نقطة الأمان** ثم موافقة نتيجتها |
| Gate 2 / 3 / 4 | كما في التقرير المرجعي — لا تعديل |

> **قاعدة صارمة انتهاءً لهذه المرحلة:** أي عمل قادم يتم **فقط في فرع `ui-redesign`**، ويُحفظ
> بنقطة حفظ (commit) بعد كل مرحلة مقبولة. ممنوع الدمج إلى `main` قبل Gate 4.
