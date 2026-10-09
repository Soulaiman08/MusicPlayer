# Themes Analysis

## Theme System
- Grid gallery (3 cols portrait, 4 landscape)
- Each theme has: primary color, secondary color, background path, alpha, blur
- Default background: "skin/res/bg_001.webp"

## Theme Backgrounds
5 built-in backgrounds: bg_001.webp through bg_005.webp

## Theme Editor (ActivityThemeEdit)
- Alpha overlay: SeekBar max=255 (0=transparent, 255=opaque)
- Blur effect: SeekBar max=50 (0=none, 50=max blur)
- Pick background from gallery or internet
- Crop support via UCrop
- Reset restores: blur=0, alpha color=855638016

## Skin Components
- SkinImageView: Background image with artwork
- SkinAddView: "Add skin" button
- Widget skin change: via action

## Auto Skin
- Key: preference_auto_skin
- Off: Default background
- On: Artwork as background

## Color Theming
- Accent color configurable (default: #ff4995f3)
- Dark/light mode toggle
- Night mode support (values-night)
