using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Views.Controls;

/// <summary>Loads artist thumbnails away from the dispatcher and caches frozen bitmaps.</summary>
public sealed class AsyncArtistImage : Image
{
    public static readonly DependencyProperty ArtistNameProperty = DependencyProperty.Register(
        nameof(ArtistName), typeof(string), typeof(AsyncArtistImage),
        new PropertyMetadata(string.Empty, OnArtistNameChanged));

    private static readonly ConcurrentDictionary<string, byte> _inFlightLoads = new(StringComparer.OrdinalIgnoreCase);
    private MainViewModel? _viewModel;
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
        _viewModel = Application.Current?.MainWindow?.DataContext as MainViewModel;
        if (_viewModel != null) _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        RequestImage();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel != null) _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        _viewModel = null;
        Interlocked.Increment(ref _requestVersion);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.ArtistImages) ||
            e.PropertyName == nameof(MainViewModel.ArtistImageCache))
        {
            RequestImage();
        }
    }

    private void RequestImage()
    {
        var artist = ArtistName?.Trim();
        if (string.IsNullOrEmpty(artist))
        {
            Source = null;
            return;
        }

        var vm = _viewModel ?? (Application.Current?.MainWindow?.DataContext as MainViewModel);
        if (vm == null) return;

        // 1. Fast in-memory cache hit (preloaded at startup)
        if (vm.ArtistImageCache.TryGetValue(artist, out var cached) && cached != null)
        {
            Source = cached;
            return;
        }

        // 2. If path is available on disk, decode asynchronously once
        if (vm.ArtistImages.TryGetValue(artist, out var path) && !string.IsNullOrWhiteSpace(path))
        {
            if (!_inFlightLoads.TryAdd(artist, 0)) return;

            int version = Interlocked.Increment(ref _requestVersion);
            Task.Run(() =>
            {
                try
                {
                    if (File.Exists(path))
                    {
                        var image = Decode(path);
                        if (image != null)
                        {
                            vm.ArtistImageCache[artist] = image;
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                if (version == Volatile.Read(ref _requestVersion) &&
                                    string.Equals(ArtistName?.Trim(), artist, StringComparison.OrdinalIgnoreCase))
                                {
                                    Source = image;
                                }
                            }), System.Windows.Threading.DispatcherPriority.Background);
                        }
                    }
                }
                finally
                {
                    _inFlightLoads.TryRemove(artist, out _);
                }
            });
            return;
        }

        Source = null;
    }

    private static BitmapImage? Decode(string path)
    {
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.UriSource = new Uri(path, UriKind.Absolute);
            image.DecodePixelWidth = 240;
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }
}
