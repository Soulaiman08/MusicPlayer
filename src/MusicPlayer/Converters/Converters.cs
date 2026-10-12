using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using MusicPlayer.Library;
using MusicPlayer.Models;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Converters;

// ── Slow-conversion attribution (scroll stutter forensics) ───────────────────
// Counts converter calls that took >0.5ms ON THE UI THREAD (i.e. synchronous
// image decodes stalling rendering). BurstProfiler snapshots these per render
// frame, so a dropped frame can be attributed to decode vs GC vs layout.
public static class UiSlowAttrib
{
    public static long Count;  // slow UI-thread conversions
    public static long Ms;     // their total wall ms
}

// ─────────────────────────────────────────────────────────────────────────────
// ResourceKeyToGeometryConverter
// Converts a resource-key string (e.g. "Icon.Songs") → Geometry
// This fixes the _Icon_* text regression in HomeCard and other icon elements.
// ─────────────────────────────────────────────────────────────────────────────
public class ResourceKeyToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string key && !string.IsNullOrEmpty(key))
        {
            try
            {
                return Application.Current?.TryFindResource(key) as Geometry;
            }
            catch { }
        }
        return null;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// BoolToVisibilityConverter
// usage: Visibility="{Binding SomeBool, Converter={StaticResource BoolToVisibilityConverter}}"
// usage inverse: Visibility="{Binding SomeBool, Converter={StaticResource BoolToVisibilityConverter}, ConverterParameter=inverse}"
// ─────────────────────────────────────────────────────────────────────────────
public class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isTrue  = value is bool b && b;
        var inverse = parameter is string s && s.Equals("inverse", StringComparison.OrdinalIgnoreCase);
        return (isTrue ^ inverse) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isVisible = value is Visibility v && v == Visibility.Visible;
        var inverse   = parameter is string s && s.Equals("inverse", StringComparison.OrdinalIgnoreCase);
        return isVisible ^ inverse;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DurationConverter — TimeSpan / long(ms) → "m:ss"
// ─────────────────────────────────────────────────────────────────────────────
public class DurationConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TimeSpan ts)
            return ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes}:{ts.Seconds:D2}";

        if (value is long ms)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return t.TotalHours >= 1
                ? $"{(int)t.TotalHours}:{t.Minutes:D2}:{t.Seconds:D2}"
                : $"{t.Minutes}:{t.Seconds:D2}";
        }
        return "0:00";
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// AlbumArtConverter — MusicTrack or string path → ImageSource?
// ─────────────────────────────────────────────────────────────────────────────
public class AlbumArtConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return ConvertCore(value, targetType, parameter, culture);
        }
        finally
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0
                        / System.Diagnostics.Stopwatch.Frequency;
            if (ms > 0.5 && Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                System.Threading.Interlocked.Increment(ref UiSlowAttrib.Count);
                System.Threading.Interlocked.Add(ref UiSlowAttrib.Ms, (long)ms);
            }
        }
    }

    private object? ConvertCore(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (AlbumArtLoader.Instance == null) return null;

        var paramStr = parameter as string ?? "";
        var fullQuality   = paramStr.Equals("full",   StringComparison.OrdinalIgnoreCase);
        var mediumQuality = paramStr.Equals("medium", StringComparison.OrdinalIgnoreCase);

        if (fullQuality)
        {
            if (value is MusicTrack trackFull)
                return AlbumArtLoader.Instance.LoadFullAlbumArt(trackFull);
            if (value is string path && !string.IsNullOrWhiteSpace(path))
                return AlbumArtLoader.Instance.LoadFullAlbumArt(path);
        }

        if (mediumQuality)
        {
            if (value is MusicTrack trackMed)
                return AlbumArtLoader.Instance.LoadMediumAlbumArt(trackMed);
            if (value is string pathMed && !string.IsNullOrWhiteSpace(pathMed))
                return AlbumArtLoader.Instance.LoadMediumAlbumArt(pathMed);
        }

        return value switch
        {
            MusicTrack track => AlbumArtLoader.Instance.LoadAlbumArt(track),
            string path      => AlbumArtLoader.Instance.LoadAlbumArt(path),
            _                => AlbumArtLoader.Instance.GetDefaultAlbumArt()
        };
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// StringNullOrEmptyToVisibilityConverter
// Visible when string is non-empty (or inverse: visible when empty)
// ─────────────────────────────────────────────────────────────────────────────
public class StringNullOrEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var isEmpty = string.IsNullOrWhiteSpace(value as string);
        var inverse = parameter is string s && s.Equals("inverse", StringComparison.OrdinalIgnoreCase);
        return (isEmpty ^ inverse) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// EqualityToVisibilityConverter — Visible when value.ToString() == parameter
// usage: Visibility="{Binding CurrentView, Converter={StaticResource EqualityToVisibilityConverter}, ConverterParameter=songs}"
// ─────────────────────────────────────────────────────────────────────────────
public class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var match = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        return match ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// ZeroToVisibilityConverter — Visible when int value == 0 (empty state)
// ─────────────────────────────────────────────────────────────────────────────
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int i) return i == 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// BoolToBrushConverter — returns ActiveBrush when true, InactiveBrush when false
// ─────────────────────────────────────────────────────────────────────────────
public class BoolToBrushConverter : IValueConverter
{
    public Brush ActiveBrush   { get; set; } = Brushes.CornflowerBlue;
    public Brush InactiveBrush { get; set; } = Brushes.White;

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => (value is bool b && b) ? ActiveBrush : InactiveBrush;

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// PlayModeToVisibilityConverter
// Visible when PlayMode.ToString() == parameter
// ─────────────────────────────────────────────────────────────────────────────
public class PlayModeToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var match = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        return match ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// IntEqualityToBrushConverter — returns ActiveBrush when int == parameter, InactiveBrush otherwise
// ─────────────────────────────────────────────────────────────────────────────
public class IntEqualityToBrushConverter : IValueConverter
{
    public Brush ActiveBrush   { get; set; } = new SolidColorBrush(Color.FromRgb(0x49, 0x95, 0xF3));
    public Brush InactiveBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x26, 0xFF, 0xFF, 0xFF));

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value != null && parameter != null && int.TryParse(value.ToString(), out var v) && int.TryParse(parameter.ToString(), out var p))
        {
            return v == p ? ActiveBrush : InactiveBrush;
        }
        return InactiveBrush;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// EqualityToBrushConverter — returns ActiveBrush when value == parameter, InactiveBrush otherwise
