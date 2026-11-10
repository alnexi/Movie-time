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
using MovieTime.Core.Theme;
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

        _logger.LogInformation("River starting up. Version: {Version}, OS: {OS}, Arch: {Arch}",
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
        services.AddSingleton<MovieTime.Core.Artwork.IHoverPreviewService, MovieTime.Core.Artwork.HoverPreviewService>();
        services.AddSingleton<MovieTime.Core.Theme.IThemePaletteApplier, WinUiThemePaletteApplier>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<MoviesViewModel>();
        services.AddTransient<MovieDetailsViewModel>();
        services.AddTransient<TvShowsViewModel>();
        services.AddTransient<SeriesDetailsViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddTransient<PlaylistsViewModel>();
        services.AddTransient<PlayerViewModel>();
        services.AddTransient<MainWindow>();

        return services.BuildServiceProvider();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _logger.LogInformation("Application launched. Initializing database and activating MainWindow. CommandLine: {Cmd}", Environment.CommandLine);

        // 1. Single-Instance Coordination: Redirect if another instance is already running
        try
        {
            var mainInstance = Microsoft.Windows.AppLifecycle.AppInstance.FindOrRegisterForKey("MovieTime_App_SingleInstance");
            if (!mainInstance.IsCurrent)
            {
                _logger.LogInformation("Another instance of River is already running. Redirecting activation and exiting...");
                var currentArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                await mainInstance.RedirectActivationToAsync(currentArgs);
                System.Diagnostics.Process.GetCurrentProcess().Kill();
                return;
            }

            mainInstance.Activated += (s, e) =>
            {
                if (_window is MainWindow mw)
                {
                    mw.DispatcherQueue.TryEnqueue(() =>
                    {
                        mw.Activate();
                        HandleExternalActivation(mw, e);
                    });
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Single-instance coordination encountered a non-fatal error.");
        }

        // 2. Safely register Movie Time in Windows Explorer Open With menu
        _ = Task.Run(() => WindowsOpenWithRegistrar.RegisterOpenWith(_logger));

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

            // Initialize and attach centralized ThemeManager before activation to prevent flash of wrong theme
            try
            {
                var themeManager = Services.GetRequiredService<IThemeManagerService>();
                await themeManager.InitializeAsync();
                if (_window.Content is FrameworkElement root)
                {
                    themeManager.AttachRootElement(root);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize ThemeManager during launch.");
            }

            _logger.LogInformation("MainWindow resolved successfully. Activating...");
            _window.Closed += OnWindowClosed;
            _window.Activate();
            _logger.LogInformation("MainWindow activated successfully.");

            // 3. Handle external file activation (Open With or command line file argument)
            if (_window is MainWindow mainWindow)
            {
                try
                {
                    var currentArgs = Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().GetActivatedEventArgs();
                    HandleExternalActivation(mainWindow, currentArgs);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Error checking initial activation arguments.");
                }
            }

            // 4. Start automatic library synchronization service (real-time FileSystemWatcher + periodic reconciliation)
            try
            {
                var autoSync = Services.GetService<MovieTime.Core.Library.ILibraryAutoSyncService>();
                if (autoSync != null)
                {
                    _ = autoSync.StartAsync();
                    _logger.LogInformation("LibraryAutoSyncService initialized and running.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to initialize LibraryAutoSyncService during launch.");
            }

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

    private void HandleExternalActivation(MainWindow mainWindow, Microsoft.Windows.AppLifecycle.AppActivationArguments? activatedArgs)
    {
        try
        {
            var validFiles = CommandLineActivationHelper.ExtractVideoFiles(activatedArgs);
            if (validFiles.Count == 0)
            {
                return;
            }

            if (validFiles.Count == 1)
            {
                _logger.LogInformation("Routing external video file directly to Player: {FilePath}", validFiles[0]);
                mainWindow.NavigateToPlayer(validFiles[0], null, null);
            }
            else
            {
                _logger.LogInformation("Routing {Count} external video files to Player as playlist.", validFiles.Count);
                var queue = validFiles
                    .Select(f => (FilePath: f, MovieId: (int?)null, Title: Path.GetFileNameWithoutExtension(f)))
                    .ToList();
                mainWindow.NavigateToPlayerWithPlaylist(validFiles[0], null, queue, 0);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to route external file activation to player.");
        }
    }

    private async void OnWindowClosed(object sender, WindowEventArgs args)
    {
        _logger.LogInformation("MainWindow closed. Finalizing application shutdown.");

        try
        {
            var playerViewModel = Services.GetService<MovieTime.App.ViewModels.PlayerViewModel>();
            if (playerViewModel != null)
            {
                await playerViewModel.StopPlaybackAsync();
                playerViewModel.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping and disposing PlayerViewModel on shutdown.");
        }

        try
        {
            var vlcPlayer = Services.GetService<IVlcPlayerService>();
            if (vlcPlayer != null)
            {
                vlcPlayer.Stop();
                vlcPlayer.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing IVlcPlayerService on shutdown.");
        }
        try
        {
            var autoSync = Services.GetService<MovieTime.Core.Library.ILibraryAutoSyncService>();
            if (autoSync != null)
            {
                await autoSync.StopAsync();
                autoSync.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error stopping auto-sync service on shutdown.");
        }

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
