# M2.5 — نتائج اختبار التمرير الحي والتقاط الأداء

**الحالة: جولة جزئية؛ ليست Baseline مكتملة ولا تسمح بإغلاق التحقيق.** شُغّل التطبيق فعليًا على `fixture201` ونُفذت تمريرات بعجلة الفأرة مع التحقق من هوية صفحة التمرير. لم تكتمل اختبارات `fixture5000`، أو Small، أو جميع الصفحات، أو عودة Small. لا يُستنتج سبب جذري من هذه البيانات.

## 1. هوية التطبيق والجهاز

| البند | النتيجة |
|---|---|
| EXE | `src\MusicPlayer\bin\Release\net8.0-windows10.0.19041.0\win-x64\MusicPlayer.exe` |
| SHA-256 | `F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766` |
| وقت EXE | `2026-11-17 19:22:00 +01:00` |
| البناء | Release، SDK `8.0.425`؛ كل ملفات المصدر المدرجة في `build-manifest.json` تطابق البصمات الحالية، لذلك لم يُعد البناء |
| PID / UI Thread | `3828` / `9908` |
| المكتبة المستخدمة | `C:\Users\win\AppData\Local\Temp\opencode\fixture201\data` فقط |
| النظام | Windows 10 Enterprise LTSC `10.0.19044` |
| CPU / RAM | Intel Core i5-4210U، نواتان/4 خيوط؛ ذاكرة ظاهرة نحو 7.9 GiB |
| محولات العرض | Intel HD Graphics Family (السائق `10.18.15.4248`) وNVIDIA GeForce 820M (السائق `23.21.13.9124`) |
| وضع العرض النشط المرصود | `1366×768`, `60 Hz` على محول Intel |

حالة Working Tree كانت متسخة قبل العمل، بما فيها الملفات المحمية `MainViewModel.cs` و`MainWindow.xaml.cs` و`NavigationChoreography.cs`. لم أعدّلها، ولم أعدّل الإنتاج أو سكربتات القياس، ولم أنفذ commit أو push أو merge أو تبديل فرع. اكتمل Warmup الخاص بالتطبيق في سجل `fixture201`، ولم تُعطّل أي مهمة خلفية عمدًا.

## 2. تنفيذ الجلسة والقيود

