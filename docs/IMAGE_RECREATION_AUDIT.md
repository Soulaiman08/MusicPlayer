# IMAGE RECREATION AUDIT — Original Android Music Player Screenshots
Date: 2026-09-01 | Engine: OpenCode + Muse Spark 1.2 | Device: Intel i5-4210U

## IMAGES ATTACHED (5 total)

### Image 1: Library + Sort Dropdown
**Original screen:** Library → Tracks tab → ⋮ overflow menu → "Sort by" → dropdown
**Elements identified:**
- Top bar: ← Back | "LIBRARY" title | AD icon (with "9" badge) | Search icon | ⋮ overflow
- Tabs row: TRACKS (active, yellow underline) | ARTISTS
- Sort dropdown panel (right side, white background, rounded corners):
  - Title: "Sort by"
  - Options: A-Z, Z-A, Year, Artist, Album, Folder, **Date added ✓** (selected, yellow check), Size, Duration, Random, Reverse
- Track list: 48x48 album art, title, artist + date "09-01", ⋮ per row
- Floating Action Button: Yellow circle with shuffle icon (bottom right)
- Mini player: 48x48 art, "Faouzia UNETHICAL C" title, "Change song by swipe" subtitle, ▶ Play, ⏭ Next, ⏮ Queue buttons

**Menus:** Sort dropdown (11 options)
**Interactions:** Tap ⋮ → menu, Tap "Sort by" → sort dropdown
**Windows implementation:** Has sort options in `LibrarySortOptions` but no dropdown UI. Has `ShuffleAll` for FAB.
**Differences:** No dropdown UI for sort, no tabs row, no "Change song by swipe" text, no AD icon
**Fixes required:**
1. Sort dropdown UI with 11 options + yellow check indicator
2. Tabs row TRACKS/ARTISTS/ALBUMS/GENRES as inline tabs
3. "Change song by swipe" text in mini player
4. AD icon in top bar

---

### Image 2: Library + ⋮ Overflow Menu
**Original screen:** Library → Tracks tab → ⋮ overflow menu opened
**Elements identified:**
- Top bar: ← Back | "LIBRARY" | AD(9) | Search | ⋮
- Tabs: TRACKS (active) | ARTISTS
- Dropdown menu (white, rounded, right side):
  - "Select"
  - "Shuffle all"
  - "Sort by ▶" (with submenu indicator)
- Track list behind
- Floating shuffle FAB
- Mini player

**Menus:** Overflow menu (3 items + submenu)
**Interactions:** Tap ⋮ → menu opens, Tap "Sort by" → submenu with 11 options
**Windows implementation:** No overflow menu UI, no Select mode
**Differences:** Missing entire overflow menu
**Fixes required:**
1. Overflow menu UI with Select/Shuffle all/Sort by items
2. Select mode for multi-select tracks
3. Sort by submenu

---

### Image 3: Library Default View
**Original screen:** Library → Tracks tab (default)
**Elements identified:**
- Top bar: ← Back | "LIBRARY" | AD(9) | Search | ⋮
- Tabs: **TRACKS** (yellow underline) | **ARTISTS** | **ALBUMS** | **GENRES** (4 inline tabs)
- Track list (8+ visible tracks) with album art, title, artist + date "09-01", ⋮ per row
- Yellow shuffle FAB (bottom right, floating)
- Mini player with shuffle icon overlay on art, "Faouzia UNETHICAL C", "Change song by swipe", play/next/queue

**Interactions:** Tap tab → switch view (TRACKS/ARTISTS/ALBUMS/GENRES), Tap FAB → shuffle all
**Windows implementation:** Uses drawer navigation, no inline tabs
**Differences:** Major layout difference - should add inline tabs as Windows alternative
**Fixes required:**
1. Inline tabs row with TRACKS/ARTISTS/ALBUMS/GENRES
2. Yellow shuffle FAB button

---

### Image 4: Track Context Bottom Sheet
**Original screen:** Track row tap → bottom sheet slides up
**Elements identified:**
- Top bar (background dimmed): ← Back | "LIBRARY" | AD(9) | Search | ⋮
- Tabs: TRACKS | ARTISTS
- Track list (dimmed)
- **Bottom sheet (dark background, rounded top corners):**
  - Title: "BABYMONSTER_ DRIP/ MV" + ♡ Favorite icon (heart) + ⓘ Info icon
  - **Row 1 (4 icons):**
    - ⏭ **Play next** (skip-forward icon)
    - ⊕ **Add to** (plus-in-square icon)
    - ☰ **Enqueue** (list with plus icon)
    - 🔔 **Ringtone** (bell icon)
  - **Row 2 (4 icons):**
    - ✂ **Trim** (scissors icon)
    - 🖼 **Artwork** (image icon)
    - ⇪ **Share** (share icon)
    - 🗑 **Delete** (trash icon)

