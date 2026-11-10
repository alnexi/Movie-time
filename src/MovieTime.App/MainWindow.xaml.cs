using System;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using MovieTime.App.ViewModels;

namespace MovieTime.App;

/// <summary>
/// Main window hosting the cinematic Home, Movies Library, Settings, Movie Details, and Player experiences.
/// Coordinates top-level navigation and fullscreen transitions between views.
/// </summary>
public sealed partial class MainWindow : Window
{
    public HomeViewModel HomeViewModel { get; }
    public MoviesViewModel MoviesViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public MovieDetailsViewModel MovieDetailsViewModel { get; }
    public TvShowsViewModel TvShowsViewModel { get; }
    public SeriesDetailsViewModel SeriesDetailsViewModel { get; }
    public PlaylistsViewModel PlaylistsViewModel { get; }
    public PlayerViewModel PlayerViewModel { get; }

    /// <summary>
    /// Legacy alias for backward compatibility.
    /// </summary>
    public HomeViewModel ViewModel => HomeViewModel;

    public MainWindow(
        HomeViewModel homeViewModel,
        MoviesViewModel moviesViewModel,
        SettingsViewModel settingsViewModel,
        MovieDetailsViewModel movieDetailsViewModel,
        TvShowsViewModel tvShowsViewModel,
        SeriesDetailsViewModel seriesDetailsViewModel,
        PlaylistsViewModel playlistsViewModel,
        PlayerViewModel playerViewModel,
        MovieTime.App.Common.IDispatcherService? dispatcherService = null)
    {
        HomeViewModel = homeViewModel ?? throw new ArgumentNullException(nameof(homeViewModel));
        MoviesViewModel = moviesViewModel ?? throw new ArgumentNullException(nameof(moviesViewModel));
        SettingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        MovieDetailsViewModel = movieDetailsViewModel ?? throw new ArgumentNullException(nameof(movieDetailsViewModel));
        TvShowsViewModel = tvShowsViewModel ?? throw new ArgumentNullException(nameof(tvShowsViewModel));
        SeriesDetailsViewModel = seriesDetailsViewModel ?? throw new ArgumentNullException(nameof(seriesDetailsViewModel));
        PlaylistsViewModel = playlistsViewModel ?? throw new ArgumentNullException(nameof(playlistsViewModel));
        PlayerViewModel = playerViewModel ?? throw new ArgumentNullException(nameof(playerViewModel));

        this.InitializeComponent();

        if (dispatcherService is MovieTime.App.Services.WinUiDispatcherService winUiDispatcher)
        {
            winUiDispatcher.SetDispatcherQueue(this.DispatcherQueue);
        }

        // Extend content into title bar for seamless edge-to-edge layout
        this.ExtendsContentIntoTitleBar = true;
        Title = MovieTime.Core.AppConstants.ApplicationName;

        try
        {
            this.AppWindow?.Resize(new Windows.Graphics.SizeInt32(1280, 800));
            var iconPath = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
            if (System.IO.File.Exists(iconPath))
            {
                this.AppWindow?.SetIcon(iconPath);
            }
        }
        catch
        {
            // Best effort window sizing and icon
        }

        // Subscribe to navigation and playback events
        HomeViewModel.NavigationRequested += OnNavigationRequested;
        HomeViewModel.PlaybackRequested += OnHomePlaybackRequested;
        HomeViewModel.MediaPlaybackRequested += OnHomeMediaPlaybackRequested;
        MoviesViewModel.NavigationRequested += OnNavigationRequested;
        MoviesViewModel.PlaybackRequested += (filePath, movieId) => NavigateToPlayer(filePath, movieId, null);
        TvShowsViewModel.NavigationRequested += OnNavigationRequested;
        SettingsViewModel.NavigationRequested += OnNavigationRequested;
        MovieDetailsViewModel.NavigationRequested += OnNavigationRequested;
        MovieDetailsViewModel.PlaybackRequested += OnMovieDetailsPlaybackRequested;
        SeriesDetailsViewModel.NavigationRequested += OnNavigationRequested;
        SeriesDetailsViewModel.PlaybackRequested += OnSeriesPlaybackRequested;
        PlaylistsViewModel.NavigationRequested += OnNavigationRequested;
        PlaylistsViewModel.PlaybackRequested += OnPlaylistPlaybackRequested;
        PlayerViewModel.NavigationRequested += OnNavigationRequested;
        PlayerViewModel.FullscreenToggled += OnFullscreenToggled;

        this.Activated += (s, e) => App.Logger?.LogInformation("MainWindow.Activated: WindowActivationState={State}", e.WindowActivationState);
        this.VisibilityChanged += (s, e) => App.Logger?.LogInformation("MainWindow.VisibilityChanged: Visible={Visible}", e.Visible);
        this.Closed += (s, e) => App.Logger?.LogInformation("MainWindow.Closed event fired on MainWindow.");

        _ = HomeViewModel.InitializeAsync();
    }