- شُغّل التطبيق بمتغير `MUSICPLAYER_DATA_DIR` الموجّه إلى fixture معزول. لم يُشغّل على قاعدة مكتبة المستخدم الحقيقية. لم تُشغّل `fixture5000` في هذه الجولة.
- نُفّذ `Capabilities` ثم Probe قصير بمسار `-OutDir` مطلق. أنشأ السكربت CSV وJSON وState في `perf-results\m25\live-tests\preflight\...`، ما تحقق منه قبل القياسات. Probe مدته ثانيتان لم ينتج عينات (CSV يحتوي الرأس فقط)، لذا استُبعد.
- تعذر أول Probe لأن السكربت لم يقبل `-ExePath` مطلقًا مع تركيبه الداخلي `Join-Path`; استُخدم مسار EXE نسبي كما يتوقع السكربت، مع إبقاء كل `-OutDir` مطلقًا.
- استُخدمت أوضاع `Start` و`Stop` في الالتقاط؛ ولكل تشغيل مسار مستقل صريح تحت `perf-results\m25\live-tests\`. استُخدم `MeasureScrollVerified.ps1` لنبضات `mouse_event` فعلية بعد أن لم تحرك محاولات Computer Use Scroll المحتوى المرئي. ثبت تغيّر الصفوف في All Songs List من Track 00001 إلى Track 00189 أثناء التمرير، كما بقيت هوية `AllSongsListBox` صحيحة. وفي Grid ثبتت هوية `SongsGridScroller` وظهر Track 00009 بعد جلسة Medium.
- الحجم Medium نُفّذ كنافذة `1280×720` (Client `1264×712`). Maximized كان Client `1366×738` وViewport الشبكة `1302×523`. أصغر حجم معلن في XAML هو `1116×650`؛ لم أتمكن من ضبطه عبر الواجهة، لذا لم تُنفّذ حالتا Small أو Return Small. أبلغت أدوات سطح المكتب عن إدخال مستخدم متزامن أثناء محاولات متابعة تغيير الحجم؛ أوقفت التفاعل بعد ذلك.
- استُخدم تسجيل نافذة محلي عبر FFmpeg/GDI. التسجيلات 15fps هي **تسجيل شاشة فقط وليست إثبات FPS للتطبيق**. لا يوجد وقت مطبوع داخل الفيديو؛ ربط الوقت بملفات الالتقاط مستند إلى أوقات ملفات النظام وManifest، لا إلى Overlay مرئي.

## 3. القياسات الصالحة المتاحة

قيم UI CPU وProcess CPU ومدة الحدث من مخرجات `MeasureScrollVerified` للفترة التي أُرسلت فيها أحداث العجلة. قياسات `CaptureInteractiveScroll` المحيطة أوسع قليلًا وتتضمن تفعيل الصفحة/بدء العامل؛ تُعرض منفصلة ولا تُخلط مع زمن الحدث.

| المكتبة | الصفحة/النمط | الحجم | عجلات/تكرارات | مدة الحدث | UI CPU / Busy | Process CPU | Viewport | جودة العينة/التحقق |
|---|---|---|---:|---:|---:|---:|---:|---|
| fixture201 | All Songs List، Continuous | Medium | 600 / 1 | 39.842 s | 10.750 s / 26.98% | 14.141 s | `1200×454` | `valid=True`؛ `AllSongsListBox`؛ حركة مرئية في الصفوف. فترة Capture المحيطة أطول من الحدث، فلا تستخدم متوسطاتها للمقارنة |
| fixture201 | All Songs Grid، Fast | Medium | 25 × 5 | متوسط 1.316 s/تكرار | متوسط 0.947 s / 71.94% | متوسط 1.575 s | `1200×497` | 5/5 `valid=True`؛ `SongsGridScroller`؛ كل تكرار احتوى 25 عجلة بفاصل 35ms |
| fixture201 | All Songs Grid، Continuous | Medium | 600 / 1 | 38.591 s | 1.984 s / 5.14% | 3.203 s | `1200×497` | `valid=True`؛ `SongsGridScroller`؛ تغير الصف المعروض بعد التمرير |
| fixture201 | All Songs Grid، Continuous | Maximized | 600 / 1 | 38.455 s | 1.703 s / 4.43% | 2.984 s | `1302×523` | `valid=True`؛ `SongsGridScroller`؛ لم يُتحقق مستقلًا من إزاحة مرئية أثناء الحدث |

تكرارات Grid Fast الخمسة (UI CPU / Busy / Process CPU / wall): `1047/74.43%/1969/1406.7ms`، `891/70.66%/1391/1261ms`، `906/71.99%/1453/1258.6ms`، `938/69.68%/1531/1346.1ms`، `953/72.92%/1531/1306.9ms`.

### مصفوفة الحجم المطلوبة

| المكتبة | التبويب | Small | Medium | Maximized | Return Small |
|---|---|---|---|---|---|
| fixture201 | All Songs List | لم يُنفذ | Continuous أعلاه؛ Fast لم يُقبل | لم يُنفذ | لم يُنفذ |
| fixture201 | All Songs Grid | لم يُنفذ | Fast ×5 وContinuous أعلاه | Continuous أعلاه | لم يُنفذ |
| fixture201 | Artists Grid | لم يُنفذ | Fast ×5 صالح | لم يُنفذ | لم يُنفذ |
| fixture201 | Albums Grid | لم يُنفذ | تمرير مباشر متحقق؛ لا قياس نبضات صالح | لم يُنفذ | لم يُنفذ |
| fixture201 | Favorites | لم يُنفذ | Fast ×5 صالح | لم يُنفذ | لم يُنفذ |
| fixture201 | Recent Play | لم يُنفذ | Fast ×5 صالح | لم يُنفذ | لم يُنفذ |
| fixture201 | Recent Add | لم يُنفذ | Fast ×5 صالح؛ تفاوت زمني كبير | لم يُنفذ | لم يُنفذ |
| fixture201 | Most Played | لم يُنفذ | Fast ×5 صالح؛ زمن التكرار مرتفع | لم يُنفذ | لم يُنفذ |
| fixture201 | Folders / Folder Details | لم يُنفذ | صفحة المجلد غير قابلة للتمرير؛ التفاصيل مرّت مباشرة | لم يُنفذ | لم يُنفذ |
| fixture201 | Home | لم يُنفذ | لا يوجد Scroll عمودي عند Medium | لم يُنفذ | لم يُنفذ |
| fixture5000 | جميع الصفحات | لم يُنفذ | لم يُنفذ | لم يُنفذ | لم يُنفذ |

لم تُنفذ Slow Scroll، أو End↔Top، أو Fullplayer/Lyrics/Visualizer، أو خمس تكرارات لكل خلية؛ التكرارات الخمسة اقتصرت على Fast في الصفحات المذكورة. عُيّن 600 حدث بفاصل 50ms للجولة المستمرة؛ الزمن الفعلي المقاس نحو 38.5–39.8 ثانية، وليس 30 ثانية بالضبط.

## 4. جودة الالتقاط والقيود القياسية

- ملفات `ui-thread-samples.csv` تسجل الفواصل الفعلية؛ في الجولات المستمرة النظيفة نسبيًا كان وسيط الفاصل `75.8ms` (Medium Grid) و`65.3ms` (Max Grid)، مع P95 `83.4ms` و`80.4ms`. دقة `GetThreadTimes` محدودة بساعة Windows؛ أطول فرق CPU عينةً ليس مدة تعليق Dispatcher دقيقة.
- التقطت ملفات Capture المحيطة لـ Grid Medium وMax على التوالي 643 و641 صفًا، ومتوسط UI Busy `10.68%` و`9.98%`، ومتوسط Process Busy `17.68%` و`17.45%`. نافذتا Capture (`51.72s` و`49.81s`) أطول من حدث العجلة نفسه؛ لذلك الأرقام الدقيقة للمقارنة هي صفوف القياس الميكانيكي أعلاه، لا متوسط Capture الكامل.
- محاولة List Medium الأولى لا تحتوي تحقق حركة؛ ومحاولة UIA Scroll وحدها لم تحرك الصفوف. Probe الثانيتين صفر عينات. كلتاهما مستبعدتان.
- **عيب في CSV الخام:** `MeasureScrollVerified.ps1` يكتب `scrollElementRect` بأربع قيم مفصولة بفواصل دون اقتباس؛ لذلك الصفوف تحتوي 22 حقلًا مقابل 19 في الرأس، وتنزاح الأعمدة عند مستهلك CSV عادي. احتُفظ بالملفات الأصلية دون تعديل، واستُخرجت قيم الجدول من ترتيب الحقول ومخرجات السكربت. لا تعتمد على استيراد CSV المباشر لهذه الملفات دون تطبيع.
- `working_set_mb` و`private_mb` موجودان في العينات الخام؛ نطاقاتهما عبر نوافذ Capture الكاملة: List Medium نحو `533–595 MiB` / `468–481 MiB`، Grid Medium `331–342 MiB` / `496–507 MiB`، Grid Maximized `337–353 MiB` / `504–520 MiB`. تتضمن هذه النطاقات وقت الالتقاط المحيط، وليست قياسًا معزولًا لحركة العجلة.
- محولات الفيديو المعروفة اثنان. Snapshot عدادات `GPU Engine` الخاص بـ PID 3828 أعاد صفرًا عند نقاط الالتقاط على عدة LUIDs؛ لا يحدد ذلك المحول الذي نفذ العرض. لم تُلتقط GPU Memory، ولم يُثبت GPU/Composition سببًا.
- `wpr.exe` و`tracerpt.exe` موجودان؛ لم يكن المستخدم Administrator، ورفض WPR البدء بالخطأ `0xc5585011`. لم أفتح مطالبة صلاحيات مرتفعة. PresentMon وxperf وWPA وdotnet-trace غير موجودة؛ لا يوجد ETL. لا توجد Presented Frames أو FPS نهائي. عدد callbacks المسجل في صفوف القياس `0`، والـ callbacks ليست Presented Frames.

## 5. تسجيلات الفيديو والملفات الخام

التسجيلات المحلية بصيغة H.264، 15fps، `1264×712`، ومدتها 180 ثانية لكل ملف:

- [`f201-interactive-scroll.mkv`](../perf-results/m25/live-tests/f201-interactive-scroll.mkv) — ملف شاشة لمرحلة Grid Medium Fast وما حولها.
- [`f201-grid-continuous.mkv`](../perf-results/m25/live-tests/f201-grid-continuous.mkv) — يغطي Grid Medium Continuous؛ انتهى قبل الالتقاط Maximized.
- [`f201-live-scroll.mkv`](../perf-results/m25/live-tests/f201-live-scroll.mkv) — تسجيل أسبق غير متزامن مع اختبارات Continuous الصالحة.
- [`preflight/recording-preflight.mkv`](../perf-results/m25/live-tests/preflight/recording-preflight.mkv) — اختبار توفر التسجيل فقط، 5 ثوانٍ.

الملفات الخام لكل تجربة تشمل `capture-manifest.json` و`capture-state.json` و`ui-thread-samples.csv`، إضافة إلى CSV القياس الميكانيكي حيث نُفذ. أمثلة:

- [`f201-songs-list-medium-continuous/`](../perf-results/m25/live-tests/f201-songs-list-medium-continuous/)
- [`f201-songs-grid-medium-fast/`](../perf-results/m25/live-tests/f201-songs-grid-medium-fast/)
- [`f201-songs-grid-medium-continuous/`](../perf-results/m25/live-tests/f201-songs-grid-medium-continuous/)
- [`f201-songs-grid-maximized-continuous/`](../perf-results/m25/live-tests/f201-songs-grid-maximized-continuous/)
- [`preflight/`](../perf-results/m25/live-tests/preflight/) و[`session-host/`](../perf-results/m25/live-tests/session-host/) — فحوص تشغيل/قياس، وليست تجارب تمرير صالحة.

## 6. الاستنتاجات

1. في زوج Grid Continuous الوحيد القابل للمقارنة، لم يرتفع UI CPU عند Maximized: `1.703s` مقابل `1.984s` في Medium، مع Busy `4.43%` مقابل `5.14%`. هذه عينة واحدة لكل حجم ولا تتضمن Small؛ لا تنفي ملاحظة المستخدم ولا تثبت سلاسة مرئية.
2. لا بيانات صالحة عن التدرج Small→Medium→Maximized أو العودة إلى Small.
3. لا يمكن تعميم نتيجة Grid على List أو Favorites أو باقي التبويبات، ولا مقارنة `fixture201` بـ`fixture5000`.
4. لا يوجد قياس موثوق لعدد الحاويات المحققة، ولا دليل يربط الحمل بنشاط خلفي أو بعدد الحاويات.
5. لا يوجد دليل Presented Frames أو GPU/Composition يسمح بإسناد سبب جذري. لا يُعلن سبب جذري من هذه الجولة.

**الخلاصة:** جُمعت عينة ميدانية حقيقية قابلة للمراجعة لـ`fixture201` على Medium وMaximized لقائمة الشبكة، وخمس نبضات سريعة على Medium، وعينة قائمة واحدة على Medium. بقية النطاق المطلوب غير منفذ؛ لا تُستخدم هذه الجولة وحدها كخط أساس نهائي أو كتبرير لتغيير إنتاجي.

## 7. استكمال الاختبار بعد طلب المتابعة — 17/18 نوفمبر

أُعيد تشغيل EXE نفسه على `fixture201` المعزول: SHA-256 بقي `F391E329A469D23DF59D939B5A0A5F6BDA2967E8F9AAC1A21EEEFB90543FF766`، PID `12372`، UI Thread `22380`. كانت المقاسات المقاسة في هذه المجموعة Medium (`1280×720` نافذة، `1264×712` Client). اختبارات القياس الميكانيكي استخدمت 25 حدث عجلة × 5 تكرارات بفاصل مضبوط 35ms، وبدأ/أوقف Capture مستقل لكل صفحة؛ قياسات الحدث مأخوذة من خرج الأداة المباشر لأن CSV الخام يزاح بسبب الفواصل غير المقتبسة الموثقة أعلاه.

| المكتبة | الصفحة/النمط | الحجم | صالح | متوسط wall لكل تكرار | متوسط UI CPU / Busy | متوسط Process CPU | Viewport / إثبات الصفحة |
|---|---|---|---:|---:|---:|---:|---|
| fixture201 | All Songs Grid، Fast | Medium | 5/5 | 1.316 s | 0.947 s / 71.94% | 1.575 s | `1200×497`, `SongsGridScroller` |
| fixture201 | Artists Grid، Fast | Medium | 5/5 | 1.228 s | 56 ms / 4.58% | 222 ms | `1200×497`, `ArtistsSearchScroll` |
| fixture201 | Favorites، Fast | Medium | 5/5 | 1.249 s | 706 ms / 56.54% | 1.100 s | `1200×485`, `FavoritesList` |
| fixture201 | Recent Play، Fast | Medium | 5/5 | 1.227 s | 662 ms / 53.99% | 922 ms | `1200×485`, `RecentPlayList` |
| fixture201 | Recent Add، Fast | Medium | 5/5 | 6.196 s | 3.622 s / 58.46% | 13.822 s | `1200×485`, `RecentAddList` |
| fixture201 | Most Played، Fast | Medium | 5/5 | 8.141 s | 4.178 s / 51.32% | 17.647 s | `1200×485`, `MostPlayedList` |
| fixture201 | Albums Grid، تمرير مباشر | Medium | — | — | Capture محيط: 2.733 s UI CPU، Busy 18.74% | 4.842 s | `1200×497`, الصفحة ظاهرة؛ تغيرت البطاقات من Amber Fields إلى Crimson Fields بعد التمرير |
| fixture201 | Folder Details، تمرير مباشر | Medium | — | — | Capture محيط: 5.587 s UI CPU، Busy 19.28% | 9.540 s | `1200×485`, `FolderDetailList`; تغير أول صف مرئي من Track 00001 إلى Track 00027 ثم عاد للأعلى |
| fixture201 | Genres Grid | Medium | 0/5 مستبعد | — | — | — | 10 أنواع، لا يوجد تمرير عمودي؛ فشل إثبات AutomationId/Name |
| fixture201 | Folders الرئيسية | Medium | 0/5 مستبعد | — | — | — | مجلد واحد فقط؛ لا يوجد ScrollPattern بهوية مثبتة، فلا تُستخدم العينة |

### دلالة إضافية وحدودها

- ملفات كل تجربة الإضافية محفوظة تحت [`continuation-f201/`](../perf-results/m25/live-tests/continuation-f201/). ملفات القياس: [Artists CSV](../perf-results/m25/live-tests/continuation-f201/artists-grid-medium/verified-20261117-235636-artists-grid-medium.csv)، [Favorites CSV](../perf-results/m25/live-tests/continuation-f201/favorites-medium/verified-20261118-000029-favorites-list-medium.csv)، [Recent Play CSV](../perf-results/m25/live-tests/continuation-f201/recent-play-medium/verified-20261118-000121-recent-list-medium.csv)، [Recent Add CSV](../perf-results/m25/live-tests/continuation-f201/recent-add-medium/verified-20261118-000550-recent_add-list-medium.csv)، [Most Played CSV](../perf-results/m25/live-tests/continuation-f201/most-played-medium/verified-20261118-001255-most_played-list-medium.csv). مجلدات Albums وFolder Details تحتوي Manifest وUI-thread samples للالتقاط المحيط؛ مجلدا Folders وGenres يحتويان سجلات المحاولات المستبعدة.
- الاختبارات الخمسة الصالحة لكل من Favorites وRecent Play تؤكد الآن أن بطاقات هذه القوائم لم تكن الجزء الوحيد المتبقي غير المختبر. وفي التمرير المباشر على Artists وAlbums وFolder Details تحركت قيمة/محتويات التمرير فعلًا عبر واجهة سطح المكتب.
- في Recent Add وMost Played استغرقت نبضة 25 عجلة نحو `6–9 s` بدل نحو `1.2 s` في أول التكرارات/القوائم الأخرى. UI CPU وProcess CPU ارتفعا أيضًا، لكن القياسات وحدها لا تحدد هل ذلك من رسم/تحميل المحتوى أو عمل آخر. لا يُستنتج سبب جذري.
- متوسط فاصل عينات الالتقاط، رغم ضبطه على 50ms، وصل إلى `488.6ms` في Recent Add و`761.4ms` في Most Played؛ لذا متوسطات Capture المحيطة لهاتين الصفحتين خشنة ولا تصلح لوصف نبضة فردية. قياسات `MeasureScrollVerified` لكل تكرار أوثق زمنيًا، مع بقاء عيب إزاحة الأعمدة في CSV الخام.
- ترتفع الذاكرة المقاسة عبر Capture خلال الجلسة الطويلة (متوسط Working Set نحو `588 MiB` عند Artists، وحوالي `636–696 MiB` في التقاطات لاحقة). اختلاف التوقيت وتراكم حالة الـwarmup/cache يمنعان تفسير ذلك كتسرب أو أثر حجم النافذة.
- الصفحة الرئيسية Home عند Medium عرضت `HomeScrollViewer` لكن شريطها الرأسي كان معطلًا؛ لا توجد قائمة Home قابلة للتمرير بهذا الحجم في بيانات `fixture201`. Recent Add وMost Played اختُبرا من بطاقاتهما في Home ثم تحقق من هوية القائمة بعد الانتقال.
- بعد قراءات الحالة الأخيرة ظهرت النافذة عند أصغر حجم مسموح `1116×650` (وكانت قبل ذلك `1280×650`). لم تُجمع عينة تمرير أو Capture عند الحجم الأدنى، ولا يوجد اختبار Return Small؛ لذلك لا تُعامل هذه الحالة كقياس Small.
- لم تُشغّل `fixture5000`، ولم تُستكمل أحجام Maximized للتبويبات الإضافية، أو Fullplayer/Lyrics/Visualizer. واجهت تفاعلات سطح المكتب مقاطعات متكررة لإدخال مستخدم، فتوقفت عن أي تغيير آخر للواجهة بدل تجاوز الحاجز. لا يغطي فيديو سابق هذه الإضافات، ولم يُنشأ فيديو جديد لهذه المجموعة.

### تحديث الاستنتاجات

1. لم يظهر ارتفاع UI CPU مع Maximized في مقارنة All Songs Grid Continuous السابقة (عينة واحدة لكل حجم)، والنتائج الجديدة تثبت أن الحمل يختلف بشدة حسب نوع الصفحة حتى عند Medium؛ خصوصًا التكرارات البطيئة في Recent Add وMost Played.
2. لا يوجد تدرج Small→Medium→Maximized أو اختبار عودة Small، لذلك لا يمكن تأكيد ملاحظة المستخدم حول أثر التكبير أو نفيها.
3. لم تعد فرضية أن المشكلة تخص Grid فقط صالحة: بعض القوائم مثل Favorites وRecent Play وMost Played وRecent Add أظهرت حملًا/تأخيرًا تحت قياس العجلة على Medium؛ لكن لا يمكن مقارنة أثر حجم النافذة دون جولات متطابقة إضافية.
4. لا نملك Presented Frames أو ربطًا موثوقًا لمحول GPU المستخدم أو GPU Memory أو ETL. لا دليل كافٍ على GPU/Composition، ولا قياس موثوق لعدد الحاويات المحققة.

**الحالة المحدّثة:** أصبح لدينا Baseline أوسع فعليًا لـ`fixture201` على Medium، ويشمل 5 تكرارات صالحة لكل من Artists Grid وFavorites وRecent Play وRecent Add وMost Played، وتمريرًا مباشرًا متحققًا في Albums Grid وFolder Details. لكنه لا يزال تقريرًا جزئيًا بالنسبة إلى المصفوفة الأصلية، وليس مقارنة كاملة لأثر Maximized. لم أعدّل ملفات الإنتاج أو أدوات القياس، ولم أنشئ commit. أُعيد بناء ZIP ليضم هذا التحديث وكل الملفات الخام المسجلة في `perf-results\m25\live-tests\`. المسار المطلق: `C:\Users\win\Desktop\Soulaiman\perf-results\m25\live-tests\M25-live-scroll-tests.zip`.