// ─────────────────────────────────────────────────────────────────────────────
public class EqualityToBrushConverter : IValueConverter
{
    public Brush ActiveBrush   { get; set; } = new SolidColorBrush(Color.FromRgb(0xFF, 0xCF, 0x4D)); // APK Accent Yellow
    public Brush InactiveBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF)); // Subtle White

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var match = string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.OrdinalIgnoreCase);
        return match ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class PlayingTrackHighlightConverter : IMultiValueConverter
{
    private static readonly Brush ActiveBrush   = new SolidColorBrush(Color.FromArgb(0x35, 0x49, 0x95, 0xF3));
    private static readonly Brush InactiveBrush = Brushes.Transparent;

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length == 2 && values[0] is int trackId && values[1] is int currentId)
            return trackId == currentId ? ActiveBrush : InactiveBrush;
        return InactiveBrush;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class DateAddedShortConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is long ticks && ticks > 0)
        {
            var date = DateTimeOffset.FromUnixTimeSeconds(ticks).LocalDateTime;
            return date.ToString("MM-dd");
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// StringToFontWeightConverter — converts "Normal"/"Bold"/"Medium" to FontWeight
public class StringToFontWeightConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        var s = value?.ToString();
        return s switch
        {
            "Bold"   => FontWeights.Bold,
            "Medium" => FontWeights.Medium,
            _        => FontWeights.Normal,
        };
    }
    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// StringToBrushConverter — converts hex color string to Brush
