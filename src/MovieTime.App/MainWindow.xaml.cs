using System;
using Microsoft.UI.Xaml;
using MovieTime.App.ViewModels;

namespace MovieTime.App;

/// <summary>
/// Main window hosting the cinematic Apple TV-inspired Home, Movies Library, Settings, and Movie Details experiences.
/// Coordinates top-level navigation between views.
/// </summary>
public sealed partial class MainWindow : Window
{
    public HomeViewModel HomeViewModel { get; }
    public MoviesViewModel MoviesViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public MovieDetailsViewModel MovieDetailsViewModel { get; }

    /// <summary>
    /// Legacy alias for backward compatibility.
    /// </summary>
    public HomeViewModel ViewModel => HomeViewModel;

    public MainWindow(
        HomeViewModel homeViewModel,
        MoviesViewModel moviesViewModel,
        SettingsViewModel settingsViewModel,
        MovieDetailsViewModel movieDetailsViewModel)
    {
        HomeViewModel = homeViewModel ?? throw new ArgumentNullException(nameof(homeViewModel));
        MoviesViewModel = moviesViewModel ?? throw new ArgumentNullException(nameof(moviesViewModel));
        SettingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));
        MovieDetailsViewModel = movieDetailsViewModel ?? throw new ArgumentNullException(nameof(movieDetailsViewModel));

        this.InitializeComponent();

        // Extend content into title bar for seamless edge-to-edge layout
        this.ExtendsContentIntoTitleBar = true;
        Title = "Movie Time";

        try
        {
            this.AppWindow?.Resize(new Windows.Graphics.SizeInt32(1280, 800));
        }
        catch
        {
            // Best effort window sizing
        }

        // Subscribe to navigation events
        HomeViewModel.NavigationRequested += OnNavigationRequested;
        MoviesViewModel.NavigationRequested += OnNavigationRequested;
        SettingsViewModel.NavigationRequested += OnNavigationRequested;
        MovieDetailsViewModel.NavigationRequested += OnNavigationRequested;

        _ = HomeViewModel.InitializeAsync();
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

        if (string.Equals(target, "Movies", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(target, "Library", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToMovies();
        }
        else if (string.Equals(target, "Home", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToHome();
        }
        else if (string.Equals(target, "Settings", StringComparison.OrdinalIgnoreCase))
        {
            NavigateToSettings();
        }
    }

    public void NavigateToMovies()
    {
        MainHomeView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Visible;
        _ = MoviesViewModel.InitializeAsync();
    }

    public void NavigateToHome()
    {
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainHomeView.Visibility = Visibility.Visible;
        _ = HomeViewModel.InitializeAsync();
    }

    public void NavigateToSettings()
    {
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Visible;
        _ = SettingsViewModel.InitializeAsync();
    }

    public void NavigateToMovieDetails(int? movieId)
    {
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainMovieDetailsView.Visibility = Visibility.Visible;
        _ = MovieDetailsViewModel.LoadMovieAsync(movieId);
    }
}
