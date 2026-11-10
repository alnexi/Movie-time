using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Library;
using MovieTime.Core.Logging;
using MovieTime.Core.Parsing;
using MovieTime.Core.Scanning;
using MovieTime.Core.Services;
using Serilog;

namespace MovieTime.Core.Extensions;

/// <summary>
/// Extension methods for registering MovieTime.Core services and logging into an <see cref="IServiceCollection"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds foundational domain and infrastructure services from MovieTime.Core, including storage and SQLite database.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="customStorageRoot">Optional custom root directory for application data.</param>
    /// <param name="customDatabasePath">Optional custom path for the SQLite database file.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddMovieTimeCore(
        this IServiceCollection services,
        string? customStorageRoot = null,
        string? customDatabasePath = null)
    {
        services.AddSingleton<IStoragePathService>(sp => new StoragePathService(customStorageRoot));
        services.AddSingleton<IAppEnvironment, AppEnvironment>();
        services.AddSingleton<IDatabaseService>(sp =>
        {
            var storage = sp.GetRequiredService<IStoragePathService>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<DatabaseService>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DatabaseService>.Instance;
            return new DatabaseService(storage, logger, customDatabasePath);
        });
        services.AddSingleton<IMediaScannerService>(sp =>
        {
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<MediaScannerService>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<MediaScannerService>.Instance;
            return new MediaScannerService(logger);
        });
        services.AddSingleton<IFilenameParserEngine>(sp =>
        {
            var storage = sp.GetRequiredService<IStoragePathService>();
            var loggerFactory = sp.GetService<ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger<FilenameParserEngine>() ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<FilenameParserEngine>.Instance;
            return new FilenameParserEngine(logger, storage);
        });

        // Repositories
        services.AddSingleton<IMovieRepository, Database.Repositories.MovieRepository>();
        services.AddSingleton<ISeriesRepository, Database.Repositories.SeriesRepository>();
        services.AddSingleton<ISeasonRepository, Database.Repositories.SeasonRepository>();
        services.AddSingleton<IEpisodeRepository, Database.Repositories.EpisodeRepository>();
        services.AddSingleton<IMediaFileRepository, Database.Repositories.MediaFileRepository>();
        services.AddSingleton<IScanRootRepository, Database.Repositories.ScanRootRepository>();
        services.AddSingleton<IPlaybackProgressRepository, Database.Repositories.PlaybackProgressRepository>();
        services.AddSingleton<IPlaylistRepository, Database.Repositories.PlaylistRepository>();

        // Library synchronization and scan root services
        services.AddSingleton<Library.IScanRootService, Library.ScanRootService>();
        services.AddSingleton<Library.ILibraryImportService, Library.LibraryImportService>();
        services.AddSingleton<Library.ILibraryAutoSyncService, Library.LibraryAutoSyncService>();
        services.AddSingleton<Library.IMLibraryManagerService, Library.MLibraryManagerService>();

        // Subtitle customization settings persistence
        services.AddSingleton<Playback.ISubtitleSettingsService, Playback.SubtitleSettingsService>();

        // Theme customization settings persistence and manager
        services.AddSingleton<Theme.IThemeSettingsService, Theme.ThemeSettingsService>();
        services.AddSingleton<Theme.IThemeManagerService, Theme.ThemeManagerService>();

        // Artwork and thumbnail extraction pipeline
        services.AddSingleton<Artwork.IEmbeddedArtworkExtractor, Artwork.EmbeddedArtworkExtractor>();
        services.AddSingleton<Artwork.IImageQualityEvaluator, Artwork.ImageQualityEvaluator>();
        services.AddSingleton<Artwork.IArtworkStorageService, Artwork.ArtworkStorageService>();
        services.AddSingleton<Artwork.IArtworkPipelineService, Artwork.ArtworkPipelineService>();

        return services;
    }

    /// <summary>
    /// Adds local Serilog logging to the service collection with rolling file sink support.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="storagePathService">Optional storage path service; if omitted, the default path service is used.</param>
    /// <param name="isDevelopment">Whether to enable Debug-level logging.</param>
    /// <returns>The same service collection for chaining.</returns>
    public static IServiceCollection AddMovieTimeLogging(
        this IServiceCollection services,
        IStoragePathService? storagePathService = null,
        bool isDevelopment = false)
    {
        var storage = storagePathService ?? new StoragePathService();
        var serilogLogger = LoggingBootstrapper.CreateLogger(storage.LogsDirectory, isDevelopment);

        // Configure static Serilog instance for global fallback access
        Log.Logger = serilogLogger;

        // Bridge Serilog into Microsoft.Extensions.Logging
        services.AddLogging(loggingBuilder =>
        {
            loggingBuilder.ClearProviders();
            loggingBuilder.AddSerilog(serilogLogger, dispose: true);
        });

        // Register Serilog.ILogger interface directly in DI
        services.AddSingleton<Serilog.ILogger>(serilogLogger);

        return services;
    }
}