public class StringToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            try { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }
            catch { }
        }
        return new SolidColorBrush(Colors.White);
    }
    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ArtistImageConverter : IValueConverter
{
    public static System.Windows.Media.ImageSource? DefaultArtistImage { get; set; }
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.ImageSource> HighResCache = new(StringComparer.OrdinalIgnoreCase);

    // Cached on the UI thread when the window's DataContext is assigned. Reading
    // Application.Current.MainWindow enforces dispatcher affinity and throws when
    // touched from a worker thread (e.g. the background hi-res prefetch in
    // ArtistRow_Click), so background callers must use this cached reference.
    public static ViewModels.MainViewModel? SharedViewModel { get; set; }

    private static ViewModels.MainViewModel? GetViewModel()
    {
        var cached = SharedViewModel;
        if (cached != null) return cached;

        var app = System.Windows.Application.Current;
        if (app?.Dispatcher?.CheckAccess() == true &&
            app.MainWindow?.DataContext is ViewModels.MainViewModel vm)
        {
            SharedViewModel = vm;
            return vm;
        }
        return null;
    }

    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            return ConvertCore(value, targetType, parameter, culture);
        }
        finally
        {
            double ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0
                        / System.Diagnostics.Stopwatch.Frequency;
            if (ms > 0.5 && Application.Current?.Dispatcher?.CheckAccess() == true)
            {
                System.Threading.Interlocked.Increment(ref UiSlowAttrib.Count);
                System.Threading.Interlocked.Add(ref UiSlowAttrib.Ms, (long)ms);
            }
        }
    }

    private object ConvertCore(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string artistName || string.IsNullOrWhiteSpace(artistName))
            return DefaultArtistImage ?? string.Empty as object;

        var paramStr = parameter as string ?? "";
        bool isHighRes = paramStr.Equals("full", StringComparison.OrdinalIgnoreCase) ||
                         paramStr.Equals("detail", StringComparison.OrdinalIgnoreCase);

        if (isHighRes && HighResCache.TryGetValue(artistName, out var cachedHighRes))
            return cachedHighRes;

        if (GetViewModel() is { } vm)
        {
            if (!isHighRes && vm.ArtistImageCache.TryGetValue(artistName, out var img))
                return img;

            if (vm.ArtistImages.TryGetValue(artistName, out var path) && !string.IsNullOrWhiteSpace(path))
            {
                // While continuous scrolling is active, return placeholder immediately for regular list thumbnails
                if (!isHighRes && AlbumArtLoader.ScrollingActive)
                    return DefaultArtistImage ?? string.Empty as object;

                try
                {
                    var bi = new System.Windows.Media.Imaging.BitmapImage();
                    bi.BeginInit();
                    bi.UriSource = new System.Uri(path, System.UriKind.Absolute);
                    bi.DecodePixelWidth = isHighRes ? 400 : 180;
                    bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    if (isHighRes)
                        HighResCache[artistName] = bi;
                    else
                        vm.ArtistImageCache[artistName] = bi;
                    return bi;
                }
                catch { }
            }

            if (isHighRes && vm.ArtistImageCache.TryGetValue(artistName, out var fallbackImg))
                return fallbackImg;
        }

        return DefaultArtistImage ?? string.Empty as object;
    }
    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class WidthToItemSizeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || width < 50) return 120.0;
        double padding = 0;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            padding = p;
        double available = width - padding;
        if (available < 50) return 120.0;
        int cols = Math.Max(2, (int)(available / 160));
        return available / cols - 16.0;
    }
    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class HalfPointConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        double d = value is double v ? v : 110;
        return new Point(d / 2, d / 2);
    }
    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class HalfConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
        => value is double v ? v / 2 : 55.0;
    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class WidthToColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || width < 50) return 5;
        double padding = 0;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            padding = p;
        double available = width - padding;
        if (available < 50) return 5;
        return Math.Max(2, (int)(available / 158));
    }
    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ThemeCardWidthConverter : IValueConverter
{
    private const double AspectRatio = 16.0 / 9.0;
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || width < 100) return 200.0;
        double available = width - 32;
        int columns = Math.Max(3, (int)(available / 180));
        double cardWidth = (available / columns) - 10;
        return Math.Max(120, cardWidth);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ThemeCardHeightConverter : IValueConverter
{
    private const double AspectRatio = 16.0 / 9.0;
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || width < 100) return 113.0;
        double available = width - 32;
        int columns = Math.Max(3, (int)(available / 180));
        double cardWidth = (available / columns) - 10;
        cardWidth = Math.Max(120, cardWidth);
        return cardWidth / AspectRatio;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class ThemeColumnsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double width || width < 100) return 4;
        double available = width - 32;
        return Math.Max(3, (int)(available / 180));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class VolumeToGeometryConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        float v = 0;
        if (value is float f) v = f;
        else if (value is double d) v = (float)d;
        else if (value is int i) v = i;
        else if (value is string s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) v = p;
        else if (value != null && float.TryParse(value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var pp)) v = pp;
        if (v > 0 && v <= 1.5f) v *= 100f; // handle 0-1 range
        string key = v <= 0.5f ? "Icon.VolumeMute" : v < 33 ? "Icon.VolumeLow" : v < 66 ? "Icon.VolumeMedium" : "Icon.VolumeHigh";
        try { return Application.Current?.TryFindResource(key) as Geometry ?? Application.Current?.TryFindResource("Icon.Volume") as Geometry; } catch { return null; }
    }
    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// AlbumCoverArtConverter (MultiBinding: album name + window DataContext/VM)
