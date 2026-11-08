using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using MovieTime.App.Common;
using MovieTime.App.Services;
using MovieTime.App.ViewModels;
using MovieTime.Core;
using MovieTime.Core.Extensions;
using MovieTime.Core.Playback;
using MovieTime.Core.Services;
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
    /// Static logger for early diagnostic tracking.
    /// </summary>
    public static Microsoft.Extensions.Logging.ILogger? Logger { get; private set; }

    /// <summary>
    /// Gets the current <see cref="App"/> instance in use.
    /// </summary>
    public static new App Current => (App)Application.Current;

    /// <summary>
    /// Gets the primary MainWindow instance.
    /// </summary>
    public Window? MainWindow => _window;

    /// <summary>
    /// Gets the application service provider (composition root).
    /// </summary>
    public IServiceProvider Services { get; }

    public App()
    {
        // 0. Initialize native LibVLC runtime binaries before XAML tree composition
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
        }
        catch
        {
            // Fallback handled by player service
        }

        // 1. Initialize DI and Logging immediately
        Services = ConfigureServices();
        _logger = Services.GetRequiredService<Microsoft.Extensions.Logging.ILogger<App>>();
        Logger = _logger;

        // 2. Global unhandled exception hooks
        this.UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (sender, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            _logger.LogCritical(ex, "FATAL AppDomain UnhandledException: {Message}\n{StackTrace}", ex?.Message, ex?.ToString());
            Log.CloseAndFlush();
        };

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
        services.AddSingleton<IDispatcherService, WinUiDispatcherService>();
        services.AddSingleton<IFilePickerService, WinUiFilePickerService>();
        services.AddSingleton<IVlcPlayerService, VlcPlayerService>();
        services.AddSingleton<MovieTime.Core.Artwork.IImageProcessingService, WicImageProcessingService>();
        services.AddSingleton<MovieTime.Core.Artwork.IVideoFrameExtractor, WindowsVideoFrameExtractor>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<MoviesViewModel>();
        services.AddTransient<MovieDetailsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PlayerViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _logger.LogInformation("Application launched. Initializing database and activating MainWindow. CommandLine: {Cmd}", Environment.CommandLine);

        try
        {
            var databaseService = Services.GetRequiredService<MovieTime.Core.Database.IDatabaseService>();
            await databaseService.InitializeAsync();
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Failed to initialize local SQLite database during application launch.");
        }

        try
        {
            _logger.LogInformation("Attempting to resolve MainWindow from DI...");
            _window = Services.GetRequiredService<MainWindow>();
            _logger.LogInformation("MainWindow resolved successfully. Activating...");
            _window.Closed += OnWindowClosed;
            _window.Activate();
            _logger.LogInformation("MainWindow activated successfully.");

            // Initiate non-blocking background artwork extraction for movies missing posters/backdrops
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500); // Allow window layout and visual tree to settle
                    var pipeline = Services.GetService<MovieTime.Core.Artwork.IArtworkPipelineService>();
                    if (pipeline != null)
                    {
                        await pipeline.ProcessPendingLibraryArtworkAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Background library artwork processing encountered a non-fatal error.");
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "FATAL: Failed to resolve or activate MainWindow: {Message}\n{StackTrace}", ex.Message, ex.ToString());
            Log.CloseAndFlush();
            throw;
        }
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
        _logger.LogCritical(e.Exception, "Unhandled application exception occurred: {Message}\n{StackTrace}", e.Message, e.Exception?.ToString());
        Log.CloseAndFlush();
    }
}
