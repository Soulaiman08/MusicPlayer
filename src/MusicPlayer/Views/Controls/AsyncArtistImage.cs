using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Views.Controls;

/// <summary>Loads artist thumbnails away from the dispatcher and ignores stale results.</summary>
public sealed class AsyncArtistImage : Image
{
    public static readonly DependencyProperty ArtistNameProperty = DependencyProperty.Register(
        nameof(ArtistName), typeof(string), typeof(AsyncArtistImage),
        new PropertyMetadata(string.Empty, OnArtistNameChanged));

    private MainViewModel? _viewModel;
    private ScrollViewer? _scrollViewer;
    private int _requestVersion;
    private readonly HashSet<string> _loadingArtists = new(StringComparer.OrdinalIgnoreCase);
    private int _focusedDiagnosticCount;

    // BEGIN TEMPORARY ARTIST IMAGE DIAGNOSTICS
    private const int MaxDiagnosticMessagesPerImage = 8;
    private const int MaxDiagnosticMessagesTotal = 1800;
    private const int MaxFocusedDiagnosticMessages = 40;
    private static int _diagnosticMessagesTotal;
    private static int _focusedDiagnosticMessagesTotal;
    private static readonly object FocusedDiagnosticFileLock = new();
    private static readonly string FocusedDiagnosticFilePath = Path.Combine(
        Path.GetTempPath(),
        $"MusicPlayer-CKay-diagnostic-{Environment.ProcessId}.log");
    private int _diagnosticMessagesForImage;
    private int _diagnosticRequestCount;
    private bool _diagnosticWaitingForArtistPath;
    // END TEMPORARY ARTIST IMAGE DIAGNOSTICS

    public AsyncArtistImage()
    {
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public string ArtistName
    {
        get => (string)GetValue(ArtistNameProperty);
        set => SetValue(ArtistNameProperty, value);
    }

    public bool HasArtistImage => Source is ImageSource;

    private static void OnArtistNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((AsyncArtistImage)d).RequestImage();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewer();
        _viewModel = Application.Current?.MainWindow?.DataContext as MainViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged -= OnSizeChangedForImageRetry;
        SizeChanged += OnSizeChangedForImageRetry;
        Dispatcher.BeginInvoke(TryRequestImageWhenVisible, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnSizeChangedForImageRetry(object? sender, SizeChangedEventArgs e)
    {
        WriteFocusedImageDiagnostic(
            $"SizeChanged old={FormatSize(e.PreviousSize)} new={FormatSize(e.NewSize)} " +
            $"loaded={IsLoaded} visible={IsVisible}");
        TryRequestImageWhenVisible();
    }

    private void OnScrollViewerSizeChanged(object sender, SizeChangedEventArgs e)
    {
        WriteFocusedImageDiagnostic(
            $"ScrollViewer SizeChanged old={FormatSize(e.PreviousSize)} new={FormatSize(e.NewSize)}");
        TryRequestImageWhenVisible();
    }

    private void TryRequestImageWhenVisible()
    {
        AttachScrollViewer();

        if (Source is not null)
        {
            SizeChanged -= OnSizeChangedForImageRetry;
            return;
        }

        var layoutElement = GetViewportElement();
        if (layoutElement.ActualWidth <= 0 || layoutElement.ActualHeight <= 0 ||
            layoutElement.RenderSize.Width <= 0 || layoutElement.RenderSize.Height <= 0)
        {
            WriteFocusedImageDiagnostic(
                $"retry deferred: no container size image={FormatSize(RenderSize)} " +
                $"container={FormatSize(layoutElement.RenderSize)} loaded={IsLoaded}");
            return;
        }

        if (!IsInViewport())
        {
            WriteFocusedImageDiagnostic(
                $"retry deferred: outside actual viewport size={FormatSize(RenderSize)} loaded={IsLoaded}");
            return;
        }

        SizeChanged -= OnSizeChangedForImageRetry;
        WriteFocusedImageDiagnostic(
            $"retry eligible image={FormatSize(RenderSize)} " +
            $"container={FormatSize(layoutElement.RenderSize)} loaded={IsLoaded} visible={IsVisible}");
        RequestImage();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        SizeChanged -= OnSizeChangedForImageRetry;
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
        }
        _scrollViewer = null;
        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
        Interlocked.Increment(ref _requestVersion);
    }

    private void AttachScrollViewer()
    {
        var scrollViewer = FindAncestorScrollViewer(this);
        if (ReferenceEquals(scrollViewer, _scrollViewer)) return;

        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
        }

        _scrollViewer = scrollViewer;
        if (_scrollViewer != null)
        {
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _scrollViewer.SizeChanged += OnScrollViewerSizeChanged;
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ArtistImages)) return;

