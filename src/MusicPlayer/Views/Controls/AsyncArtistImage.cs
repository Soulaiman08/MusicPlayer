using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.ImageLoading;
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
    private string? _pendingArtist;
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

    internal enum ImageRequestDecision
    {
        Request,
        SkipNoArtist,
        SkipNoViewModel,
        SkipNotLoaded
    }

    /// <summary>
    /// Decides whether an artist thumbnail request may start.
    ///
    /// The viewport position is deliberately NOT part of the decision: a
    /// virtualizing panel binds an item to a recycled container *before* the
    /// container is arranged, so the hit-test fails exactly when the request is
    /// made. Treating "outside the viewport" as a reason to skip dropped the
    /// request with nothing to retry it, and the tiles stayed empty until the
    /// window was resized or the metadata was refreshed.
    /// </summary>
    internal static ImageRequestDecision DecideImageRequest(
        string? artist,
        bool hasViewModel,
        bool isLoaded,
        bool isInViewport)
    {
        if (string.IsNullOrWhiteSpace(artist)) return ImageRequestDecision.SkipNoArtist;
        if (!hasViewModel) return ImageRequestDecision.SkipNoViewModel;
        if (!isLoaded) return ImageRequestDecision.SkipNotLoaded;
        _ = isInViewport; // intentionally ignored, see the remarks above.
        return ImageRequestDecision.Request;
    }

    private static void OnArtistNameChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var control = (AsyncArtistImage)d;
        control.ResetForNewArtist();
        control.RequestImage();
    }

    /// <summary>
    /// A virtualizing panel reuses containers: the same control can be bound to a
    /// different artist while the previous decode is still running. The old bitmap
    /// has to be cleared (otherwise the tile keeps showing the previous artist)
    /// and the in-flight request has to be invalidated so a late decode cannot
    /// paint the wrong artist.
    /// </summary>
    private void ResetForNewArtist()
    {
        Interlocked.Increment(ref _requestVersion);
        _pendingArtist = null;
        _diagnosticWaitingForArtistPath = false;
        Source = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        AttachScrollViewer();
        _viewModel = Application.Current?.MainWindow?.DataContext as MainViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        SizeChanged -= OnSizeChangedForImageRetry;
        SizeChanged += OnSizeChangedForImageRetry;
        Dispatcher.BeginInvoke(RequestImage, System.Windows.Threading.DispatcherPriority.Loaded);
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
            // The Artists grid is virtualized: a container exists because it was
            // realized for the viewport (plus a small cache). Rejecting the
            // request because the control has not been arranged *yet* is what
            // left recycled tiles empty - the binding happens before the arrange,
            // and nothing retried afterwards.
            WriteFocusedImageDiagnostic(
                $"retry proceeding although outside viewport size={FormatSize(RenderSize)} loaded={IsLoaded}");
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
            _scrollViewer.SizeChanged -= OnScrollViewerSizeChanged;
        }

        _scrollViewer = scrollViewer;
        if (_scrollViewer != null)
        {
            // No per-control ScrollChanged subscription: the Artists grid now
            // virtualizes, so containers are created/rebound as they enter the
            // viewport and the DataContext/Loaded path already requests the image.
            // Keeping one handler per image made every scroll event run N viewport
            // hit-tests on the UI thread.
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

        // Do not gate the retry on the viewport: recycled tiles are bound before
        // they are arranged, so the hit-test failed exactly when a retry was
        // needed.
        RequestImage();
    }

    private void RequestImage()
    {
        var artist = ArtistName?.Trim();
        var vm = _viewModel ?? (Application.Current?.MainWindow?.DataContext as MainViewModel);
        if (!string.IsNullOrEmpty(artist) &&
            string.Equals(_pendingArtist, artist, StringComparison.OrdinalIgnoreCase))
        {
            // A decode for this artist is already running for this control.
            return;
        }
        var isCkayDiagnostic = string.Equals(artist, "CKay", StringComparison.OrdinalIgnoreCase);
        var version = Interlocked.Increment(ref _requestVersion);
        if (_diagnosticRequestCount < 2)
        {
            _diagnosticRequestCount++;
            WriteImageDiagnostic(
                $"RequestImage start artist='{artist ?? "<empty>"}' size={FormatSize(RenderSize)} " +
                $"loaded={IsLoaded} sourceSet={Source is not null}");
        }

        if (DecideImageRequest(artist, vm != null, IsLoaded, IsInViewport()) != ImageRequestDecision.Request)
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

        // Also check the coordinator's shared memory cache (populated by ArtistImageConverter / ArtistImageProvider).
        var coord = ImageLoadingCoordinator.Instance;
        if (coord != null)
        {
            var coordCached = coord.TryGetArtistImage(artist, isHighRes: false)
                           ?? coord.TryGetArtistImage(artist, isHighRes: true);
            if (coordCached != null)
            {
                _diagnosticWaitingForArtistPath = false;
                WriteImageDiagnostic($"coordinator memory cache hit artist='{artist}'");
                var image = vm.ArtistImageCache.GetOrAdd(artist, coordCached);
                vm.TrimArtistImageCache(artist);
                Source = image;
                return;
            }
        }

        if (!vm.ArtistImages.TryGetValue(artist, out var path) || string.IsNullOrWhiteSpace(path))
        {
            _diagnosticWaitingForArtistPath = true;
            WriteImageDiagnostic($"RequestImage ended without Source: artist path missing artist='{artist}'");
            Source = null;
            return;
        }

        _diagnosticWaitingForArtistPath = false;
        // The grid is virtualized, so this control only exists for an item that
        // was realized for (or next to) the viewport. The binding happens before
        // the arrange, so a viewport hit-test at this point is unreliable: it
        // used to reject the request and nothing ever retried it, which left
        // recycled tiles empty until the window was resized.
        var inViewport = IsInViewport();
        WriteImageDiagnostic(
            $"artist path found artist='{artist}' cacheHit=false inViewport={inViewport} " +
            $"size={FormatSize(RenderSize)}");
        if (isCkayDiagnostic)
        {
            WriteFocusedImageDiagnostic($"path='{path}' fileExists={File.Exists(path)} inViewport={inViewport}");
        }

        _pendingArtist = artist;

        WriteFocusedImageDiagnostic(
            $"decode started path='{path}' fileExists={File.Exists(path)} pathFound=true " +
            $"artistBinding=" +
            $"{GetBindingExpression(ArtistNameProperty)?.Status.ToString() ?? "no-binding"} " +
            $"dataContext={DataContext?.GetType().Name ?? "null"}");
        _ = Task.Run(() => Decode(path, artist, WriteImageDiagnostic, WriteFocusedImageDiagnostic)).ContinueWith(task =>
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (string.Equals(_pendingArtist, artist, StringComparison.OrdinalIgnoreCase))
                    _pendingArtist = null;
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
                vm.TrimArtistImageCache(artist);

                // Also populate the coordinator's shared memory cache so ArtistImageConverter
                // and other consumers can hit it without re-decoding.
                var coordForCache = ImageLoadingCoordinator.Instance;
                if (coordForCache != null && image is BitmapSource)
                {
                    coordForCache.MemoryCache.Set(
                        ArtistImageProvider.NormalizeKey(artist),
                        ImageKind.Artist,
                        ImageQuality.Thumbnail,
                        image);
                }
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
