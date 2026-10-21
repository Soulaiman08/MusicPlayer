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
