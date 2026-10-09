namespace MusicPlayer.Services;

/// <summary>
/// DEPRECATED — UI navigation source of truth is MainViewModel._viewHistory + CurrentView.
/// Kept for DI compatibility and potential future consolidation. Do not use for new navigation; use MainViewModel.Navigate/GoBack/CloseTopmostOverlay.
/// </summary>
public class NavigationService
{
    private readonly Stack<string> _backStack = new();
    private string _currentPage = "songs";

    public event EventHandler<string>? Navigated;

    public string CurrentPage => _currentPage;

    public void NavigateTo(string page)
    {
        if (_currentPage != page)
        {
            _backStack.Push(_currentPage);
            _currentPage = page;
            Navigated?.Invoke(this, page);
        }
    }

    public void GoBack()
    {
        if (_backStack.Count > 0)
        {
            _currentPage = _backStack.Pop();
            Navigated?.Invoke(this, _currentPage);
        }
    }

    public void ClearHistory()
    {
        _backStack.Clear();
        _currentPage = "songs";
    }
}
