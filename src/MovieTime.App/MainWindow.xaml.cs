using System;
using Microsoft.UI.Xaml;
using MovieTime.App.ViewModels;

namespace MovieTime.App;

/// <summary>
/// Main window hosting the cinematic Apple TV-inspired Home experience.
/// </summary>
public sealed partial class MainWindow : Window
{
    public HomeViewModel ViewModel { get; }

    public MainWindow(HomeViewModel viewModel)
    {
        ViewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
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

        _ = ViewModel.InitializeAsync();
    }
}
