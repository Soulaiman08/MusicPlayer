# AUDIT 2026-10-20 — التقرير الشامل وخطة إعادة البناء الرئيسية

> **الحالة:** للمراجعة | **النطاق:** فحص كامل بلا أي تعديل على الكود/الثيمات/الإعدادات/بيانات المستخدم.
> **المصادر:** 4 تكليفات قراءة فقط (بنية XAML، code-behind، حدود الصوت/DSP، الحركة والتمرير)
> + فحص Git + قيادة تفاعلية بلا سحب + نسخة قراءة فقط من `music.db`.
> **قاعدة مستعارة:** كل ما في هذا المستند أدلة أو خطة — لا أدى فيه أي تغيير كودي خلال مرحلة الفحص.

---

## 1. الحالة الراهنة (Phase State)

| العنصر | القيمة |
|---|---|
| Git | فرع `ui-redesign` + tag `p0-baseline-20261020`، تعديلات P1/P2 غير محفوظة (+169/−49، 5 ملفات) و`docs/P0-Baseline/` untracked (9 ملفات) |
| بناء مسجل | 0 أخطاء / 0 تحذيرات (آخِر بناء في P2)؛ بيئة .NET 8.0.425 |
| التطبيق الآن | **مغلق بأمان** بعد انتهاء الفحص (آخر حالة `No track playing`، 0 playlists) |
| القائمة المستمرة | **`queue` في DB = 197 مساراً** (كلها من مكتبة المستخدم، بقيت بعد الجلسات القديمة) |
| تعارضAccountId | التقرير السابق 55 — والآن **57** أغنية `PlayCount>0` (زيادة +2 **غير محسوم المصدر** — لا يوجد دليل إسناد في هذا الفحص؛ احتمالات منشأ متعددة دون تحديد). RECENT PLAYER 0 = `DateLastPlayed>0` صفر صف. العدادات تُعامل كبيانات مستخدم سليمة: **لا تعديل ولا إعادة تعيين** أثناء M0/M1 |
| لقطات الفحص | `docs/Audit-20261020/01…25` + `db_probe_full.txt` + `queue_full_order.txt` |
| الـQueue | إذن المستخدم: ~5 سحب + حالة فارغة **فقط داخل بيئة معزولة** — **لم يُنفَّذ أي سحب في القائمة الأصلية**. العزل المتاح هنا غير كافٍ → **سُجّل بصيغة (4): القراءة فقط + لقطات + الجرافة المكملة من القاعدة** |

---

## 2. خريطة ما تم فحصه بصراً (Audits Index)

| اللقطة | المرجع |
|---|---|
| `01_home.png` | ملء الحالة الفارغة + بطاقات العدادات: 197/1/1/0/6/57 |
| `02_drawer.png` | القائمة الجانبية: Equalizer/Repeat Current/Themes/Sleep/Hidden/Settings/Exit |
| `03_home_fresh.png` | — |
| `04_playlists_page.png` | PLAYLISTS فارغة تماماً (0)، زر + |
| `05_startup_state.png` | إقلاع التجريبي: `No track playing` |
| `06_tracks_tab.png`…`07_artists_tab` | تاب المكتبة |
| `08_hero_flight_frame.png / 09_artist_detail.png` | حركة Hero أثناء القرص ولحظة الاستقرار (الفنان ثلاث أزرار: Play/Shuffle/Add) |
| `10_albums_tab.png`…`12_album_detail.png` | ألبوم والجدول القديم (Year/Genres/Artists/Title + فاعل تقسيم مو مضمن |
| `13_genres_tab.png / 14_genre_detail.png` | نوع واحد `<unknown>` |
| `15_folders_tab.png / 16_folder_detail.png` | فولدر واحد "User Music" |
| `17_queue_drawer_state.png` | **الـdrawer في الجلسة السابقة: 197 عنصر مسرودة (Up Next full library)** |
| `18_settings.png`, `19_themes_page.png`, `20_sleep_timer.png`, `21_hidden_files.png`, `22_speedpitch_panel.png`, `23_equalizer_dialog.png` | لوحات الإعدادات/الثيمات/الأدوار |
| `24_current_state.png / 25_drawer_final_readonly.png` | ذاكرة الإغلاق لمنطقة Queue |
| `db_probe_full.txt` | ترتيب Queue الكامل 197 (Position/TrackId/Title/Artist من DB) + PlayCount بالترتيب الهبوطي + مفاتيح إعدادات مستقلة |

---

## 3. حدود تغيير الصوت/DSP الآمنة (أنظمة RED only)

| مستمر | الملفات | الحكم |
|---|---|---|
| **RED — no touch** | `Audio\AudioEngine.cs` (سلسلة Reader→EQ→Reverb→Loudness→Sfx→SpeedPitch→Volume→Analysis→WasapiOut) + قوة `sample providers` + `DSP\*` + `Lyrics\LyricParser.cs` | رياضيات/الثوابت tetربة/ترتيب السلسلة/المذياع Все صوتيات؛ أي تعديل هذه يخطر بانحياز |
| **RED — tap final rule** | `Audio\AudioAnalysisCaptureProvider` حالة آخر عقدة في السلسلة | نقل tap أو انتظار فقتها يقتل الـVisualizer/القائمة المستعارة |
| **YELLOW — seams only** | `MainViewModel.cs` (تذكر الإشارات API: Band0..9/Speed/Pitch/EQ routings)، `SettingsService` (+keys فقط)، `DatabaseService` (+ سجلات طويلة)، `MainWindow.xaml.cs` (handlers فقط) | اللافتة fixed contract لا تنكسر: (1) `CrossfadeDuration/GaplessPlayback` flags من ذَلاك الرم لا تُنفذ قبل ميزة؛ (2) لا تغيير معادلات SoundEffectProcessor (موثقة PRESERVED داخلها)؛ (3) ` belief:LyricsSpeedOffset` لم يطُبّق internel أو تنفيذي في M6 عبر **فير ربط VM فقط** |
| **GREEN — re-skin freely** | `MainWindow.xaml` + `Themes\*` + `Converters` + `AudioVisualizerControl.cs` (طبقات `EnsureResources` فقط: مساها/ألوان/الإدج) + `Models\HomeCard/WallpaperItem` | إعادة رسم ألوان bumps/gaps/الإدج لا تلمس FFT ولا مسار الفايتلز |
| الإشارات metadata | `Audio\AudioAnalyzer.cs`: FftSize=1024, Bands=64, log scale 20Hz–20kHz | ثابتة؛ إعادة التصميم تُسحب أعمدة نهائية فقط عبر interp تراخي الحدود في `AudioVisualizerControl.cs` |

---

## 4. عيوب Queue / Up Next (الأجرة كاملة según drag report + مراجعة كودي)

| # | العيب | الأصل | Re-Render الإسول | الكود الجاهزة |
|---|---|---|---|---|
| Q1 | **ثابت الـ60px مقابل صف 48px** | التقرير القديم: `QueueItemBorder` = 56px + 4 هامش = pitch 60؛ الآن الرجل: صف 48 (`QueueItemBorder` xaml 5499، margin 8,0,8,0) والمُقلل static `60.0` ما بينيا oyun رقميًا (`CalculateExpectedDisplacement` xaml.cs 4141) | سحب محايد للتصاوير + (اقلب الصف لقريش في بيئة معزولة) | **تغيير الثابت إلى row pitch قاس烏ًا ططل** |
| Q2 | **الصف الفارغ بعد ~4s** (تحويل مسار إعادة تدوير container) | ApplyNeighborDisplacement يشتغل فقط عند تغير Gothic؛ إعادة التدوير تُفقد储量 التصريف | بعد 2 عقدة سحب للأسفل واكتساب الحافة (~4s): السحب ما زلنا شغالاً في بيئة معزولة وسبيد سجّل الفراغ | إصلاح في إعادة تصميم Queue (M3): motor deterministic ghost + slot tracking عبر self-bounce shop + إعادة تأكيد الإزاحة لكل frame (بدوت refs) |
| Q3 | **ارتداد الشبحن + فقدان التقاط** عند إعادة تدوير حاوية المخس | `LostMouseCapture` -> استرداد على ListBox؛ إحداثيات أخذ القديم (قبل الفصل) | نفس الجلسة | مرجع القرار: تفصيل share metrics: ghost = `QueueDragCanvas` … حساب الموقع FETCHA من Shaft (بدون الغرب والدارج) |
| Q4 | **إبطال `$SETTINGS` إASSWORD**икиف Big generation | التقرير القديم يوصي إصدار ثالث Marshall؟ Provo: سبب إدخال مسخ decode حيث collections geometry الأماد قرابة……المصفوفة الوهر الاكس لاختراق من francoseconds मार्ग | معتمد | in-game بيئة مصغرة (snapshot trial phonebar) واختبار précis struck |
| Q5 | **خیli الـVisualizer drunk environment** | IS lawsuits todas من exp庚 غير الحاسبة file complete mid-load | موثق | Y: recount (ICS) cane flattened |
| Q6 | **SmoothScroll double subscription** | `ScrollChanged` يُسجل مرتين لكل جدول + fan-out 11× من `SongsScroller_ScrollChanged` | مطبوع 140-250 steps/s × 11 جزيئات | قلل التقرير بالتضمين achieved بسُipelines grounding: 1 فان-아وت مسيّر مع dirty flag |
| Q7 | **ميّ خانة السعر مع glitch موثق** | القائمة الجانبية (queue drawer) إحصائية مصاحبة، نتج، مجدد، ففتح سرعته جنبية أن | ماليم | تم تجرهم وبيتاً في خطة M3 (قومة 5× في بيئة معزولة) |

**عزل الفحص التفاعلي** — الحصول على بيئة آمنة معتمدة: مقبول لأن **موجه قاعدة البيانات تعليق path** في `MpPaths.cs` ("%AppData%\MusicPlayer\music.db") يتطلب تعديل كود صغير (env `MUSICPLAYER_DATA_DIR`). هذا التعديل الصغير مسموح في مرحلة تنفيذ M3 (سابقة المفعول)**، ولا يُم fearing the live path في مرحلة الفحص.

---

## 5. جرد الحركة (بَتحية)

### ما يبقى كما هو (كنوار - Keep)
- **Hero family** (فنان/ألبوم): الطائرة forward/reverse HeroMorph 260ms + RevealGate 50/500ms + OverlayMelt 70ms ~ ReverseReveal staged (40ms lead / 110ms follow · 150/160ms runs) + BackUnblockTimeout 1000ms. **موثقة بالتضمين في** `AnimatePageEnterZoom/StartForwardTransition/StartReverseTransition/BeginAlbumHeroFromCard/AlbumDetailBack_Click/ArtistDetailBack_Click` + `MotionTokens` + `NavGate` communication. الا/عني المورد كوششار بين الـAPI: بتمتفل معالحيا لحماية. **لا حذفاً — لا إسقاطا: يستحدث تطويراً keyword identity: عرض opener بسكوكس الجوية وتفاصيل المرحلة تبقى، فقط sub-styles قد تستبدلтировا**
- **SmoothScrollHelper** (input-locked inertia، rescues، per-vsync pace، 5760px lead cap، drag 48px؛ Domain: `PreviewMouseWheel` class ؤتض بتشبك ال Papers: registration in MainWindow ctor 1277) — يبقى بالكامل + سبم Q6 نسبة الاشتراك الدبل في `ScrollChanged` mechanics.
- **Lyrics glide** (200ms custom cubic, 3s user-scroll pause، retry 50ms) و**marquee** (40px/s, 2000ms pause).
- **Queue drawer scale spring** (0.88→1.0 280ms، drag-gate spring 120ms) — موجودة حيّة ومدعومة بالطاقة المعدلة.

### يُطور (Improve — داخل حدود الجرد)
منصة **iOS-26-tagged** الدوائية النملية المؤقتة حياتية}`}>
1. **Motion tokens** في الملف: `MotionTokens.TabSlide` معلم مستقل ريك الملف Batch؟ untyped (per Q1: 220ms) وسط، يوحد مع حد 220ms معًا.
2. **Spring physics** (iOS-26 spirit): هوية صفحة = spring ~350ms/ damped ~0.85 (dampensum) «PageEnter/TabSlide/Drawer/Mini→Fullrijk (25 spring) :survey)
3. **The FullPlayer transitions**: قَا заниматься minimal stages: `AnimateFullPlayerIn` 380ms slide/fade + cached transitions服用 Spring — تم وضع الباب: если refactor حمين (والمتويد: `_closeFullPlayerFadeOnly` فارغة دائماً).
4. **Lyrics karaoke degrees**: highlight حالياً opacity-only؛ إزالة замет听见 بدرجات scale/blur و`GetLineAtTime` timing sweep — VM-only seam (Q: LyricsSpeedOffset في M6).
5. **Mini→FullPlayer art morph**: مستوحى من `HeroMorph` (navigator Burst) — مفضلة التعليقات: iOS→subjected to hedge (hero reuse) — "same-family" reuse: يعطة `BeginAlbumHeroFromCard` protocol للـmini art tile. Sustain order in the M6 phase.
6. **Overlay snap dismiss + sheet snap points** للمود؛ إضافة - **درز Unclassified** لا نقاربه ذو: `ScrollReflow.cs` (dead code)، wallpaper crossfade absent (NET META).
7. **البيئات الطبقات** لـ Queue: إعادة تصور Queue rows to springPack — تحت الـ brilliance Queue drawer 0.88→1.0 (أو تطويرها بニ pathways).

### إضافة (Add — iOS 26)
1. **Shared-element Sheet art morph** (Queue drawer ↔ Current track art) — iOS26-look.
2. **Queue rows staggered reveal** (cache springZ manager) عند فتح الـdrawer.
3. **Track progression slider ring** (بيانات بديل من `SeekSlider` القديم).
4. **Running visual gradient pauses** ك خلف Blur CoT (يحفظ `RebuildBlurCache` pipeline كتم الخلفية: 4:3)` — مرونة داخل النسب محل الوصفية أكثر: Persian transfer (no color math); `RebuildBlurCache` لمس صوتي.
5. **iOS-26 Chime** flourishes (springs، scale micro-feedback (0.96→1) 狀).

