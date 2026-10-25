using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MusicPlayer.ViewModels;

namespace MusicPlayer.Views;

public partial class HomeContent : UserControl
{
    public HomeContent()
    {
        InitializeComponent();
    }

    private MainWindow? OwnerWindow => Window.GetWindow(this) as MainWindow;

    private void AllSongsCard_Click(object sender, MouseButtonEventArgs e) => NavigateTo("songs");
    private void FoldersCard_Click(object sender, MouseButtonEventArgs e) => NavigateTo("folders");
    private void FavoritesCard_Click(object sender, MouseButtonEventArgs e) => NavigateTo("favorites");
    private void RecentPlayCard_Click(object sender, MouseButtonEventArgs e) => NavigateTo("recent");
    private void RecentAddCard_Click(object sender, MouseButtonEventArgs e) => NavigateTo("recent_add");
    private void MostPlayCard_Click(object sender, MouseButtonEventArgs e) => NavigateTo("most_played");

    private void NavigateTo(string view) => OwnerWindow?.ViewModel?.NavigateCommand.Execute(view);

    private void NewPlaylist_Click(object sender, RoutedEventArgs e)
        => OwnerWindow?.ShowNewPlaylistOverlay();

    private void PlaylistItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlaylistViewModel playlist })
            OwnerWindow?.ViewModel?.NavigateToPlaylist(playlist.Id, playlist.Name);
    }

    private void PlaylistRename_Click(object sender, RoutedEventArgs e)
        => OwnerWindow?.PlaylistRename_Click(sender, e);

    private void PlaylistContextPlay_Click(object sender, RoutedEventArgs e)
        => OwnerWindow?.PlaylistContextPlay_Click(sender, e);

    private void PlaylistContextDelete_Click(object sender, RoutedEventArgs e)
        => OwnerWindow?.PlaylistContextDelete_Click(sender, e);
}