**Menus:** Track bottom sheet (8 actions + favorite + info)
**Interactions:** Tap track row → bottom sheet, Tap action → execute
**Windows implementation:** Has `OpenEditTags`, `SaveEditTags`, `SaveAlbumArt` but no bottom sheet UI
**Differences:** No bottom sheet UI, missing Trim, Ringtone, Share, Play next, Enqueue, Add to, Delete actions
**Fixes required:**
1. Track bottom sheet UI (dark, rounded, 2 rows × 4 icons)
2. Play next command
3. Add to (playlist selector)
4. Enqueue (add to end of queue)
5. Ringtone (set as ringtone - Windows: open file location)
6. Trim (audio editor - already exists as placeholder)
7. Artwork (already exists)
8. Share (open file in explorer)
9. Delete (delete from library + file)

---

### Image 5: Search Screen
**Original screen:** Library → Search icon tap → Search activity
**Elements identified:**
- Top bar: ← Back | "Search library" textbox (placeholder)
- Section: "Songs" with ⌃ collapse arrow
- Track list: album art, title, artist, ⋮ per row
- Currently playing track "Faouzia UNETHICAL O..." highlighted yellow with shuffle icon overlay
- Mini player: "Faouzia UNETHICAL Official /MV", "Change song by swipe", play/next/queue

**Interactions:** Type → live filter, Tap track → play, Tap ⋮ → context menu
**Windows implementation:** Has `SearchQuery` with debounce but no dedicated search screen
**Differences:** No dedicated search screen, no "Songs" section header, no collapse arrow
**Fixes required:**
1. Search screen with dedicated "Search library" textbox
2. "Songs" section header with collapse arrow
3. Current track highlighted in yellow
4. Shuffle icon overlay on current track art

---

## WINDOWS IMPLEMENTATION CHECKLIST

| Image | Element | Status | Action |
|---|---|---|---|
| 1 | Sort dropdown 11 options | Missing UI | Add dropdown |
| 1 | Tabs row | Missing | Add inline tabs |
| 1 | "Change song by swipe" | Missing | Add text |
| 1 | AD icon | N/A (Windows) | Skip |
| 2 | Overflow menu | Missing | Add menu |
| 2 | Select mode | Missing | Add multi-select |
| 2 | Sort by submenu | Missing | Add submenu |
| 3 | 4 inline tabs | Missing | Add tabs |
| 3 | Yellow shuffle FAB | Missing | Add FAB |
| 4 | Bottom sheet UI | Missing | Add bottom sheet |
| 4 | Play next | Missing | Add command |
| 4 | Add to | Missing | Add command |
| 4 | Enqueue | Missing | Add command |
| 4 | Ringtone | Missing | Add command (Windows: open file) |
| 4 | Trim | Has placeholder | Wire to AudioEditor |
| 4 | Artwork | Has | Wire UI |
| 4 | Share | Missing | Add command (open file location) |
| 4 | Delete | Has | Wire to delete track |
| 4 | Favorite | Has | Wire to ToggleFavorite |
| 4 | Info | Has | Wire to metadata |
| 5 | Search screen | Has search | Add dedicated view |
| 5 | "Songs" section | Missing | Add section header |
| 5 | Collapse arrow | Missing | Add toggle |
| 5 | Current track highlight | Missing | Add highlight |
| 5 | Shuffle icon overlay | Missing | Add overlay |

---

## IMPLEMENTATION PLAN

### Batch 1: Track Bottom Sheet (CRITICAL - Image 4)
8 actions: Play next, Add to, Enqueue, Ringtone, Trim, Artwork, Share, Delete
+ Favorite + Info icons
Dark bottom sheet with rounded top, 2 rows × 4 icons
Each action must call existing or new commands

### Batch 2: Library Tabs + Sort Dropdown (Image 1 + 3)
4 inline tabs: TRACKS/ARTISTS/ALBUMS/GENRES
Sort dropdown with 11 options + yellow check
Yellow shuffle FAB button
"Change song by swipe" in mini player

### Batch 3: Search Screen (Image 5)
Dedicated search view
"Songs" section header with collapse
Current track yellow highlight
Shuffle icon overlay on current track

### Batch 4: Overflow Menu (Image 2)
Select mode (multi-select)
Shuffle all
Sort by submenu

### Batch 5: Documentation
Update FINAL_RECREATION_AUDIT.md
Update IMAGE_RECREATION_AUDIT.md
Build + test
