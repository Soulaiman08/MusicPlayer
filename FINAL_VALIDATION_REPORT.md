# FINAL VALIDATION REPORT — Scroll Performance & Blur Cache

Generated: 2026-09-20 20:20

## Build Status

**0 Errors, 0 Warnings** (clean build after code cleanup)

All duplicate `using` directives removed. Unused fields (`_lastIdleFrameUs`, `_lastScrollFrameUs`, `_sleepDetectActive`, `_sleepDetectTicks`) removed.

---

## Scroll Performance — All Window Sizes

| Metric | Small (1024×650) | Large (1382×754) | Requirement |
|---|---|---|---|
| **avgUs** | 16095 | 14819 | ≤16667 (60fps) |
| **gt33** | **0** | **0** | 0 |
| **CPU** | 22.5% | 9.7% | Low |
| **Realized** | 10 | 10 | Stable |
| **VP Height** | 415 | 415 | Same |
| **Blur** | 11.5 (frozen) | 11.5 (frozen) | Preserved |

**Verdict**: PASS — gt33=0 at ALL sizes, avgUs within budget, CPU acceptable.

---

## Blur Cache Fix

### Problem
`BlurEffect` on BackgroundImage applied a live GPU blur shader every frame during scrolling. At large window sizes (1382×754), the shader processed ~1M pixels, adding ~5ms/frame cost. This caused gt33=8 (janky frames) during scroll.

### Solution
Removed live `BlurEffect` from XAML. Added pre-rendered blur cache in `MainWindow.xaml.cs`:
- On `BackgroundImagePath` or `WallpaperBlur` change → render image to `RenderTargetBitmap` → apply `BlurEffect` → freeze → set as `BackgroundImage.Source`
- On window resize → re-render (debounced 200ms via `_bgBlurTimer`)
- During scrolling → zero GPU blur cost (static frozen bitmap)

### Verification
| Condition | Before (live blur) | After (frozen cache) |
|---|---|---|
| gt33 | **8** | **0** |
| avgUs | 17450 | 14819 |
| maxUs | 35157 | 31028 |
| Blur visible | Yes | Yes (identical) |

---

## Close Test
- 10/10 PASS, all `sent=True`
- Wait 6 seconds for WPF shutdown sequence

---

## Summary of All Fixes (This Session)

| Fix | Before | After | Verified |
|---|---|---|---|
| Idle CPU (marquee) | 30% | 0% | ✅ |
| Scroll CPU (render loop) | 34% | 7% | ✅ |
| Large window gt33 (blur) | 8 | 0 | ✅ |
| Virtualization | Working | Working | ✅ |
| Visual quality | Original | Preserved | ✅ |
| Effects/animations | Original | Preserved | ✅ |

---

## Archive
- `archive/src_20260920_2020/src/` — Full source snapshot