// Resolves the ONE canonical cover for an album name (see
// DatabaseService.PickCanonicalTrack) instead of the card's own track art —
// multi-song pseudo-albums ("Unknown Album") otherwise show a different cover
// on every surface. Expects: values[0] = album name, values[1] = MainViewModel
// (whose AlbumCoverTracks is pre-warmed off the UI thread before cards publish,
// so this never touches the database). Falls back to the neutral placeholder.
// ─────────────────────────────────────────────────────────────────────────────
public class AlbumCoverArtConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            var loader = AlbumArtLoader.Instance;
            string? album = values.Length > 0 ? values[0] as string : null;
            MusicTrack? cover = null;
            if (!string.IsNullOrWhiteSpace(album) && values.Length > 1 &&
                values[1] is MainViewModel vm &&
                loader != null)
            {
                vm.AlbumCoverTracks.TryGetValue(album.Trim(), out cover);
            }
            if (loader == null || cover == null)
                return loader?.GetDefaultAlbumArt();
            var paramStr = parameter as string ?? "";
            if (paramStr.Equals("full", StringComparison.OrdinalIgnoreCase))
                return loader.LoadFullAlbumArt(cover);
            if (paramStr.Equals("medium", StringComparison.OrdinalIgnoreCase))
                return loader.LoadMediumAlbumArt(cover);
            return loader.LoadAlbumArt(cover);
        }
        catch { return AlbumArtLoader.Instance?.GetDefaultAlbumArt(); }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
public class RoundedClipConverter : IMultiValueConverter
{
    // Fallback keeps the image fully visible (no clipping) when layout size
    // isn't known yet, avoiding a momentary square-corner glitch.
    private static readonly Rect FallbackRect = new(double.NaN, double.NaN, double.NaN, double.NaN);

    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        double r = 16;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
            r = p;

        if (values.Length == 2)
        {
            var w = ToFinite(values[0]);
            var h = ToFinite(values[1]);
            if (w > 0 && h > 0)
                return new RectangleGeometry(new Rect(0, 0, w, h), r, r);
        }
        return null;
    }

    private static double ToFinite(object? value)
    {
        if (value is double d && !double.IsNaN(d) && !double.IsInfinity(d)) return d;
        return 0;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

public class WallpaperThumbnailConverter : IValueConverter
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Windows.Media.ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);
    private const int MaxCacheEntries = 100;

    public static System.Windows.Media.ImageSource? GetThumbnail(string thumbPath, int decodeWidth = 240)
    {
        if (string.IsNullOrWhiteSpace(thumbPath)) return null;

        if (_cache.TryGetValue(thumbPath, out var cached))
            return cached;

        if (!System.IO.File.Exists(thumbPath)) return null;

        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit();
            bi.UriSource = new Uri(thumbPath, UriKind.Absolute);
            bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bi.CreateOptions = System.Windows.Media.Imaging.BitmapCreateOptions.None;
            if (decodeWidth > 0)
                bi.DecodePixelWidth = decodeWidth;
            bi.EndInit();
            bi.Freeze();

            if (_cache.Count < MaxCacheEntries)
                _cache.TryAdd(thumbPath, bi);

            return bi;
        }
        catch
        {
            return null;
        }
    }

    public static void PreloadThumbnails(System.Collections.Generic.IEnumerable<string> paths, int decodeWidth = 240)
    {
        foreach (var path in paths)
        {
            if (string.IsNullOrWhiteSpace(path)) continue;
            if (_cache.ContainsKey(path)) continue;
            GetThumbnail(path, decodeWidth);
        }
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is System.Windows.Media.ImageSource img)
            return img;

        if (value is not string thumbPath || string.IsNullOrWhiteSpace(thumbPath))
            return DependencyProperty.UnsetValue;

        if (_cache.TryGetValue(thumbPath, out var cached))
            return cached;

        var loaded = GetThumbnail(thumbPath, 240);
        return loaded ?? DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

// ─────────────────────────────────────────────────────────────────────────────
// FallbackIfEmptyConverter — Returns fallback string (e.g. "Unknown") if value is null or empty
// ─────────────────────────────────────────────────────────────────────────────
public class FallbackIfEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object parameter, CultureInfo culture)
    {
        string? text = value?.ToString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return parameter?.ToString() ?? "Unknown";
        }
        return text;
    }

    public object ConvertBack(object? value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