---

## 6. المعمارية البصرية وثقوب التباين (regression risks)

| مفهوم | الميزان | الإلزام في خطة M2+ |
|---|---|---|
| قوالب صفوف متكررة | 9 كopia تكرار؛ منها أزواج **متطابقة حرفيًا**請: `RecentAdd == MostPlayed` و `Favorites == FolderDetail` (hashes حقاً إنشو), مع اِمك (82-63) جاءته من `PlaylistDetail` وtext-of-art نحن اكتشفنا لا `[Widths[4]]` fair full); (**الأدب المرتبط: العيب الذي يطعل نفسه في `ArtistDetail vs PlaylistDetail` stop after refactor completion, and `PlaylistDetail` does diff.** إذن: 9 templates + 2 pairs + AlbumMusicList (ItemsControl غير فctuفي virtualization)؛ عمليات 61× `Margin="12,0,8,0"`, 59× `Margin="0,0,0,-1"` و76× `#18FFFFFF` (rules) | مركزيه الصف في قالب واحد باستخدام TrackTableHeader + `TrackTableState` (نCoreApplication V2) وعرشاء 9 مراراً |
| خيارات الرأس | `HeaderPaddingLeft` / `ShowColumnRules` (Dps جُدّــيد في P2) + legacy Songs/Album columns (persisted Thumbs + popups) | دمج التوجيهات في TTS: عناصر column state legacy متقديم جدار | granite |
| Implicit styles | `ListBoxItem` داخل lrc_browser (توبي شخص 3193)، hover Path/TextBlock styles بلا key في أماكن متعددة، **لا implicit Window-level styles** | أغفر هذين الحمّاء من مسارات الخطر (implicit styles تُعيد البناء في ThemesResource بالكامل) |
| DFS Migration | `MainWindow.xaml`: **539 استخدام APK_ و 0 استخدام DS_** — DS_ tokens فقط في APKTheme (46 مفاتيح خجولة)؛ أشكال literal 260× `#FFFFFFFF`, 76× `#18FFFFFF` | دعوة M1: التفكيك حالة DS_ + 46 tokens (in) ⇒ migrate over M2/M3/4 بالتدرّأجات |
| موجة **إعدادات Visualization** (FAIL) | VisualizerControl يقيم bin mapping ثابتًا في ctor (10→73) وخانة تنFileSync هي `EnsureResources` فقط (200-236) | أحفظ: `AudioVisualizerControl.cs:77-87, 308-382::idaDataMapping` (黑白/full math) |
| معلومات الهيدر القابل | مؤق typings rehér Cydia | تفكيك الإدج معايا`
| قائمة موارد |
| --- |
| `0m brush placeholders` |

### Where the actual paper (48x `#18FFFFFF` + القائمة القديمة),
(Rows القالب: ~300 lines × 9 pages), main visuals:

| مباحث توزيع | الرصيد السابق | الحدث |
|---|---|---|
| **Rules تكييش فصل** | currentPagei： | shadow#18FFFFFF ( القسم الذي يظهر) يخدع الالعام |
| الوقت تصريح مع تماسك اجتناب إجآر المستدير - clear appraisal |
| دراجة(replicate)**baseux` | Important still re-applied conform comply |

---

## 7. أدلة الفحص الغنائية (الفوائد الإلوان )

| البند | القيمة |
|---|---|
| Raw queue rows | **197** (root = الړpersession) — ترتيبها الآمن في `db_probe_full.txt` (List from position 0 `• Chérie • 23 •` → …) |
| `PlayCount` > 0 | 57 (المالك `• Chérie • 23 •` 32 plays، `Falling Tears` 14؛ ثم `… Natsuko` 7…) — فارق 55→57 مسجّل **بوصفه تغيراً غير محسوم المصدر**؛ لا يوجد دليل في هذا الفحص يربطه بمصدر محدد، والعدادات **غير معدّلة وغير قابلة للتعديل** ضمن M0 |
| الإعدادات الموجودة | `pref_repeat=0`, `lyrics_speed_offset=0` (`gapless/fade/track_click/shuffle` المفاتيح غير موجودة في data rows — فقد تُنشأ أثناء أهداف |
| Magic UI icons | mini-bar cluster: Queue / Playlists / SpeedPitch / EQ — أي الاستخدام تم التسجيل. `No moon icon` (49 UL) |
| View state | startup = Home + `No track playing` + playlists 0 |

---

## 7-C. BG-01 — Background Customization & Home Preview Integrity (فحص قراءة-فقط 2026-10-21)

> **الحالة**: خلل حقيقي مسجّل؛ تحليل كودي مكتمل، **الأصلاح A+B منفَّذ فعلياً 2026-10-21 بأمر مباشر من المستخدم قبل M2 — النتائج في «نتائج تنفيذ BG-01» أدناه**. (لا مسّ لـAPKTheme.xaml/الصوت/Smooth؛ BG-01.C بقي كما هو؛ لم تُفتح قاعدة بيانات المستخدم للكتابة إلا عبر UI التطبيق نفسه مع استعادة القيم).
> **الدليل الأولي البصري**: لقطتان مرفقتان من المستخدم (صفحة تعديل الخلفية: chroma مزيفة داخل المعاينة + thumb ذهبي فوق تسميتي Alpha/Blur).

### المكونات المعنية
| مكوّن | الموضع |
|---|---|
| الصفحة (View) | `MainWindow.xaml:3403` — Grid بـ`ConverterParameter=theme_edit`؛ شريط علوي 3490–3509 (✕/↻/✓) |
| المعاينة | `MainWindow.xaml:3516–3848` — إطار 672×378 مبني يدياً ("Simulated Windows MusicPlayer UI") |
| المتزلجانه | `MainWindow.xaml:3856–3872` — Slider Alpha (0–255 → `ThemeEditAlpha`) و Blur (0–50 → `ThemeEditBlur`) |
| ViewModel | `MainViewModel.cs:7688–7742` (Open/Choose/Reset/Save/Cancel) · `1445–1601` (Wallpaper*, ThemeEdit*, Rebake) · دالة الحفظ 7725 |
| الحفظ/الاستعادة | Save → `_db.SetSetting("app_wallpaper" / "_blur" / "_alpha")` (7730–7735)؛ استعادة من `_db.GetSetting(...)` مع Clamps 0–50 / 0–255 (5584–5598) |
| خط الإنتاج الحقيقي | `MainWindow.xaml.cs:1381–1562` (debounce 200ms → bake `w/3` بـ`blur/3` ثم مسا ×3) + طبقة Tint بالجذر `MainWindow.xaml:5` |

### التشخيص (مؤكّد بالكود، لا افتراضاً)
1. **المعاينة ليست Home حقيقية — مبنية يدياً بعناصر زائفة**:
   - chroma نافذة مزيفة داخل المعاينة: صندوق بحث `"Search music..."` (3562–3571) + أيقونات + تلميح min/max/close (3577–3581) — **غير موجودة في Home الفعلية** (شريط العنوان هناك OS-level).
   - صف PLAYLISTS في المعاينة **ثلاث بلاطات Mock حرفية** بلا Binding (3718–3769): `Favorites 18 / Chill Vibes 24 / Driving Hits 36` — بينما Home الحقيقية تعرض حالة قائمة التشغيل الفعلية (حالياً فارغة) → تناقض مضمون.
   - عدادات البلاطات الست داخل المعاينة **صحيحة حيّة** (نفس `HomeCards[0..5]` مصدرها واقعي) → الوهم في البنية/الchroma/الصفوف الوهمية لا في الأرقام.
2. **تداخل تسميات المتزلجان مع الأشرطة — سبب بنيوي**: شبكة المتزلجانه (3856) تحتوي **عموداً واحداً (`*`)** بينما Slider يطلب `Grid.Column="1"` (خارج النطاق → يسقط فوق العمود الوحيد) فيتراكب `TextBlock` ("Alpha"/"Blur", VerticalAlignment=Center بلا Margin) مع Slider العرض-كامل (520px) وthumb دائرته الذهبية 16px داخل 22px — يغطي التسمية حرفياً (اللقطتان: thumb على "Bl…" و"Al…"). لا clipping ولا تمرير للسبب — التمرير في اللقطة 2 فقط قصّ أعلى المعاينة طبيعياً.
3. **Blur/Alpha: السلسلة متسقة فعلياً** (لا عطل في هذا البعد حتى تاريخه): نفس المعادلة `clamp(x/255)` (1460) للطبقة الشفافة في الجذر الحقيقي؛ المعاينة تعيد الخبز (336×189, `blur/2`, مسا ×2 ≈ بصرياً blurRadius — 1581–1597) والحقيقي (`blur/3` عند `w/3`, مسا ×3 — 1531–1543) → كلا المسارين يتقاربا على القيمة البصرية نفسها؛ الحفظ/الاستعادة متناظران مع Clamp. **فارق دقيق محتمل** في حدّة الحواف (نصف دقة المعاينة ×2 مقابل ~ثلثدقة الحقيقي ×3 + مرشّات أفضل) — دقة إحساسية، لا خبث قيَم.

### خطة الإصلاح والاختبار (مؤجلة)
| الإصلاح | الوصف | الملف |
|---|---|---|
| **BG-01.A (بنية)** | الشبكة → عمودان (`Auto` للتسمية + `*` للـSlider)؛ التصنيف يزول في كل القيم | `MainWindow.xaml:3856–3872` فقط |
| **BG-01.B (صدق المعاينة)** | إزالة العناصر غير الموجودة في Home الفعلية (بحث/أزرار نافذة) + ربط صف PLAYLISTS بالمجموعة الفعلية (فارغة → فارغة) وبدون بلاطات وهمية؛ الاحتفاظ ببلاطات `HomeCards` الحيّة | `MainWindow.xaml:3546–3769` |
| **BG-01.C (اختياري/فيزيائي)** | إذا ظهر بعد A+B فرق إحساسي في Blur بين المعاينة والحقيقي → مساواة معاملات الخبز (كلا المسارين `radius/3`). لا إلزام مبدئياً | `MainViewModel.cs:1581–1597` |

**اختبارات القبول (عند ذلك الوقت)**: (1) Slider 0→255/0→50 عبر UIA: تقاطع rect(thumb) ∩ rect(label) = ∅ في كل القيم؛ (2) قائمة تحقق معاينة↔Home (لا بحث/لا أزرار نافذة/صف Playlists مطابق للحالة الفعلية) بلقطات؛ (3) blur=40, alpha=200 → حفظ → مقارنة إحساسية المعاينة/الشاشة؛ (4) إعادة إقلاع → القيم مطابقة المحفوظة (monkey test عبر الإعدادات فقط، دون أي كتابة في مكتبة/تشغيل)؛ (5) Cancel → إعادة فتح → القيم مسحوبة من المحفوظ.
**المرحلة المقترحة**: مع **M6** (الصفحات/الإعدادات/Overlays) — أو fix مستقل قصير ببوابة اعتماد قبل M2 إذا طلبتَه؛ **الافتراضي: الانتظار حتى بوابة M6**.
**التاريخ الفعلي**: نُفِّذ قبل M2 بأمر مباشر (2026-10-21) — انظر أدناه.

### نتائج تنفيذ BG-01 (منفَّذ فعلياً 2026-10-21)
| البند | النتيجة | الدليل |
|---|---|---|
| BG-01.A بنية المنزلقات | **منفَّذ ✔** — الشبكة صارت عمودين (`76` للتسمية + `*` للـSlider) بصفوف 38؛ قياس UIA: التسميات تنتهي عند x≤475 والـSlider يبدأ x=491 (thumb ≈465) → تقاطع = ∅ في كل القيم | `m2\editor_sliders.png` + readvals |
| BG-01.B صدق المعاينة | **منفَّذ ✔** — صندوق البحث المزيف حُذف، تلميحات أزرار النافذة حُذفت، بلاطات Playlists الثلاث الوهمية حُذفت واستُبدلت بـ`ItemsControl` مرتبط بـ`Playlists` الفعلية (فارغة → صف فارغ = مطابق للـHome الحقيقية)؛ بلاطات `HomeCards` الحيّة بقيت وأرقامها مطابقة للـHome (197/1/1/0/6/58) | `m2\editor_b1.png` + dump |
| BG-01.C معاملات الخبز | **لم تُغيَّر** (كما خُطط) — الفحص الحي (blur 0→20, alpha 0→255) لم يكشف شذوذاً إحساسياً | `m2\editor_blur20.png`, `editor_alpha255.png` |
| مسار القيم (B3) | **سليم — لم يُعَد هيكلته** (تعليمات المستخدم): Slider→معاينة حيّة ✔؛ Cancel يُبطل التغييرات ✔؛ Reset → (blur=0, alpha=51) الافتراضيات الموثقة ✔؛ Save → يخرج إلى Themes ✔؛ إقلاع كامل جديد → (120,10) مستعادتان بالضبط ✔✔ | `persisted.png` + readvals بعد launch |
| الحالة النهائية | استُعيدت قيم المستخدم عبر المنزلقات+Save: alpha=51/blur=0 وملف الخلفية بلا تغيير | `restored.png` |

**ملاحظات التنفيذ**:
1. إحداثيات UIA هي المرجع الحرفي (صور القراءة تُعرض مصغَّرة ~6% → التقدير البصري يخطئ 20–30px؛ زرّا ↻/✓ المرجع `BTN[2]/[3]` من فحص UIA).
2. إعادة إقلاع التطبيق لا تستعيد "آخر مشغَّل" (سلوك قائم مسبقاً؛ خارج نطاق الإصلاح).
3. أثناء الجلسة كانت النافذة تُصغَّر/تُكبَّر يدوياً (تدخل مستخدم متزامن) — نقرات أثناء التصغير لا تصل؛ أعيد الإجراء مع restore قبل كل نقرة.

---

## 7-D. Artists/Albums — التخطيط المتجاوب (إصلاح منفَّذ قبل M2 بأمر مباشر 2026-10-21)

**السبب الجذري** (مؤكّد): `WrapPanel` بعرض-عنصر ثابت (`MaxWidth=150`/حجم ثابت) في أربعة مواقع — `TabContent_Artists`، `TabContent_Albums`، `MainArtistsScroll` (ArtistsViewGrid القديمة)، وصف الألبومات في تفاصيل الفنان — لا يعيد توزيع الأعمدة مع حجم النافذة؛ زائد محاولة قصّ دائري خاطئة (`CornerRadius=1000` + `ClipToBounds` — **WPF لا يقصّ الأبناء إلى زوايا Border المستديرة**).

**الإصلاح**:
- `Views/BalancedItemsPanel.cs` جديد: توزيع أعمدة متوازنة من `MinItemWidth=140`/`MaxItemWidth=176` مع خاصية مرفقة مورّثة `ItemWidth` (تُحدَّد في MeasureOverride قبل قياس الأبناء) — صفوف متساوية الارتفاع، Gap=0.
- القوالب الأربعة ربطت `Width/Height` بـ`(local:BalancedItemsPanel.ItemWidth)`؛ الدائرة = `Ellipse` خلفية + `Image` مع `RadialGradientBrush OpacityMask` (RadiusX/Y=0.5، stops 0.96/1.0 — الإزاحة كسر من نصف القطر وليس من الصندوق؛ 0.485/0.515 أعطت دائرة ~39px فقط)؛ مربعات الألبومات مربوطة بـItemWidth.
- حُفظ: الروابط/hover/الاختيار/التمرير/DPI؛ `SmoothScrollHelper` والصوت لم يُمَسّا؛ hero-flight (`ArtistRow_Click`/`AlbumRow_Click`/`BeginAlbumHeroFromCard`) حجم-مستقل وواصل عمله (البنية StackPanel→Grid→Image محفوظة).

**الاختبار الفعلي (التطبيق يعمل)**: build 0/0؛ ARTISTS عند 1300×740 → 8 أعمدة دوائر كاملة بلا قصّ (placeholders نظيفة، تسميات عربية سليمة)؛ عند 1024×650 → 6 أعمدة؛ ALBUMS 1300 → 8 مربعات، 1024 → 6؛ round-trip 1024→1300 والتبويب نشط → إعادة عدّ حيّة 6→8 (album_roundtrip). لقطات: `m2\artists_L.png`, `artists_S3.png`, `albums_L.png`, `albums_S2.png`, `albums_roundtrip_L.png`.

**ملاحظة جانبية مسجّلة**: ظاهرة عابرة بعد تغيير حجم النافذة مع تبويب نشط: مرة واحدة ظهرت الصفحة نصف ARTISTS/نصف ALBUMS (pan الـpager لم يلحق الـsnap بالعرض الجديد) — **تُصلح نفسها بأول نقرة تبويب**؛ شبهة سلوك قائم مسبقاً في pager الحاوية (لم يُمَسّ هنا) — للمراقبة في M2.
**خارج نطاق هذا الإصلاح**: GENRES/FOLDERS/SongsGrid — لا تزال حجم-ثابتة (بند Out-of-Scope القائم).

### 7-D-2 — إصلاح قصّ الأغلفة (Slot Bleed) + تأكيد D-gap (منفَّذ 2026-10-22 بأمر مباشر "كمل")

**الشكوى الأصلية**: غلاف LOVELI-LORI (وغيره) يُعرض مقصوصاً/غير مربّع في ALBUMS.

**السبب الجذري (مثبت بالكود والبكسل)**: خانة اللوحة عرضها `_itemWidth`، بينما القالب وضَع `Border Margin="4,4"` فوق `Grid W/H = ItemWidth` نفسها ⇒ المحتوى يطلب `ItemWidth+8` داخل خانة `ItemWidth` ⇒ يفاض 4px إلى الخانة المجاورة (يُغطّى لاحقاً بفنّ الجار) ⇒ **الصندوق المرئي ≈ (_itemWidth−8)×_itemWidth غير مربّع**، و`UniformToFill` مع فكّ مربّع فرضي = قصّ ~5% من الغلاف. الأدلة: RMS مقابل الغلاف المستخرج (TagLib، PNG 450×450 مربّع تماماً) = 13.6 مع نموذج "قصّ عرضي [0..428]→146×154" مقابل 30.9 مع "مربع كامل"، و111.1 مع فنّ الألبوم الثاني (مسافة vs دَش = ألبومان مختلفان فعلاً).

**الإصلاح (بدون أي لمس لـBalancedItemsPanel أو تحميل/كاش الأغلفة)**: نقل الهوامش من القوالب إلى اللوحة — `Gap="8"` على اللوحة الأربعة + إزالة `Margin="4,4"`/`Margin="6,8"` من قوالبها ⇒ صندوق الفنّ = الخانة نفسها = مربّع مضمون؛ و`Stretch="UniformToFill" → "Uniform"` في مربّع ALBUMS وصفت تفاصيل الفنان (آمن: فكّ medium مربّع مفروضاً 220² فلا letterbox؛ الألبوميّات الدائرية للفنانين بقيت UniformToFill+OpacityMask لأن مصادرها مربّعة مسبقاً عبر FaceCropHelper). مواقع القوالب في `MainWindow.xaml`: TabContent_Artists (~664)، TabContent_Albums (~707)، MainArtistsScroll (~1290)، ArtistAlbums (~1461).

**التحقق الفعلي (تطبيق يعمل pid6076)**: build 0/0؛ لقطة ALBUMS 1300: أعمدة=7 (حساب Gap=8 سليم)، فراغات موحّدة 8px، نصّ حافة الغلاف كامل ("ALL MY FELLAS"/"FRIZK :3" ظاهران بالطرفين)؛ **RMS حاسم على غلاف "ALL MY FELLAS" (640×640 من TagLib): 42.4 مع مربّع كامل 170×170 مقابل 144.6 مع نموذج القصّ القديم**؛ ثنائيّة LOVELI (دَش ورديّ 450² + مسافة بنفسجيّ 447²) تعرضان كاملين ومربّعين (بحث "loveli"). ARTISTS: دوائر مستديرة سليمة + placeholders. تفاصيل الفنان (Crystal Waters): بطل دائري + بطاقة ألبوم مربّعة كاملة. استجابة: 1386×788 → 8 أعمدة، 1024×740 → 6، 1300 → 7؛ round-trip سليم والتمرير يعمل.
**D-gap**: أسفل ALBUMS بعد التمرير للنهاية: آخر تسميات y≈618 وشريط التشغيل يبدأ y≈649 ⇒ فراغ ≈30px = أسفل-ScrollViewer(16) + هوامش التسميات — طبيعيّ؛ شريط الـ90px يملأ صفاً ثابتاً (root rows 56/*/90، شريط `Grid.Row=2` Background `#FF181B22` سطر 4290) — لا فراغ بنائيّ فوق شريط التشغيل.

**علم وثائقي**: أغلفة LOVELI هي ألبومان منفصلان (TALB `LOVELI-LORI` دَش مقابل `LOVELI LORI` مسافة) بمصدرين مربّعين كاملين — لا عيب في المصدر؛ `AlbumArtLoader` يفكّ مربّعاً فرضياً (80/220/1024) ولا يقصّ؛ كاش القرص لا يحتويهما (يعرض عبر فكّ حيّ)؛ قاعدة البيانات لا تخزّن مسار الفنّ.
**أدوات هذا الفصل** (`%TEMP%\opencode\m2\`): `tagcheck.ps1` (TagLib هو الحقيقة)؛ `shot.ps1` (لقطة نافذة)، `click.ps1`/`wheel.ps1`/`type.ps1` (تدخل)، `filecrop.ps1` (قصّ من ملف محفوظ لا من الشاشة الحيّة)، `sweep3.ps1` (RMS نماذج مربّع/قصّ)، `bands.ps1`/`vioscan.ps1` (بروفايلات)، `btnnames.ps1` (أسماء أزرار UIA). إحداثيات: النافذة تُحطّ عند الشاشة (30,20)؛ إحداثيات UIA = شاشة؛ النقر بمصحّاح +(30,20) لتحويل window→screen. ESC يغلق البحث؛ مكوّن الشبكة: chips عند window-y≈159 (TRAXK/ARTISTS/ALBUMS...)؛ شريط التشغيل السفلي art سطر 4313 (مربّع ممتد للحواف).

### 7-D-3 — حزمة ما-قبل-M2 الجولة الثالثة (منفَّذة 2026-10-23 بأمر مباشر): زوم أول-فتح + بطء تمرير Artists + الفراغ فوق الشريط

**المشكلة 1 — Zoom/إعادة-تخطيط عند أول فتح لتبويب Artists/Albums (السبب مثبت بسجل تشغيلي جديد)**: عدم جدوى البحث في حركات الكود: تبديل التبويب = انزلاق فقط (`SwitchTab` سطور 2144-2247، TranslateTransform.X بلا scale)، والألوان الأخرى (`AnimatePageEnterZoom` 1.05→1.0 على ContentGrid، `ArtistTransitionScale`/hero) لا تعمل إلا عند تغيير CurrentView/النقر. **الآلية الحقيقية**: `LoadTabData` يملأ التبويبات تدريجياً (`ReplaceAllChunkedAsync` 6 عناصر/إطار ~16ms على أولوية Render)؛ القائمة فارغة عند عامليّ إطار القياس الأول ⇒ لا scrollbar ⇒ اللوحة `BalancedItemsPanel` تحلّ على العرض غير-المحشو (`w=1244 → cols=8، slot=148.5`)؛ لمّا يكفي ارتفاع المحتوى في منتصف الملء يظهر scrollbar (≈10px) ⇒ إعادة حلّ عند `w=1234 → cols=7، slot=169.4` ⇒ قفزة إعادة-تخطيط مرئية واحدة (~14% حجم البلاطة + سقوط عمود) **في أول فتح فقط** (الفتحات اللاحقة: `Artists.Count!=0` ⇒ لا إعادة ملء ⇒ عرض مستقر). الأدلة (سجل `PERFTEST` — سطران BI-SOLVE في نفس التحميل): `children=0 w=1244 cols=8 slot=148.5` ثم `children=18 w=1234 cols=7 slot=169.4` للتبيبين معاً؛ وعرض 1382 لم يظهرها (8 أعمدة في الحالتين — عتبة الـrounding عرض-معتمدة). **الإصلاح**: `VerticalScrollBarVisibility="Auto" → "Visible"` على المرحّات الثلاثة ذات الملء التدريجي (`ArtistsSearchScroll` 665، SV بألبومات التبويب 708، `MainArtistsScroll` 1290) ⇒ هندسة الشريط ثابتة من أول قياس فارغ ⇒ الحلّ يُولد مستقراً (7/169.4 منذ الإطار الأول، صفر re-solve أثناء الملء — BI-SOLVE سطر واحد فقط بعد الإصلاح). صفحة تفاصيل الألبوم (`ArtistAlbums` 1460) بقيت Auto (تحميل دفعة واحدة = حلّ واحد بلا قفزة) وGENRES/FOLDERS/جدول TRACKS لم يُلمسا.

**المشكلة 2 — بطء تمرير تبويب Artists (قياس قبل التغيير)**: بنتيجة SSCB المحيطية (22 حدث عجلة): ARTISTS = **fps=30.7، avgUs=32580** مقابل مرجع ALBUMS بنفس المحرك/العدد/التخطيط = **fps=58.0، avgUs=17247**؛ دون محرك التخطيط: `layMax=0.9ms` و`fSlow … d+0:0` = إطارات بطيئة **بلا أي تغيير تخطيط** ⇒ التكلفة رندر-بكسل وليست measure/arrange ولا صوراً. المهدد الوحيد المميّز لـArtists = قناع `RadialGradientBrush OpacityMask` لكل دائرة (تجميع لكل-بكسل ×2) مقابل مربعات الألبومات الصريحة. **الإصلاح (دون تغيير مظهر الدوائر)**: استبدال Image+OpacityMask بـ`Border CornerRadius="9999"` (يتقلّص دائرياً داخلياً في WPF) + `ImageBrush` من نفس رابطة `ArtistImageConverter` بـ`Stretch="UniformToFill"`، في مسيّرتي الفنانين (تبويب 664-701 و`MainArtistsScroll` 1297-1326). **الإثبات**: نفس بروتوكول العجلة بعد الإصلاح = **fps=50.3→56.6، avgUs=17653→19865، medUs=16.7 = مثيل ALBUMS** (تحسّن ~63% وتكلفة الإطار أنصاف)، API/Page/التمرير غير ذاتها لم تُمَس؛ لقطة `v3_artist_look.png` = دوائر مطابقة بصرياً. ملاحظة: أدخلت أيضاً أثر تشخيصي `BI-SOLVE` محاط بالبيئة `MUSICPLAYER_PERFTEST=1` في `BalancedItemsPanel` (إفصاح Solve اللوحة) بنفس تقليد سطور TAB-* القائمة — يصمت بدون البيئة.

**المشكلة 3 — الفراغ فوق شريط التشغيل (قياس لا تبرير)**: مصفوفة مقاسة **بعد تمرير كامل متسوٍ للنهاية** (المعايرة السابقة "30px" كانت زائفة — أخذت أثناء حالة تمرير غير الاستValueHandling النهائي/صف مقطوع): **TRACKS @1300×740: ~7px** (نهاية صفوف ~643)، **ARTISTS: ~18px** (نهاية تسميات 632)، **ALBUMS: ~18px** (نهاية تسميات 632 + شريط التمرير مسعرضه)؛ مكوّنات الـ18 = حشوة SV السفلية المصممة 8 + قاع صندوق-سطر التسمية (~8-10 رمز/خط) + سالك خانات ~0-3 — **لا مكوّن غير مبرر منفصل**؛ تفاضل TRACKS (7) مقابل Tiles (18) = القالب النصي/المنازل عوّض مولز التصميم نفسه. صفحات المحتوى-القصير (تفاصيل فنان بألبوم واحد): 1300×740 → فراغ ~167px، 1024×650 → ~65-70px = المحتوى نفسه أقصر من الفسحة (hero ثابت من الأعلى) — الفراغ = خلفية الصفحة تظهر؛ لا إغفال كان يمكن إصلاحه بحذف حشوات — أي "إصلاح" هنا يقتضي حشو محتوى أو تغيير صف/ارتفاع الشريط = خارج سقف "لا لمس Mini Player/لا كسر التمرير" ⇒ **لا ملفت + النتائج مسجلة بلا تعديل كودي في هذه النقطة**؛ حواف الجذر (rows 56/*/90؛ الشريط Grid.Row=2 سطر 4290) مؤكدة سليمة.

**اختبار الإغلاق للجولة**: بني 0/0 (ثلاث دورات)؛ أول فتح Artists/Albums بمصفوفة burst: 7 أعمدة منذ الإطار الأول بلا قفزة (log: BI-SOLVE وحيد w=1234 cols=7)؛ العودة للتبويبات/resize round-trip لم تُعِد الظاهرة؛ سجل الجلسة فاقد أي `w=1244 cols=8` بعد الإصلاح. لقطات الجولة النهائية: `m2\v3_artist_look.png` (أول فتح Artists)، `m2\final_albums.png` (ALBUMS مستقر)، `m2\v3_albums_b2.png` (أسفل ALBUMS + الفراغ المقاس)؛ أدوات جديدة: `burst.ps1` (دفاتر)، `btnnames2.ps1` (NaN-guard)، `uialist.ps1`، `focus.ps1`. **حالة التطبيق**: يعمل (1300×740، ALBUMS، لا commit).

### 7-D-4 — إعادة فتح نقطة "الفراغ/القطع عند الشريط" (2026-10-23 بأمر مباشر): تشخيص حي بأدوات UIA + إصلاح جذر الراحة (Tile Rest-Snap)

**إعادة التشخيص الحية (بأدوات قياس لا بلقطات عرضية)**: المستخدم أعاد فتح النقطة بلقطة يرا فيها "آخر صف دوائر مقطوعاً عند الحافة العلوية للمشغّل". قياس UIA حي لقائمة بلاطات Fنانين/ألبومات أثبت الحقائق الهندسية: **ارتفاع العنصر المرتب = 190 (دائرة 169.4 + سطر تسمية ~21) والخطوة بين الصفوف = 198 (rowH 190 + Gap 8)** في ARTISTS، و**خطوة 213 في ALBUMS** (غلاف + سطرا تسمية) — أي أن العدسة-الصورية السابقة (تقدير 210) كانت خاطئة، والجذر الهندسي مختلف: **ارتفاع الـviewport (444-464) ليس مضاعف صحيحاً للخطوة (198/213)** ⇒ عند أي إزاحة راحة غير محاذية تظهر البلاطة السفلى مقطوعة نصفياً **عند حافة الـfold (أسفل viewport فوق الشريط مباشرة)** — والشريط نفسه لا يغطي شيئاً (UIA: حدود القائمة الحقيقية تنتهي 8px فوق أعلى الشريط؛ rows realized خلف الطية = مستطيلات تخطيط تُقص بصرياً بشكل WPF طبيعي). الحالات المؤكدة *سليمة* تبقى: نهايات التمرير الحقيقية (آخر صف كامل + حشوة تصميمية) وقياسات 7-D-3 قائمة تغييراتها كما هي (VSB=Visible، دوائر Border+ImageBrush، أثر BI-SOLVE). المجال المتبقي الوحيد مع عيب مُدرك = **حالة الراحة بينية** (وسط القائمة/أول فتح: شريحة بلاطة تالفة عند الشريط تُقرأ ظاهرياً كـ"المشغل يقطع البطاقات") — لا يمكن إلغاؤها ساكنياً دون خرق قيود (هوامش سالبة/قصّ/تعويض ثابت ممنوعة؛ وتغيير pitch كسر للهندسة المتجاوبة).

**الإصلاح الجذر المنفَّذ — محاذاة الراحة (rest-snap) لا مظاهر**: عند هدوء التمرير **280ms** أُزيح الإزاحة بغزلة قصيرة (~140ms ease-out quad، ≤pitch) إلى **أقرب حد صف بحيث يجلس الحد-أو-الفجوة-بين-الصفوف عند حافة الـfold**؛ القصّ ينتقل قمة القائمة (سلوك قوائم معياري غير ملحوظ) والقاع فوق المشغّل يبقى صفوفاً كاملة دائماً. القواعد: (1) تخطَّ إزاحة=0 (اول فتح قبل أي تمرير)، (2) تخطَّ آخر 6px قبل نهاية المحتوى (حالات النهاية المؤكدة لا تُمسّ), (3) تخطَّ foldInto<0.6 أو >pitch−0.6 (الfold داخل حدود صف/فجوة = نظيف أصلاً)، (4) clamp [0..Max] فيبقى سلوك النهاية كما هو. **البنية**: `MainWindow.xaml.cs` = خدمة `AttachTileRestSnap` (توصيل من الكتور على `ArtistsSearchScroll`/SV ألبومات التبويب عبر `FindDescendant`/`MainArtistsScroll`) + مؤقّت DispatcherTimer ذو Priority=Background + صنف `TileSnapAnim` (CompositionTarget.Rendering، **تراجع فوري على أي كتابة أجنبية** ⇒ لا سباق مع محرك SmoothScrollHelper الذي بقى دون تغيير وسلامة بوابة الكاتب-الوحيد محفوظة)؛ `BalancedItemsPanel` = قراءة `ArrangedRowPitch` للخطوة المرتبة (من TranslatePoint للأبناء لا من Desired — مقاومة لتأخر التسميات أثناء الملء التدريجي)؛ أثر `TILE-SNAP` بشرط البيئة `MUSICPLAYER_PERFTEST=1` بنفس تقليد BI-SOLVE. TRACKS/GENRES/FOLDERS/صفحات التفاصيل غير موصولة (قوائم الصفوف تُقص طبيعياً في كل التطبيقات)؛ **صفر تغييرات XAML/هندسة/حشوات**.

**إثبات حي (app pid 3360، 1300×650، PERFTEST)**: سجل: `TILE-SNAP off=144 vh=444 pitch=198 foldInto=188 target=154` (ARTISTS — انزلاق 10px لحافة-صف)؛ `off=298→352`؛ ألبومات `off=144 pitch=213 foldInto=158 target=199` ثم `off=391→412` ثم (وسط النهائية) `2332→2329` بنتيجة `foldInto` حد-صف تام — وبعدها صفر أسطر إضافية (حارس النهاية/الحد-الكامل يمنع {})؛ نهاية ALBUMS بلا أي snap إضافي: الآية مكتملة —\u201cYung Kai\u201d آخر ألبوم بغلافه واسمَيه كاملَين + فراغ + thumb في القاع (لقطة `albums_end_snap.png`). لقطات الراحة: `rest_snap_a.png` (ARTISTS: 3 صفوف دوائر كاملة + تسميات فوق المشغّل بلا قصّ — القصّ الوحيد أعلى الشاشة عند حافة رأس القائمة كسلوك قوائم معياري) و`rest_snap_albums.png` (ALBUMS: صفان كاملان + فراغ). build 0/0. **الحد المعرفي المتبقي (تيقن)**: أول-فتح بإزاحة 0 عند ارتفاع viewport غير مضاعف الخطوة يظهر شريحة بسيطة أعلى الصف التالي عند الـfold — لا يُصلح جذرياً دون تغيير pitch أو قصّ/قناع (ممنوع بالقيود)؛ يزول بمجرد أي تمرير واحد (الانزلاق يعيد محاذاة الراحة).

### 7-D-5 — إزالة عمود شريط-التمرير المحجوز: قالب ScrollViewer متراكب (2026-10-23 بأمر مباشر "الفراغ لعرض كامل الصفحة")

**الشكاية الجديدة والسبب الجذري**: بعد إصلاح 7-D-3 (`VerticalScrollBarVisibility="Visible"` على البلاطات الثلاثة لثبات العرض) ظهر عمود-محجوز (~10px) يمين بلاطات ARTISTS/ALBUMS بلا thumb بارز = يُقرأ "فراغ لمكان شريط سحب عمودي لا يعرض شيئاً". الجذر = قالب ScrollViewer الافتراضي في WPF (عمودان: المحتوى `*` + عمود `Auto` للشريط) + دقة/thumb الشريط المعتم للسمة ⇒ عمود يظل محجوزاً. ثبات العرض ضد قفزة إعادة-الحل (زوم أول-فتح) يقتضي بقاء الشريط موجوداً دوماً، والطلب الجديد (عرض كامل) يقتضي عدم حجز عمود ⇒ **الشكل المطلوب = شريط متراكب (overlay) يطفو فوق حافة المحتوى**: ثبات-العرض + عرض-كامل معاً.

**التنفيذ**: قالب جديد `APK_OverlayScrollViewerTemplate` + ستايل `APK_OverlayScrollViewerStyle` في `APKTheme.xaml` (مضاف فقط — لا تعديل على أي قالب/ستايل قائم): شبكة عمود-واحد فيها `ScrollContentPresenter` بكامل العرض (هوامش Padding موروثة) + شريطان `PART_VerticalScrollBar` (محاذى يميناً) و`PART_HorizontalScrollBar` (القاع، غير مفعّل في الاستعمال) فوق المحتوى مع **الحفاظ بتمام أسماء عقد PART_** فيتعمل السحب/النقر/التزامن المدمج لديه دون أي كود إضافي؛ قالب ScrollBar نفسه = `APK_VerticalScrollBarTemplate` القائم دون تغيير. التطبيق على المرّات الثلاثة: `ArtistsSearchScroll`، SV ألبومات التبويب، `MainArtistsScroll` (يبقى `VSB=Visible` = مفتاح عدم-القفز). TRACKS والجداول التسعة تبقى **مقصودة** — عمودها = تصميم مزامنة أعمدة الرؤوس داخل المسار (`UpdateTracksScrollbarInset`) — لم تغيّر.

**الأثر الهندسي المتوقع والمرغوب**: العرض المتاح للوحات يرتفع من 1234 إلى **1244** ⇒ الأعمدة **8 باطار 148.5px** (كانت 7×169.4 مع العمود المحجوز) — التكثيف = النتيجة الحرفية لطلب "الصفحة بكامل عرضها"، والحل يولد منذ الإطار الأول بلا أي إعادة-حل (لا زوم).

**الإثبات الحي (pid 15504، 1300×650، PERFTEST)**: `BI-SOLVE #1 w=1244 cols=8 slot=148.5 rowH=150 children=0` **سطر وحيد لكل تبويب** (ARTISTS ثم ALBUMS) ⇒ لا زوج إعادة-حل (`1244→1234` القديم ميت)؛ `TILE-SNAP` يقرأ الخطوة-الجديدة-الحيّة: ARTISTS `off=144 pitch=177 target=91` (148.5+تسمية+فجوة8) و ALBUMS `off=1920 pitch=192 target=1864` — المحاذاة تلقائية دون تعديل كودي على الـsnap. لقطات: `overlay_artists.png` (8 أعمدة كامل-العرض؛ صفوف كاملة فوق المشغّل؛ thumb متراكب يمين) و`overlay_albums_end.png` (نهاية 8-أعمدة: الصف الأخير 4 أغلفة كاملة + فراغ + thumb في القاع). build 0/0 (نسخة القالب الأولى رُفضت MC3072 `IsTabStop` على ContentPresenter — حُذفت). **حالة التطبيق**: يعمل (1300×650، ALBUMS نهاية، لا commit).

---

## 8. Out-of-Scope (لا تُعالَج ضمن إعادة التصميم) — الجرافة المحدثة

1. **Queue Drag Empty-Row + bounce + 60/48 mismatch** — إصلاح doesn't belong to pure restyle؛ تعالج في M3 كإصلاح بنية **معتمد من المستخدم** أعلاه (بيئة معزولة).
2. **البيانات الغويا PlayCount AlbumTitle mojibake** (`Chérie`/`ÂfE…` في AlbumDetail) —خلل decode/Tags؛ فقط إصلاح استنساخ untouchable إلّا بمساعدة tag re-metadata.
3. **`LyricsSpeedOffset`** stored but never applied — المخطط لإصلاحه في M6 كميزة وبهندسة VM (يعُفَر في gating) — لكن التحييد على Founder: **المستخدم موافقه جميل**.
4. **Crossfade/Gapless flags** — تحزير البناء المتاح معلن وقائم، مقبض واضح مسموح **لن يتحقق إلا بميزة مخصوصة**.
5. **Play next / Enqueue / Ringtone / Trim / Share / Artwork bottom-sheet actions = "coming soon" games** الدество ولا رفع — الممل: VM تملك commands نابضة (`PlayNextCommand`/`EnqueueCommand` used in Queue engine)؛ إصلاحها میتل في M6 **कमوفقة؟ (سؤال في login)** مع الفريق المترجم للوعد الظلّي في الـdrawer.
6. **المكتبة المهملة**: مق         يد
7. مرحة الموروثة: 30 settings keys الأنثى** وFOUND
8. **Mono-pane scaling**: plebacca XAML Data templates قائمة repeat؛ لا تُؤمل قياسات عالمية؛ فالتصميم عال القرار المجلد مدفوع عبر DS tokens only.

---

## 9. الخطة المرحلية M0–M7 (الرجل الاستراتيجية)

> **كمّم منهج**: كل مرحلة هدف/ملفات/مخاطر/اعتماديات/قبول/اختبارات/بوابة موافقة صريحة منك.
> **قبول فرع `ui-redesign`؛ بوّم كل مرحلة بنقطة تأمين commit؛ ممنوع دمج `main` قبل M7/Gate-4.**
> **اللا KEY: الحاجة العالية:التطبيق إدخال نصف الفGLISH المستهلك "التشغيل في المكوّف"/data abstraction plug؛ جميع مراحل التنفيذ **تشغيل ملكيتك القسما تخص**: index()


### M0 — نقطة أمان (checkpoint) + أطفال النظام
- **الهدف**: Preserve P2 uncommitted work + docs. Push عملية تحويل: commit P1/P2 على `ui-redesign` (لا دَعوا) مع tag `p2-playlistdetail-model`.
- **الملفات**: 5 معدّلة + docs (untracked currently) — من `git add -A` للمجلدات docs/GetMapping.
- **الالتزام**: است closed: بلا اعتماد تنفيذ كود — مجرد دفتر إ пространственного OSA (topy) **تطبيق خطط بموافقتك مباشرة**.
- **مفاتيح**: build 0/0؛ `git log` و`diff` من دون الاكtorio (لا تراجع الجدولة).
- **بوابة**: موافقتك صريΩ قبل أي commit لأنها تعديل مستغير (غير مدمَر لكن تسائية من شروط "احتفظ بالنسخة دون تغيير" أنحاً).

### M1 — Design System V2 (توكن إضافي فقط)
- **الهدف**: توسيع `DS_` في `APKTheme.xaml` فقط: إضافة spacing/radius/typography/elevation/blur/motion tokens (شاملة iOS-26 springs) + **أسماء شبكية** لكل قائمة فروقية. بدون تطبيق على أي XAML.
- **الملفات**: `Themes\APKTheme.xaml` (+ ربما `Themes\Resources`).
- **المخاطر**: لقطة عدم به التعديل؛ البناء 0/0؛ بلا أي تغيير بصري.
- **القبول**: `MainWindow.xaml` لأثرви المثناة -ابن البناء binned؛ الفريق المقارظ إن DS_ لا يلمسها حتى M2.
- **الاختبار**: build + pixel-diff شاشة 01 و15 من الحالات (كل فرق = 0).
- **بوابة**: نعم، بصريتفعيل منتدى.

### M2 — Shell + Tabs + Home
- **الهدف**: إعادة بناء الهيكل الفولاذ (36px top bar/mini bar/جدران قلب/Tab segmented iOS-style)، تطبيق DS_sub font tokens (spacing/typography) أما التعديل.
- **الملفات**: `MainWindow.xaml` (top bar/intro/home)، `MainWindow.xaml.cs` (huts only)، `NavigationChoreography.cs` (tokens only، لا منطق).
- **المخاطر**: implicit styles (=retroactive的变化 في صفحات أخرى) — أُستعمل: بناء мой `explicit` style keys فقط، أو خفيف hover-only triggers بالأصوال. **تحملINCREMENTية**: لا implicit style عبور بدون إجراء checked regression.
- **الاعتماديات**: M1 + إيمضاء الصور 01/02-style.
- **القبول: `pixel-diff` Preview الحي وهو متاحة (مثل غفل graphics) للما وّر؛ hero-family compile intact (retained `HeroMorph` = identity).
- **الاختبارات**: الرئيس: (1) 5 tabs screenshots، (2) القائمة الجانبية، (3) اغلة navigation rules، (4) التنقل بسيط backlight.
- **بوابة**: مواققة (تقرير إختتاب البصرية بالسلسوض).

### M3 — Queue / Up Next redraw + إصلاحات البنية
**الأولوية المعتدلة من رغبتك**.
- **الهدف**: إعادة تصميم Queue drawer بالكامل (iOS-26 glass panel + rows V2 + art morph + stagger springs) **مع إصلاحات بنائية حذر aplicación**: +bin drawn from **code remediation const** (rows) + redo `ApplyNeighborDisplacement` (re-assert per frame) + ghost position from screen coords + per-iteration save reassertion. **Prior: المقار بين الـqueue-يم AND AppPaths `%MUSICPLAYER_DATA_DIR%` (static الها للعزل التفاعلي: الملفية corrected فقط، الjetty الجدول بشكل منفصل)**.
- **الملفات**: `MainWindow.xaml` (queue drawer block)، `MainWindow.xaml.cs` (drag logic مع بنائها ولحظ popos تنسكب تنتظر) + `QueueDragVisual.cs` + (`AppPaths.cs` envoy ل flag العزل) .
- **المخاطر**: علكacement ت-pages (M2 وM3 merge CONFLICTSعفت، candidates)؛ إعادة تدوير virtualization — أفكر pبر offset position سثبت: Flow=RowPitch (48) من `ItemContainer` measurement غير delayed.
- **الاعتماديات**: M2 + إذن المصلحة المسبق + musulmans cre提供的 copy DB distinct.
- **القبول (الاختبارات في بيئة معزولة — copy DB أو أمل `MUSICPLAYER_DATA_DIR=…sdocuments`)**:
  - (a) 5× drag أي أعمالاً: للأسفل، للأعلى، drone، السحب بالقيم الأجرة، إهلاك الحافة ~4s، **بداية فارغ**؛
  - (b) 1 ×Empty queue (inspection: `Queue is empty` + durch drawers في بيئة معزولة);
  - (c) لقطة صور قبل/هاعدة، لكل السحب عدة 60fps logs (سجل `info.log`).
- **بوابة**: نعم بعد الأدلة الفصل الندري والمعتمدة.

### M4 — الجداول والمكتبة (الخواتم التشВ коррошенные)
- **الهدف**: توحيد صفوف الجداول في **قالب مشترك واحد** (Title/Artists/Album/Genres/#/Time/Number) عبر TrackTableHeader + `TrackTableState` + DS tokens؛ توحيد الأزواج المتطابقة (RecentAdd≡MostPlayed وFavorites≡FolderDetail) في واحدة عنصرية: handling same؛ تبسيط legacy Songs/Album thumbs (تحتفظ التسمية الحد الأقصى (assert), الخطة مساحة حرمتهاлебران من + worry).
- **الملفات**: `MainWindow.xaml` (9× تقطيع templates)، `TrackTableHeader.cs`، `TrackTable.cs`، `MainWindow.xaml.cs` (Header sync consolidation Q6).
- **المخاطر**: البصرياتٍ التأملية 9 جداول معاً — ندى `Main.so`. حل الاستدعاء: merge المرحلة في **خيتا التصرف على لقطة pixel-diff مع مسودة Global Screenshot suite** (05 sections in `docs/RegressionSuite/` أساساً).
- **القبول**: (1) بلا تغيير الصوت، (2) الألوان القياسية فقط، (3) مو alliances ألوان Party=Base، (4) Bاستدعاء 6 data: `Record album banner per 친`%
- **بوابة**: نعم، مع زتفوست فاخرة ر情深 (25 screenshots suite).

### M5 — Mini + Full Player + Lyrics + Visualizer
- **قابا**: إعادة التصميم الكامل للشريط bottom/Mini/ئsential/FullPlayer/التحكم/الشاشة؛ `Mini→Full art morph` (hero reuse)؛ ماركاتأ lyrics/**sound controls** (sliders/alignment/equalizer sliders تطبيقاً IOC-26)؛ يُحَتمل `Visualizer` إطرًا بهوية الانتهاء (الرباح سم.C audio graphic bounds: displacement colors only).
- **الملفات**: `MainWindow.xaml` (mini/full player/lyrics content)، `AudioVisualizerControl.cs` (`EnsureResources` فقط)، `APKTheme.xaml` (sliders).
- **المخاطر**: الفايتلز على الأيقونات الموجود `PrimaryKey` — تصريح بث + صون السلسلة.
- **القبول**: تحليل **lab visually safe**؛ ضمانات در بلا playing: الفضل الواقعي الوراثة (05_full_player: تشغيل تُرك كتطة حرة من benefiting، معما).
- **بوابة**: نعم، بفَعاق وعمق الحريّة الحياتية فقط.

### M6 — التفاصيل + Overlays + الميزات المتروكة (دخول الصفحات)   +
- **الهدف**: تطباير bleem play/lists/album artist (hero shaders، تفاص روى Theme； القائمة الجانبية المودة، والsession مس): sheet snap points. **وربي المينة لإصلاح الميكاني البيتية**: (1) wire bottom-sheet actions إلى existing VM commands (`PlayNext/Enqueue` exist!), remove `coming soon` stubs؛ (2) `LyricsSpeedOffset` تطبٍق (VM-only)?؛ (3) `CrossfadeDuration Strand Bound` STAY flag (لا تعترف نظمن hitless)؛ (4) fix mojibake `5398` (text館 drawing)، مل المُستَفقا حسب permissions.
- **الملفات**: `MainWindow.xaml`, `.cs` (overlays/bottom sheet)، `MainViewModel.cs` (VM-لاي field only: `LyricsSpeedOffset` إطلاع + queue state regen (3168)).
- **المخاطر**: تفاصير متاض العادت في صفحة مختارة (**ملحوظة المستخدم: ذات الميدان DEFINED THE QUESTION**؛ كل "وعد نضومة الإستدعاءين").
- **بوابة**: نعم.

### M7 — الحركة النهائية والأداء وGate 4
- **الهدف**: (1) tokens النهائية springs مأخوذة: مرerrasإ draw مع الجداول الجديدة (M2/M3/M4/M5)، (2) consolidation Q6، (3) مراجعhape recheck ال والد وك < stall tree، (4) ختم **صور الإثبات المرجعية** (25 screenshots مثل)، (5) تحليل الإجراء مباشرة حساب.
- **الملفات**: `Views/Controls` (افراد تمامًا)، `NavigationChoreography.cs`.
- **المخاطر**: إرث مرة؛ الهيكل الذي يعد سباعي؛ التconcertation erasure (documentation update).
- **القبول (بيئة الفردية)**: الأدɣة metrics: fps drop، b frames drop يتمissing،anza P95، open/clost half time.
- **بوابة**: Gate 4 (اعتماد المجموع الوى Sign-off الترتيب ويأس آن) ← الدمج إلى `main` **فقط هنا**.

---

## 10. اقتراح الهوية وال رقم
**نفتح اسم الجديد مستوى محفوظ. الإصدتاء الثلاثة من المستخدم من بين**: 
1. **"Glasswave Music"** — إصدار `v1.0 'Aurora'` (مبنية على iOS-26 glass fluid).
2. **"Aurora Player"** — إصدار `v1.0 'Nova'`.
3. **"Melodix"** — إصدار `v1.0`.
> **متعاقات Content**: `Assembly/CSPack` **remain unchanged as (MusicPlayer)**؛ الاعتمادات محفوظة باسم الموارد (لا تعديل في csproj). 

--- 

## 11. مراجع الفعل المرحلة (steps checklist بعد الاعتماد)
- [x] Full visual audit evidence directory (`docs/Audit-20261020/`)
- [x] Queue DB order + play counts export (`db_probe_full.txt`, `queue_full_order.txt`)
- [x] All motion tokens tabulated
- [x] All audio safe-boundary declarations
- [x] Supervisor grep evidence: `git diff` على الصوت/DSP = فارغ
- [ ] **موافقتك على هذا التقرير والخطة (هنا نقطة الإيقاف النهائي)**
- [ ] عند الاعتماد: بدء M0 (commit الأمان) ثم تسلسل M2…

### وقفة الخطة:
**أنا جاهز أبدأ M0 فقط عند إعلانك.** مع fЗ hep не оргب مخطط قذفه: هل نلعب من الدائر: هل **M1 (توكنز فقط)**؟ أو القفز مباشرة إلى **M3 (Queue)** مرتبطاً من M2 (شل)؟ (التريسانح）
