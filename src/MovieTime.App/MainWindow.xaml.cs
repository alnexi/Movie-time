using System;
using Microsoft.UI.Xaml;
using MovieTime.App.ViewModels;

namespace MovieTime.App;

/// <summary>
/// Main window hosting the cinematic Apple TV-inspired Home, Movies Library, and Settings experiences.
/// Coordinates top-level navigation between HomeView, MoviesView, and SettingsView.
/// </summary>
public sealed partial class MainWindow : Window
{
    public HomeViewModel HomeViewModel { get; }
    public MoviesViewModel MoviesViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }

    /// <summary>
    /// Legacy alias for backward compatibility.
    /// </summary>
    public HomeViewModel ViewModel => HomeViewModel;

    public MainWindow(
        HomeViewModel homeViewModel,
        MoviesViewModel moviesViewModel,
        SettingsViewModel settingsViewModel)
    {
        HomeViewModel = homeViewModel ?? throw new ArgumentNullException(nameof(homeViewModel));
        MoviesViewModel = moviesViewModel ?? throw new ArgumentNullException(nameof(moviesViewModel));
        SettingsViewModel = settingsViewModel ?? throw new ArgumentNullException(nameof(settingsViewModel));

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

        _ = HomeViewModel.InitializeAsync();
    }

    private void OnNavigationRequested(string target)
    {
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
        MainMoviesView.Visibility = Visibility.Visible;
        _ = MoviesViewModel.InitializeAsync();
    }

    public void NavigateToHome()
    {
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Collapsed;
        MainHomeView.Visibility = Visibility.Visible;
        _ = HomeViewModel.InitializeAsync();
    }

    public void NavigateToSettings()
    {
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainSettingsView.Visibility = Visibility.Visible;
        _ = SettingsViewModel.InitializeAsync();
    }
}
