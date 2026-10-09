# مصفوفة اختبار التفاعل والوظائف (Interaction Test Matrix)

توثق هذه المصفوفة نتائج الاختبارات الوظيفية لكل شاشة وزر وقائمة وعنصر تحكم في بيئة Windows.

---

## مصفوفة الاختبارات الوظيفية الشاملة

| Screen | Control / Action | Expected Android Behavior | Windows Implementation | Tested | Result | Bug Found & Fixed |
|---|---|---|---|---|---|---|
| **Main / TopBar** | Menu Button (Hamburger) | فتح القائمة الجانبية للتنقل | `ToggleDrawerCommand` | ✅ Yes | Passed | None |
| **Main / TopBar** | Search Input | فلترة الأغاني فوراً مع Debounce | `OnSearchQueryChanged` + SQLite `SearchTracks` | ✅ Yes | Passed | Unicode & Arabic supported |
| **Main / TopBar** | Clear Search Button | مسح النص وإعادة إظهار كل الأغاني | `ClearSearch_Click` | ✅ Yes | Passed | None |
| **Main / TopBar** | Scan Music Button | فحص المجلدات واستخراج البيانات | `ScanMusicCommand` (Async) | ✅ Yes | Passed | Real TagLib# extraction |
| **Main / TopBar** | Shuffle All Button | خلط جميع الأغاني وتشغيل أول مقطع | `ShuffleAllCommand` + `AudioEngine.ShuffleEnabled` | ✅ Yes | Passed | Seamless playback start |
| **Main / TopBar** | Window Maximize | تكبير النافذة مع احترام Taskbar | `WM_GETMINMAXINFO` Hook in Win32 | ✅ Yes | Passed | Respects WorkArea perfectly |
| **Main / Drawer** | All Songs Click | فتح قائمة كل الأغاني | `NavigateCommand("songs")` | ✅ Yes | Passed | Virtualized list |
| **Main / Drawer** | Albums Click | فتح قائمة الألبومات | `NavigateCommand("albums")` | ✅ Yes | Passed | Grouped by Album |
| **Main / Drawer** | Artists Click | فتح قائمة الفنانين | `NavigateCommand("artists")` | ✅ Yes | Passed | Grouped by Artist |
| **Main / Drawer** | Genres Click | فتح قائمة الأنواع | `NavigateCommand("genres")` | ✅ Yes | Passed | Grouped by Genre |
| **Main / Drawer** | Folders Click | فتح قائمة المجلدات | `NavigateCommand("folders")` | ✅ Yes | Passed | Distinct directory paths |
| **Main / Drawer** | Favorites Click | فتح المفضلة | `NavigateCommand("favorites")` | ✅ Yes | Passed | Filtered from SQLite |
| **Main / Drawer** | Equalizer Shortcut | فتح معادل الصوت | `ShowEqualizerPanelCommand` | ✅ Yes | Passed | Overlay opens smoothly |
| **Main / Drawer** | Sound Effects Shortcut | فتح المؤثرات الصوتية | `ShowSoundEffectsPanelCommand` | ✅ Yes | Passed | Overlay opens smoothly |
| **Main / Drawer** | Sleep Timer Shortcut | ضبط مؤقت الإيقاف التلقائي | `ShowSleepTimerCommand` | ✅ Yes | Passed | Real 30-min timer |
| **Main / Drawer** | Settings Shortcut | فتح شاشة الإعدادات التفضيلية | `NavigateCommand("settings")` | ✅ Yes | Passed | Full 9 categories view |
| **Home** | Home Cards (6 Cards) | فتح الأقسام مع أعداد حقيقية | `AllSongsCard_Click`, `FoldersCard_Click`, etc. | ✅ Yes | Passed | Real counts from DB |
| **Home** | New Playlist Button | فتح نافذة إدخال اسم القائمة | `NewPlaylistDialog.ShowDialog()` | ✅ Yes | Passed | Persisted in SQLite |
| **Home** | Playlist Tile Click | تشغيل أول أغنية في القائمة | `PlaylistItem_Click` | ✅ Yes | Passed | Playback begins immediately |
| **Mini Player** | Click on Mini Player Card | فتح شاشة الـ Full Player | `MiniPlayer_Click` -> `OpenFullPlayerCommand` | ✅ Yes | Passed | Fixed: previously opened Lyrics only |
| **Mini Player** | Play / Pause Toggle | تبديل حالة تشغيل الصوت | `PlayPauseCommand` | ✅ Yes | Passed | Instant WaveOut response |
| **Mini Player** | Next / Prev | الانتقال للمقطع التالي/السابق | `NextCommand` / `PreviousCommand` | ✅ Yes | Passed | Index & track changes instantly |
| **Mini Player** | Shuffle Toggle | تفعيل/تعطيل الخلط | `ToggleShuffleCommand` | ✅ Yes | Passed | Accent color highlight changes |
| **Mini Player** | Repeat Toggle | تكرار الكل / واحد / إيقاف | `ToggleRepeatCommand` | ✅ Yes | Passed | Cycles Order, LoopAll, Single |
| **Mini Player** | Speed / Pitch Chip | فتح حوار التحكم بالسرعة | `ShowSpeedPitchPanelCommand` | ✅ Yes | Passed | Displays live multiplier |
| **Full Player** | Back Button | إغلاق Full Player والعودة للمكتبة | `CloseFullPlayerCommand` | ✅ Yes | Passed | Smooth transition |
| **Full Player** | Large Cover + Blurred BG | عرض غلاف الألبوم بدقة عالية | `AlbumArtConverter` (Async loader) | ✅ Yes | Passed | No missing placeholders |
| **Full Player** | Synced Lyrics Scroll | تظليل ومزامنة الكلمات | `UpdateLyricProgress` via Position timer | ✅ Yes | Passed | Synced line by line |
| **Full Player** | Click to Seek on Lyric | القفز الفوري لزمن السطر المضغوط | `LyricLine_Click` -> `AudioEngine.Seek` | ✅ Yes | Passed | Precise timestamp seek |
| **Full Player** | Seek Slider Drag | تقديم/تأخير زمني فوري | `PlaybackProgress` (TwoWay) | ✅ Yes | Passed | Real-time position change |
| **Full Player** | Rewind / Forward 10s | قفز زمني نسبي 10 ثوان | `SeekRelativeCommand(-10)` / `(10)` | ✅ Yes | Passed | Accurate offset seek |
| **Speed / Pitch** | Speed Slider (0.25x - 2x) | تسريع/إبطاء الصوت في الوقت الحقيقي | `SpeedPitchSampleProvider.Speed` | ✅ Yes | Passed | Real-time WSOLA time-stretch |
| **Speed / Pitch** | Pitch Slider (-12 to +12) | تغيير نبرة الصوت في الوقت الحقيقي | `SpeedPitchSampleProvider.Pitch` | ✅ Yes | Passed | Real-time resampled pitch shift |
| **Speed / Pitch** | Speed Quick Buttons | ضبط فوري للقيم 0.5x, 1x, 1.5x, 2x | `SetSpeedPresetCommand` | ✅ Yes | Passed | Instant response |
| **Speed / Pitch** | Reset Button | استعادة 1.0x / 0 semitones | `ResetSpeedPitchCommand` | ✅ Yes | Passed | Audio returns to normal |
| **Equalizer** | 10 Band Sliders (-15 to +15dB)| تعديل الترددات بالوقت الفعلي | 10 Biquad Peaking Filters in `EqualizerSampleProvider` | ✅ Yes | Passed | Audible real-time EQ changes |
| **Equalizer** | 23 Presets Combo | تطبيق النمط الموسيقي المختار | `EqPreset.Presets` -> `SetAllBands` | ✅ Yes | Passed | Presets apply to all 10 bands |
| **Equalizer** | Bass Boost Toggle & Slider | تعزيز الترددات الخفيضة 80Hz | Biquad Low-Shelf Filter | ✅ Yes | Passed | Audible deep bass boost |
| **Equalizer** | 3D Virtualizer Toggle & Slider| توسيع المسرح الصوتي ستيريو | Mid/Side stereo processor | ✅ Yes | Passed | Audible spatial widening |
| **Equalizer** | L/R Balance Slider | موازنة السماعة اليسرى واليمنى | Stereo multiplier | ✅ Yes | Passed | Smooth panning balance |
| **Sound Effects** | Electronic Tube Card | تشبع الصمام المفرغ الدافئ | `SoundEffectProcessor.ElectronicTube` | ✅ Yes | Passed | Audible warm triode distortion |
| **Sound Effects** | 3D Rotate Card | دوران ستيريو بين السماعتين | `SoundEffectProcessor.3DRotate` (LFO) | ✅ Yes | Passed | Audible dynamic panning |
| **Sound Effects** | Tone Low Card | تشكيل الترددات الخفيضة | `SoundEffectProcessor.ToneLow` | ✅ Yes | Passed | Deep warm low character |
| **Sound Effects** | Surround Sound Card | إحاطة صوتية موسعة | `SoundEffectProcessor.SurroundSound` | ✅ Yes | Passed | Haas spatial reflection |
| **Sound Effects** | Magic Sound Card | بريق الترددات العالية الناصعة | `SoundEffectProcessor.MagicSound` | ✅ Yes | Passed | Sparkle exciter effect |
| **Sound Effects** | Live Treble Card | تعزيز حدة ونقاء التريبل | `SoundEffectProcessor.LiveTreble` | ✅ Yes | Passed | High-shelf dynamic clarity |
| **Play Queue** | Track Item Click | تشغيل المقطع من قائمة الانتظار | `TrackRow_Click` | ✅ Yes | Passed | Changes track instantly |
| **Play Queue** | Remove from Queue Button | حذف المقطع المحدد | `RemoveFromQueueCommand` | ✅ Yes | Passed | Removed from AudioEngine Queue |
| **Play Queue** | Clear Queue Button | تفريغ قائمة الانتظار بالكامل | `ClearQueueCommand` | ✅ Yes | Passed | Queue emptied |
| **Settings** | All 10+ Checkboxes | تغيير الإعدادات وحفظها فوراً | TwoWay Binding to `SettingsService` & SQLite | ✅ Yes | Passed | Reloads correctly on restart |