        var inViewport = IsInViewport();
        if (_diagnosticWaitingForArtistPath)
        {
            var artist = ArtistName?.Trim();
            var pathAvailable = !string.IsNullOrEmpty(artist) &&
                                _viewModel?.ArtistImages.TryGetValue(artist, out var path) == true &&
                                !string.IsNullOrWhiteSpace(path);
            WriteImageDiagnostic(
                $"ArtistImages notification after prior no-path attempt; pathAvailable={pathAvailable} " +
                $"inViewport={inViewport} size={FormatSize(RenderSize)}");
            if (pathAvailable) _diagnosticWaitingForArtistPath = false;
        }

        if (inViewport) RequestImage();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        WriteFocusedImageDiagnostic(
            $"ScrollChanged verticalOffset={e.VerticalOffset:0.##} viewportHeight={e.ViewportHeight:0.##}");
        if (Source is not null || !IsInViewport()) return;

        SizeChanged -= OnSizeChangedForImageRetry;
        TryRequestImageWhenVisible();
    }

    private void RequestImage()
    {
        var artist = ArtistName?.Trim();
        var vm = _viewModel ?? (Application.Current?.MainWindow?.DataContext as MainViewModel);
        if (!string.IsNullOrEmpty(artist) && _loadingArtists.Contains(artist)) return;
        var isCkayDiagnostic = string.Equals(artist, "CKay", StringComparison.OrdinalIgnoreCase);
        var version = Interlocked.Increment(ref _requestVersion);
        if (_diagnosticRequestCount < 2)
        {
            _diagnosticRequestCount++;
            WriteImageDiagnostic(
                $"RequestImage start artist='{artist ?? "<empty>"}' size={FormatSize(RenderSize)} " +
                $"loaded={IsLoaded} sourceSet={Source is not null}");
        }

        if (string.IsNullOrEmpty(artist) || vm == null || !IsLoaded)
        {
            Source = null;
            WriteImageDiagnostic(
                $"RequestImage ended without Source: invalid artist/view-model or not loaded " +
                $"artistValid={!string.IsNullOrEmpty(artist)} viewModelAvailable={vm != null} loaded={IsLoaded}");
            return;
        }

        if (vm.ArtistImageCache.TryGetValue(artist, out var cached))
        {
            _diagnosticWaitingForArtistPath = false;
            WriteImageDiagnostic($"image cache hit artist='{artist}'");
            Source = cached;
            return;
        }

        if (!vm.ArtistImages.TryGetValue(artist, out var path) || string.IsNullOrWhiteSpace(path))
        {
            _diagnosticWaitingForArtistPath = true;
            WriteImageDiagnostic($"RequestImage ended without Source: artist path missing artist='{artist}'");
            Source = null;
            return;
        }

        _diagnosticWaitingForArtistPath = false;
        // BalancedItemsPanel realizes the full grid. Defer disk reads and image decoding
        // for tiles outside the visible ScrollViewer viewport until scrolling reveals them.
        var inViewport = IsInViewport();
        WriteImageDiagnostic(
            $"artist path found artist='{artist}' cacheHit=false inViewport={inViewport} " +
            $"size={FormatSize(RenderSize)}");
        if (isCkayDiagnostic)
        {
            WriteFocusedImageDiagnostic($"path='{path}' fileExists={File.Exists(path)} inViewport={inViewport}");
        }
        if (!inViewport)
        {
            WriteImageDiagnostic($"RequestImage ended without Source: outside viewport artist='{artist}'");
            return;
        }

        if (!_loadingArtists.Add(artist)) return;

        WriteFocusedImageDiagnostic(
            $"decode started path='{path}' fileExists={File.Exists(path)} pathFound=true " +
            $"artistBinding=" +
            $"{GetBindingExpression(ArtistNameProperty)?.Status.ToString() ?? "no-binding"} " +
            $"dataContext={DataContext?.GetType().Name ?? "null"}");
        _ = Task.Run(() => Decode(path, artist, WriteImageDiagnostic, WriteFocusedImageDiagnostic)).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                _loadingArtists.Remove(artist);
                if (task.Status != TaskStatus.RanToCompletion)
                {
                    var exception = task.Exception?.GetBaseException();
                    WriteImageDiagnostic(
                        $"image decode task failed artist='{artist}' status={task.Status} " +
                        $"exception={exception?.ToString() ?? "unknown"}");
                    WriteFocusedImageDiagnostic($"decode task failed exception={exception?.ToString() ?? "unknown"}");
                    return;
                }

                if (task.Result == null)
                {
                    WriteImageDiagnostic($"image decode returned null artist='{artist}'");
                    return;
                }

                WriteImageDiagnostic($"image decode succeeded artist='{artist}'");
                var image = vm.ArtistImageCache.GetOrAdd(artist, task.Result);
                WriteFocusedImageDiagnostic(
                    $"ImageSource created type={image.GetType().Name} " +
                    $"size={image.Width:0.##}x{image.Height:0.##} " +
                    $"pixelSize={(image is BitmapSource bitmap ? $"{bitmap.PixelWidth}x{bitmap.PixelHeight}" : "n/a")} " +
                    $"frozen={(image is BitmapSource frozenBitmap && frozenBitmap.IsFrozen)} " +
                    $"cacheContains={vm.ArtistImageCache.ContainsKey(artist)}");
                if (IsLoaded && version == Volatile.Read(ref _requestVersion) &&
                    string.Equals(ArtistName?.Trim(), artist, StringComparison.OrdinalIgnoreCase))
                {
                    Source = image;
                    WriteImageDiagnostic($"Source assigned artist='{artist}'");
                    WriteFocusedImageDiagnostic(
                        $"Source assigned sameSource={ReferenceEquals(Source, image)} " +
                        $"size={FormatSize(RenderSize)} visible={IsVisible} visibility={Visibility} " +
                        $"stretch={Stretch} clip={Clip?.GetType().Name ?? "none"} " +
                        $"parent={VisualTreeHelper.GetParent(this)?.GetType().Name ?? "none"}");
                }
                else
                {
                    WriteImageDiagnostic(
                        $"Source not assigned: request stale or control unloaded artist='{artist}' " +
                        $"loaded={IsLoaded} requestCurrent={version == Volatile.Read(ref _requestVersion)}");
                    if (IsLoaded && string.Equals(ArtistName?.Trim(), artist, StringComparison.OrdinalIgnoreCase))
                        TryRequestImageWhenVisible();
                }
            }, System.Windows.Threading.DispatcherPriority.Background);
        }, TaskScheduler.Default);
    }

    private bool IsInViewport()
    {
        var scrollViewer = _scrollViewer ?? FindAncestorScrollViewer(this);
        var layoutElement = GetViewportElement();
        if (scrollViewer == null || !IsLoaded || !IsVisible ||
            !layoutElement.IsVisible || layoutElement.ActualWidth <= 0 || layoutElement.ActualHeight <= 0 ||
            layoutElement.RenderSize.Width <= 0 || layoutElement.RenderSize.Height <= 0)
        {
            WriteFocusedImageDiagnostic(
                $"viewport check unavailable scrollViewer={scrollViewer != null} loaded={IsLoaded} " +
                $"visible={IsVisible} imageSize={FormatSize(RenderSize)} " +
                $"containerSize={FormatSize(layoutElement.RenderSize)}");
            return false;
        }

        try
        {
            var contentPresenter = FindScrollContentPresenter(scrollViewer);
            if (contentPresenter == null || contentPresenter.ActualWidth <= 0 || contentPresenter.ActualHeight <= 0)
            {
                WriteFocusedImageDiagnostic(
                    $"viewport check unavailable contentPresenter={contentPresenter != null} " +
                    $"presenterSize={(contentPresenter == null ? "n/a" : FormatSize(contentPresenter.RenderSize))}");
                return false;
            }

            var bounds = layoutElement
                .TransformToAncestor(contentPresenter)
                .TransformBounds(new Rect(new Point(0, 0), layoutElement.RenderSize));
            var viewport = new Rect(0, 0, contentPresenter.ActualWidth, contentPresenter.ActualHeight);
            var intersects = bounds.IntersectsWith(viewport);
            WriteFocusedImageDiagnostic(
                $"viewport check bounds={FormatRect(bounds)} viewport={FormatRect(viewport)} " +
                $"presenterSize={FormatSize(contentPresenter.RenderSize)} " +
                $"imageSize={FormatSize(RenderSize)} containerSize={FormatSize(layoutElement.RenderSize)} " +
                $"result={intersects}");
            return intersects;
        }
        catch (InvalidOperationException)
        {
            // The visual tree may be reparenting during virtualization or tab changes.
            return false;
        }
    }

    private FrameworkElement GetViewportElement()
    {
        if (Source is null && VisualTreeHelper.GetParent(this) is FrameworkElement container)
            return container;

        return this;
    }

    private static ScrollContentPresenter? FindScrollContentPresenter(DependencyObject root)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollContentPresenter presenter) return presenter;
            var nested = FindScrollContentPresenter(child);
            if (nested != null) return nested;
        }

        return null;
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scrollViewer) return scrollViewer;
        return null;
    }

    private static BitmapImage? Decode(
        string path,
        string artist,
        Action<string> diagnostic,
        Action<string> focusedDiagnostic)
    {
        try
        {
            if (string.Equals(artist, "CKay", StringComparison.OrdinalIgnoreCase))
            {
                var exists = File.Exists(path);
                try
                {
                    using var stream = new FileStream(
                        path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var readable = stream.Length == 0 || stream.ReadByte() >= 0;
                    focusedDiagnostic(
                        $"decode started fileExists={exists} readable={readable} bytes={stream.Length}");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
                {
                    focusedDiagnostic(
                        $"file read check failed fileExists={exists} exception={ex}");
                    throw;
                }
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = 180;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            focusedDiagnostic(
                $"decode completed pixelSize={image.PixelWidth}x{image.PixelHeight} frozen={image.IsFrozen}");
            return image;
        }
        catch (IOException ex)
        {
            diagnostic($"image decode failed artist='{artist}' exception={ex}");
            focusedDiagnostic($"decode failed exception={ex}");
            return null;
        }
        catch (UriFormatException ex)
        {
            diagnostic($"image decode failed artist='{artist}' exception={ex}");
            focusedDiagnostic($"decode failed exception={ex}");
            return null;
        }
        catch (NotSupportedException ex)
        {
            diagnostic($"image decode failed artist='{artist}' exception={ex}");
            focusedDiagnostic($"decode failed exception={ex}");
            return null;
        }
    }

    // BEGIN TEMPORARY ARTIST IMAGE DIAGNOSTICS
    private void WriteImageDiagnostic(string message)
    {
        if (Interlocked.Increment(ref _diagnosticMessagesForImage) > MaxDiagnosticMessagesPerImage) return;
        if (Interlocked.Increment(ref _diagnosticMessagesTotal) > MaxDiagnosticMessagesTotal) return;
        Debug.WriteLine($"[TEMP-ARTIST-IMAGE] {message}");
    }

    private void WriteFocusedImageDiagnostic(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(
                new Action(() => WriteFocusedImageDiagnostic(message)),
                System.Windows.Threading.DispatcherPriority.Background);
            return;
        }

        if (!string.Equals(ArtistName?.Trim(), "CKay", StringComparison.OrdinalIgnoreCase)) return;
        if (Interlocked.Increment(ref _focusedDiagnosticMessagesTotal) > MaxFocusedDiagnosticMessages ||
            Interlocked.Increment(ref _focusedDiagnosticCount) > MaxFocusedDiagnosticMessages)
            return;
        var entry =
            $"[{DateTimeOffset.Now:O}] [TEMP-ARTIST-IMAGE-CKAY] {message} " +
            $"sourceSet={Source is not null} artist='{ArtistName}' " +
            $"size={FormatSize(RenderSize)} loaded={IsLoaded}";
        Debug.WriteLine(entry);
        try
        {
            lock (FocusedDiagnosticFileLock)
                File.AppendAllText(FocusedDiagnosticFilePath, entry + Environment.NewLine);
        }
        catch (IOException ex)
        {
            Debug.WriteLine($"[TEMP-ARTIST-IMAGE-CKAY] diagnostic file write failed: {ex}");
        }
        catch (UnauthorizedAccessException ex)
        {
            Debug.WriteLine($"[TEMP-ARTIST-IMAGE-CKAY] diagnostic file write failed: {ex}");
        }
    }

    private static string FormatSize(Size size)
        => FormattableString.Invariant($"{size.Width:0.##}x{size.Height:0.##}");

    private static string FormatRect(Rect rect)
        => FormattableString.Invariant(
            $"{rect.X:0.##},{rect.Y:0.##} {rect.Width:0.##}x{rect.Height:0.##}");
    // END TEMPORARY ARTIST IMAGE DIAGNOSTICS
}
