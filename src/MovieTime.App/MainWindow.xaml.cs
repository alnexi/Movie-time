using System;
using Microsoft.UI.Xaml;
using MovieTime.App.ViewModels;

namespace MovieTime.App;

/// <summary>
/// Main window hosting the cinematic Apple TV-inspired Home and Movies Library experiences.
/// Coordinates top-level navigation between HomeView and MoviesView.
/// </summary>
public sealed partial class MainWindow : Window
{
    public HomeViewModel HomeViewModel { get; }
    public MoviesViewModel MoviesViewModel { get; }

    /// <summary>
    /// Legacy alias for backward compatibility.
    /// </summary>
    public HomeViewModel ViewModel => HomeViewModel;

    public MainWindow(HomeViewModel homeViewModel, MoviesViewModel moviesViewModel)
    {
        HomeViewModel = homeViewModel ?? throw new ArgumentNullException(nameof(homeViewModel));
        MoviesViewModel = moviesViewModel ?? throw new ArgumentNullException(nameof(moviesViewModel));

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
    }

    public void NavigateToMovies()
    {
        MainHomeView.Visibility = Visibility.Collapsed;
        MainMoviesView.Visibility = Visibility.Visible;
        _ = MoviesViewModel.InitializeAsync();
    }

    public void NavigateToHome()
    {
        MainMoviesView.Visibility = Visibility.Collapsed;
        MainHomeView.Visibility = Visibility.Visible;
        _ = HomeViewModel.InitializeAsync();
    }
}
