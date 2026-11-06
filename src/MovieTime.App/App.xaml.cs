using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using MovieTime.App.ViewModels;
using MovieTime.Core;
using MovieTime.Core.Extensions;
using Serilog;

namespace MovieTime.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// Acts as the application's Composition Root for Dependency Injection and local logging.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private readonly Microsoft.Extensions.Logging.ILogger<App> _logger;

    /// <summary>
    /// Gets the current <see cref="App"/> instance in use.
    /// </summary>
    public static new App Current => (App)Application.Current;

    /// <summary>
    /// Gets the application service provider (composition root).
    /// </summary>
    public IServiceProvider Services { get; }

    public App()
    {
        // 1. Initialize DI and Logging immediately
        Services = ConfigureServices();
        _logger = Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<App>>();

        // 2. Global unhandled exception hook
        this.UnhandledException += OnUnhandledException;

        _logger.LogInformation("Movie Time starting up. Version: {Version}, OS: {OS}, Arch: {Arch}",
            AppConstants.ApplicationVersion,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture);

        // 3. Initialize WinUI XAML components
        this.InitializeComponent();
    }

    /// <summary>
    /// Configures the Dependency Injection container.
    /// </summary>
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // 1. Core Services (Platform-agnostic)
        services.AddMovieTimeCore();

        // 2. Logging Infrastructure (Local-only Serilog file sink)
#if DEBUG
        services.AddMovieTimeLogging(isDevelopment: true);
#else
        services.AddMovieTimeLogging(isDevelopment: false);
#endif

        // 3. Presentation Services & Windows
        services.AddTransient<HomeViewModel>();
        services.AddTransient<MoviesViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _logger.LogInformation("Application launched. Initializing database and activating MainWindow.");

        try
        {
            var databaseService = Services.GetRequiredService<MovieTime.Core.Database.IDatabaseService>();
            await databaseService.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize local SQLite database during application launch.");
        }

        // Resolve MainWindow from DI container
        _window = Services.GetRequiredService<MainWindow>();
        _window.Closed += OnWindowClosed;
        _window.Activate();
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _logger.LogInformation("MainWindow closed. Finalizing application shutdown.");
        try
        {
            var databaseService = Services.GetService<MovieTime.Core.Database.IDatabaseService>();
            if (databaseService != null)
            {
                await databaseService.DisposeAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing database service on shutdown.");
        }

        Log.CloseAndFlush();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger.LogCritical(e.Exception, "Unhandled application exception occurred: {Message}", e.Message);
        Log.CloseAndFlush();
    }
}
