using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using MusicPlayer.Converters;
using MusicPlayer.Models;

namespace MusicPlayer.Views;

public class QueueDragVisual : ContentControl
{
    private readonly TranslateTransform _translate;
    private readonly ScaleTransform _scale;

    public QueueDragVisual(MusicTrack track, double visualWidth = 380)
    {
        // Floor matches the caller: never exceed the list the card was
        // grabbed from (a wider card + shadow over a narrow panel reads as
        // the panel growing).
        Width = Math.Max(200, visualWidth);
        Height = 58;

        var border = new Border
        {
            CornerRadius = new CornerRadius(12),
            Background = new SolidColorBrush(Color.FromArgb(240, 24, 28, 36)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 6, 12, 6),
            Effect = new DropShadowEffect
            {
                BlurRadius = 16,
                ShadowDepth = 6,
                Color = Colors.Black,
                Opacity = 0.55,
                RenderingBias = RenderingBias.Performance
            }
        };

        // Cache the visual to a hardware GPU texture so dragging does not re-render pixel shaders every frame
        CacheMode = new BitmapCache { RenderAtScale = 1.0, SnapsToDevicePixels = true };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(20) }); // Grip
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) }); // Art
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // Title + Artist
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) }); // Duration

        // Grip icon
        var gripCanvas = new Canvas
        {
            Width = 10,
            Height = 16,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        var gripBrush = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255));
        for (int row = 0; row < 3; row++)
        {
            for (int col = 0; col < 2; col++)
            {
                var dot = new Ellipse
                {
                    Width = 2.5,
                    Height = 2.5,
                    Fill = gripBrush
                };
                Canvas.SetLeft(dot, col * 5.0);
                Canvas.SetTop(dot, row * 5.5);
                gripCanvas.Children.Add(dot);
            }
        }
        Grid.SetColumn(gripCanvas, 0);

        // Album Art
        var artBorder = new Border
        {
            Width = 42,
            Height = 42,
            CornerRadius = new CornerRadius(8),
            ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center
        };
        try
        {
            var img = new Image
            {
                Stretch = Stretch.UniformToFill,
                Source = (ImageSource)new AlbumArtConverter().Convert(track, typeof(ImageSource), null!, null!)
            };
            artBorder.Child = img;
        }
        catch { }
        Grid.SetColumn(artBorder, 1);

        // Title + Artist
        var textStack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(10, 0, 8, 0)
        };
        textStack.Children.Add(new TextBlock
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brushes.White,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Text = track.Title ?? "Unknown Title"
        });
        textStack.Children.Add(new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)),
            TextTrimming = TextTrimming.CharacterEllipsis,
            Margin = new Thickness(0, 2, 0, 0),
            Text = track.Artist ?? "Unknown Artist"
        });
        Grid.SetColumn(textStack, 2);

        // Duration
        var ts = TimeSpan.FromMilliseconds(track.Duration);
        string durStr = ts.TotalHours >= 1
            ? $"{(int)ts.TotalHours}:{ts.Minutes:D2}:{ts.Seconds:D2}"
            : $"{ts.Minutes}:{ts.Seconds:D2}";

        var durationText = new TextBlock
        {
            FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            Text = track.Duration > 0 ? durStr : ""
        };
        Grid.SetColumn(durationText, 3);

        grid.Children.Add(gripCanvas);
        grid.Children.Add(artBorder);
        grid.Children.Add(textStack);
        grid.Children.Add(durationText);
        border.Child = grid;

        Content = border;

        _translate = new TranslateTransform();
        _scale = new ScaleTransform(1.02, 1.02);
        var transformGroup = new TransformGroup();
        transformGroup.Children.Add(_scale);
        transformGroup.Children.Add(_translate);
        RenderTransform = transformGroup;

        IsHitTestVisible = false;
    }

    public void SetPosition(double x, double y)
    {
        _translate.X = x;
        _translate.Y = y;
    }

    public double CurrentX => _translate.X;
    public double CurrentY => _translate.Y;
    public TranslateTransform Translation => _translate;
}