    private void OnHomePlaybackRequested(string filePath, int? movieId)
    {
        NavigateToPlayer(filePath, movieId, null);
    }

    private void OnHomeMediaPlaybackRequested(string filePath, int? movieId, int? episodeId)
    {
        NavigateToPlayer(filePath, movieId, episodeId);
    }

    private void OnMovieDetailsPlaybackRequested(string filePath)
    {
        NavigateToPlayer(filePath, MovieDetailsViewModel.MovieId, null);
    }

    private void OnSeriesPlaybackRequested(string filePath, int episodeId)
    {
        NavigateToPlayer(filePath, null, episodeId);
    }

    private void OnPlaylistPlaybackRequested(string filePath, int? movieId, System.Collections.Generic.List<(string FilePath, int? MovieId, string Title)> queue, int startIndex)
    {
        NavigateToPlayerWithPlaylist(filePath, movieId, queue, startIndex);
    }

    private void OnFullscreenToggled(bool isFullscreen)
    {
        try
        {
            if (isFullscreen)
            {
                this.AppWindow?.SetPresenter(AppWindowPresenterKind.FullScreen);
            }
            else
            {
                this.AppWindow?.SetPresenter(AppWindowPresenterKind.Default);
            }
        }
        catch
        {
            // Best effort presenter change
        }
    }

