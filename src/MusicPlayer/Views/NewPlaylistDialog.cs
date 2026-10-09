using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MusicPlayer.Views;

public partial class NewPlaylistDialog : Window
{
    public string PlaylistName { get; private set; } = string.Empty;
    private readonly TextBox _nameBox;

    public NewPlaylistDialog() : this("My Playlist", "Create New Playlist", "Create") { }

    public NewPlaylistDialog(string initialName, string title, string buttonText)
    {
        Title = title;
        try { Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app_logo.png")); } catch { }
        Width = 360;
        Height = 180;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        WindowStyle = WindowStyle.ToolWindow;
        ResizeMode = ResizeMode.NoResize;
        Background = System.Windows.Media.Brushes.Transparent;
        AllowsTransparency = false;

        var root = new Border { Background = System.Windows.Media.Brushes.Transparent, Padding = new Thickness(0) };
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var header = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1E, 0x21, 0x28)),
            Padding = new Thickness(20, 14, 20, 14)
        };
        var headerText = new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White };
        header.Child = headerText;
        Grid.SetRow(header, 0);

        var inputBorder = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x15, 0x18, 0x1F)),
            Padding = new Thickness(20, 12, 20, 8)
        };
        _nameBox = new TextBox
        {
            Text = initialName,
            FontSize = 14,
            Padding = new Thickness(8, 6, 8, 6),
            MaxLength = 80
        };
        _nameBox.Loaded += (_, _) => { _nameBox.SelectAll(); _nameBox.Focus(); };
        _nameBox.KeyDown += (_, e) => { if (e.Key == Key.Enter) Confirm(_nameBox.Text); if (e.Key == Key.Escape) DialogResult = false; };
        inputBorder.Child = _nameBox;
        Grid.SetRow(inputBorder, 1);

        var btnBorder = new Border
        {
            Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0x15, 0x18, 0x1F)),
            Padding = new Thickness(20, 8, 20, 16)
        };
        var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancelBtn = new Button { Content = "Cancel", Width = 80, Height = 32, Margin = new Thickness(0, 0, 8, 0) };
        cancelBtn.Click += (_, _) => DialogResult = false;
        var okBtn = new Button { Content = buttonText, Width = 80, Height = 32, IsDefault = true };
        okBtn.Click += (_, _) => Confirm(_nameBox.Text);
        btnPanel.Children.Add(cancelBtn);
        btnPanel.Children.Add(okBtn);
        btnBorder.Child = btnPanel;
        Grid.SetRow(btnBorder, 2);

        grid.Children.Add(header);
        grid.Children.Add(inputBorder);
        grid.Children.Add(btnBorder);
        root.Child = grid;
        Content = root;
    }

    private void Confirm(string name)
    {
        PlaylistName = name.Trim();
        if (!string.IsNullOrWhiteSpace(PlaylistName))
            DialogResult = true;
    }
}
