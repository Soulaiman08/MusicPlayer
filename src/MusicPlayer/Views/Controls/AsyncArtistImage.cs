using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MusicPlayer.ImageLoading;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Views.Controls;

/// <summary>Loads artist thumbnails via the centralized ImageLoadingCoordinator.</summary>
public sealed class AsyncArtistImage : Image
{
    public static readonly DependencyProperty ArtistNameProperty = DependencyProperty.Register(
        nameof(ArtistName), typeof(string), typeof(AsyncArtistImage),
        new PropertyMetadata(string.Empty, OnArtistNameChanged));

    private MainViewModel? _viewModel;
    private int _requestVersion;
    private CancellationTokenSource? _requestCts;

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
        _ = isInViewport; // intentionally ignored
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
    /// has to be cleared and the in-flight request has to be cancelled so a late
    /// decode cannot paint the wrong artist.
    /// </summary>
    private void ResetForNewArtist()
    {
        // Cancel any in-flight request for the previous artist
        _requestCts?.Cancel();
        _requestCts?.Dispose();
        _requestCts = null;

        Interlocked.Increment(ref _requestVersion);
        Source = null;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel = Application.Current?.MainWindow?.DataContext as MainViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RequestImage();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
        ResetForNewArtist();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MainViewModel.ArtistImages)) return;
        RequestImage();
    }

    private void RequestImage()
    {
        var artist = ArtistName?.Trim();
        var vm = _viewModel ?? (Application.Current?.MainWindow?.DataContext as MainViewModel);
        if (vm == null) return;

        // Cancel previous request if artist changed
        if (!string.IsNullOrEmpty(artist) &&
            string.Equals(_pendingArtist, artist, StringComparison.OrdinalIgnoreCase))
        {
            return; // Request for this artist is already in flight
        }

        var version = Interlocked.Increment(ref _requestVersion);
        _requestCts?.Cancel();
        _requestCts?.Dispose();
        _requestCts = new CancellationTokenSource();

        if (DecideImageRequest(artist, true, IsLoaded, true) != ImageRequestDecision.Request)
        {
            Source = null;
            return;
        }

        // Check VM cache first (fast path)
        if (vm.ArtistImageCache.TryGetValue(artist, out var cached))
        {
            Source = cached;
            return;
        }

        // Check coordinator memory cache
        var coord = ImageLoadingCoordinator.Instance;
        if (coord != null)
        {
            var coordCached = coord.TryGetArtistImage(artist, isHighRes: false)
                           ?? coord.TryGetArtistImage(artist, isHighRes: true);
            if (coordCached != null)
            {
                var image = vm.ArtistImageCache.GetOrAdd(artist, coordCached);
                vm.TrimArtistImageCache(artist);
                Source = image;
                return;
            }
        }

        // Check if we have a cached path (triggers async load via coordinator)
        if (!vm.ArtistImages.TryGetValue(artist, out var path) || string.IsNullOrWhiteSpace(path))
        {
            Source = null;
            return;
        }

        // Request via centralized coordinator with proper priority
        _pendingArtist = artist;
        var requestCts = _requestCts.Token;

        // Use Background priority for initial load; Visible priority would be used if we knew it was in viewport
        var priority = ImagePriority.Background;

        _ = coord.LoadArtistImageAsync(artist, isHighRes: false, priority, requestCts)
            .ContinueWith(task =>
            {
                Dispatcher.BeginInvoke(() =>
                {
                    if (requestCts.IsCancellationRequested) return;
                    if (version != Volatile.Read(ref _requestVersion)) return;
                    if (!string.Equals(ArtistName?.Trim(), artist, StringComparison.OrdinalIgnoreCase)) return;

                    if (task.Status == TaskStatus.RanToCompletion && task.Result != null)
                    {
                        Source = task.Result;
                    }
                    else if (IsLoaded && string.Equals(ArtistName?.Trim(), artist, StringComparison.OrdinalIgnoreCase))
                    {
                        // Retry if still relevant
                        RequestImage();
                    }
                }, System.Windows.Threading.DispatcherPriority.Background);
            }, TaskScheduler.Default);
    }

    private string? _pendingArtist;

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scrollViewer) return scrollViewer;
        return null;
    }
}