    private void OnNavigationRequested(string target)
    {
        if (target.StartsWith("MovieDetails:", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = target.Substring("MovieDetails:".Length);
            if (int.TryParse(idPart, out int movieId))
            {
                NavigateToMovieDetails(movieId);
                return;
            }
        }
        else if (string.Equals(target, "MovieDetails", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToMovieDetails(null);
            return;
        }

        if (target.StartsWith("SeriesDetails:", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = target.Substring("SeriesDetails:".Length);
            if (int.TryParse(idPart, out int seriesId))
            {
                NavigateToSeriesDetails(seriesId);
                return;
            }
        }
        else if (target.StartsWith("TVShows:", StringComparison.OrdinalIgnoreCase))
        {
            var idPart = target.Substring("TVShows:".Length);
            if (int.TryParse(idPart, out int seriesId))
            {
                NavigateToSeriesDetails(seriesId);
                return;
            }
        }
        else if (string.Equals(target, "SeriesDetails", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToSeriesDetails(null);
            return;
        }

        if (string.Equals(target, "Movies", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, "Library", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToMovies();
        }
        else if (string.Equals(target, "TV Shows", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(target, "TVShows", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToTvShows();
        }
        else if (string.Equals(target, "Home", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToHome();
        }
        else if (string.Equals(target, "Settings", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToSettings();
        }
        else if (string.Equals(target, "Playlists", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToPlaylists();
        }
    }

    public void NavigateToMovies()
    {
        ExitPlayerMode();
        MainHomeView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainTvShowsView.Visibility = Visibility.Collapsed;
        MainSeriesDetailsView.Visibility = Visibility.Collapsed;
        MainPlaylistsView.Visibility = Visibility.Collapsed;
        MainPlayerView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Visible;
        _ = MoviesViewModel.InitializeAsync();
    }

    public void NavigateToTvShows()
    {
        ExitPlayerMode();
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainSeriesDetailsView.Visibility = Visibility.Collapsed;
        MainPlaylistsView.Visibility = Visibility.Collapsed;
        MainPlayerView.Visibility = Visibility.Collapsed;
        MainTvShowsView.Visibility = Visibility.Visible;
        _ = TvShowsViewModel.InitializeAsync();
    }

    public void NavigateToHome()
    {
        ExitPlayerMode();
        bool wasAlreadyHome = MainHomeView.Visibility == Visibility.Visible;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainTvShowsView.Visibility = Visibility.Collapsed;
        MainSeriesDetailsView.Visibility = Visibility.Collapsed;
        MainPlaylistsView.Visibility = Visibility.Collapsed;
        MainPlayerView.Visibility = Visibility.Collapsed;
        MainHomeView.Visibility = Visibility.Visible;

        HomeViewModel.ActiveCategory = "Home";
        HomeViewModel.IsSidebarOpen = false;

        if (wasAlreadyHome)
        {
            MainHomeView.ScrollToTop();
        }
        else
        {
            _ = HomeViewModel.InitializeAsync();
        }
    }

    public void NavigateToSettings()
    {
        ExitPlayerMode();
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainTvShowsView.Visibility = Visibility.Collapsed;
        MainSeriesDetailsView.Visibility = Visibility.Collapsed;
        MainPlaylistsView.Visibility = Visibility.Collapsed;
        MainPlayerView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Visible;
        _ = SettingsViewModel.InitializeAsync();
    }

    public void NavigateToPlaylists()
    {
        ExitPlayerMode();
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainTvShowsView.Visibility = Visibility.Collapsed;
        MainSeriesDetailsView.Visibility = Visibility.Collapsed;
        MainPlayerView.Visibility = Visibility.Collapsed;
        MainPlaylistsView.Visibility = Visibility.Visible;
        _ = PlaylistsViewModel.InitializeAsync();
    }

    public void NavigateToMovieDetails(int? movieId)
    {
        ExitPlayerMode();
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainTvShowsView.Visibility = Visibility.Collapsed;
        MainSeriesDetailsView.Visibility = Visibility.Collapsed;
        MainPlaylistsView.Visibility = Visibility.Collapsed;
        MainPlayerView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Visible;
        _ = MovieDetailsViewModel.LoadMovieAsync(movieId);
    }

    public void NavigateToSeriesDetails(int? seriesId)
    {
        ExitPlayerMode();
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainTvShowsView.Visibility = Visibility.Collapsed;
        MainPlaylistsView.Visibility = Visibility.Collapsed;
        MainPlayerView.Visibility = Visibility.Collapsed;
        MainSeriesDetailsView.Visibility = Visibility.Visible;
        _ = SeriesDetailsViewModel.LoadSeriesAsync(seriesId);
    }

    public void NavigateToPlayer(string filePath, int? movieId = null, int? episodeId = null)
    {
        App.Logger?.LogInformation("NavigateToPlayer start: Collapsing views...");
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainTvShowsView.Visibility = Visibility.Collapsed;
        MainSeriesDetailsView.Visibility = Visibility.Collapsed;
        MainPlaylistsView.Visibility = Visibility.Collapsed;

        App.Logger?.LogInformation("NavigateToPlayer: Setting MainPlayerView.Visibility = Visible...");
        MainPlayerView.Visibility = Visibility.Visible;

        App.Logger?.LogInformation("NavigateToPlayer: Calling EnsureVideoSurfaceAttached...");
        MainPlayerView.EnsureVideoSurfaceAttached();

        App.Logger?.LogInformation("NavigateToPlayer: Calling PlayerViewModel.InitializeAsync with episodeId={EpId}...", episodeId);
        _ = PlayerViewModel.InitializeAsync(filePath, movieId, episodeId);
        App.Logger?.LogInformation("NavigateToPlayer: Finished dispatching InitializeAsync.");
    }

    public void NavigateToPlayerWithPlaylist(string filePath, int? movieId, System.Collections.Generic.List<(string FilePath, int? MovieId, string Title)> queue, int startIndex)
    {
        ExitPlayerMode();
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainTvShowsView.Visibility = Visibility.Collapsed;
        MainSeriesDetailsView.Visibility = Visibility.Collapsed;
        MainPlaylistsView.Visibility = Visibility.Collapsed;

        MainPlayerView.Visibility = Visibility.Visible;
        MainPlayerView.EnsureVideoSurfaceAttached();

        _ = PlayerViewModel.InitializeWithPlaylistAsync(filePath, movieId, queue, startIndex);
    }

    private void ExitPlayerMode()
    {
        if (MainPlayerView.Visibility == Visibility.Visible)
        {
            _ = PlayerViewModel.StopPlaybackAsync();
            MainPlayerView.Visibility = Visibility.Collapsed;
        }

        if (PlayerViewModel.IsFullscreen)
        {
            OnFullscreenToggled(false);
        }
    }
}
