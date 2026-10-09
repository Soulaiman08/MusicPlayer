# UI Styles Analysis

## App Theme (AppTheme)
- Parent: Theme.AppCompat.Light.NoActionBar
- Window background: white
- No title bar
- Accent color: #ff4995f3 (color_theme)
- Action bar size: 56dp
- Window animations: music_activity_in/out
- Split motion events: false
- Soft input: adjustPan

## Toolbar Theme (AppToolbar)
- Parent: ThemeOverlay.AppCompat.Dark.ActionBar
- Background: transparent
- Title: 20sp, white
- Subtitle: 14sp, #80ffffff

## Tab Layout (AppTabLayoutText)
- Text size: 14sp
- Font: sans-serif-medium
- All caps: false

## Activity Transitions
- Enter: music_activity_in.xml
- Exit: music_activity_out.xml
- Open enter: music_activity_in.xml
- Open exit: music_activity_out.xml
- Close enter: music_activity_in.xml
- Close exit: music_activity_out.xml

## Dialog Animations
- Bottom dialog: music_bottom_dialog_in/out
- Popup: popup_fade_in

## Night Mode
- Supported via values-night/styles.xml
- Dark background: #ff1d2026
- Dark toolbar: #33000000

## Variants
- AppTheme.Transparent - For player/lock screen
- AppTheme.Drive - For drive mode
- AppTheme.Lock - For lock screen
- AppTheme.Empty - For shortcut launcher
- AppIgnoreNightTheme - Ignores dark mode
