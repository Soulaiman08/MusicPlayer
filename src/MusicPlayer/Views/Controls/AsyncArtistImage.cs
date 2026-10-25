using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
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
        _scrollViewer = FindAncestorScrollViewer(this);
        if (_scrollViewer != null) _scrollViewer.ScrollChanged += OnScrollChanged;
        _viewModel = Application.Current?.MainWindow?.DataContext as MainViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        Dispatcher.BeginInvoke(RequestImage, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_scrollViewer != null) _scrollViewer.ScrollChanged -= OnScrollChanged;
        _scrollViewer = null;
        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
        Interlocked.Increment(ref _requestVersion);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ArtistImages) && IsInViewport()) RequestImage();
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (IsInViewport()) RequestImage();
    }

    private void RequestImage()
    {
        var artist = ArtistName?.Trim();
        var vm = _viewModel ?? (Application.Current?.MainWindow?.DataContext as MainViewModel);
        var version = Interlocked.Increment(ref _requestVersion);
        if (string.IsNullOrEmpty(artist) || vm == null || !IsLoaded)
        {
            Source = null;
            return;
        }

        if (vm.ArtistImageCache.TryGetValue(artist, out var cached))
        {
            Source = cached;
            return;
        }

        if (!vm.ArtistImages.TryGetValue(artist, out var path) || string.IsNullOrWhiteSpace(path))
        {
            Source = null;
            return;
        }

        // BalancedItemsPanel realizes the full grid. Defer disk reads and image decoding
        // for tiles outside the visible ScrollViewer viewport until scrolling reveals them.
        if (!IsInViewport()) return;

        _ = Task.Run(() => Decode(path)).ContinueWith(task =>
        {
            if (task.Status != TaskStatus.RanToCompletion || task.Result == null) return;
            var image = vm.ArtistImageCache.GetOrAdd(artist, task.Result);
            Dispatcher.BeginInvoke(() =>
            {
                if (IsLoaded && version == Volatile.Read(ref _requestVersion) &&
                    string.Equals(ArtistName?.Trim(), artist, StringComparison.OrdinalIgnoreCase))
                    Source = image;
            }, System.Windows.Threading.DispatcherPriority.Background);
        }, TaskScheduler.Default);
    }

    private bool IsInViewport()
    {
        var scrollViewer = _scrollViewer ?? FindAncestorScrollViewer(this);
        if (scrollViewer == null || !IsLoaded || RenderSize.Width <= 0 || RenderSize.Height <= 0)
            return false;

        try
        {
            var bounds = TransformToAncestor(scrollViewer).TransformBounds(new Rect(new Point(0, 0), RenderSize));
            var viewport = new Rect(0, 0, scrollViewer.ActualWidth, scrollViewer.ActualHeight);
            return bounds.IntersectsWith(viewport);
        }
        catch (InvalidOperationException)
        {
            // The visual tree may be reparenting during virtualization or tab changes.
            return false;
        }
    }

    private static ScrollViewer? FindAncestorScrollViewer(DependencyObject element)
    {
        for (var parent = VisualTreeHelper.GetParent(element); parent != null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is ScrollViewer scrollViewer) return scrollViewer;
        return null;
    }

    private static BitmapImage? Decode(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = 180;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (System.IO.IOException) { return null; }
        catch (UriFormatException) { return null; }
        catch (NotSupportedException) { return null; }
    }
}
