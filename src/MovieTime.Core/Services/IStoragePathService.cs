namespace MovieTime.Core.Services;

/// <summary>
/// Service providing access to local application storage paths.
/// Ensures all application data remains strictly local in accordance with offline requirements.
/// </summary>
public interface IStoragePathService
{
    /// <summary>
    /// Gets the root local application data folder path (%LocalAppData%\MovieTime).
    /// </summary>
    string RootDataPath { get; }

    /// <summary>
    /// Gets the path to the database directory.
    /// </summary>
    string DatabaseDirectory { get; }

    /// <summary>
    /// Gets the full path to the primary SQLite database file.
    /// </summary>
    string DatabaseFilePath { get; }

    /// <summary>
    /// Gets the directory where extracted posters are cached.
    /// </summary>
    string PostersDirectory { get; }

    /// <summary>
    /// Gets the directory where extracted backdrops are cached.
    /// </summary>
    string BackdropsDirectory { get; }

    /// <summary>
    /// Gets the directory where thumbnails are cached.
    /// </summary>
    string ThumbnailsDirectory { get; }

    /// <summary>
    /// Gets the directory where hover preview frames are cached.
    /// </summary>
    string PreviewsDirectory { get; }

    /// <summary>
    /// Gets the directory where application log files are stored.
    /// </summary>
    string LogsDirectory { get; }

    /// <summary>
    /// Gets the directory where configuration files (e.g. naming_patterns.json) reside.
    /// </summary>
    string ConfigDirectory { get; }

    /// <summary>
    /// Gets the path to the naming_patterns.json file.
    /// </summary>
    string NamingPatternsFilePath { get; }

    /// <summary>
    /// Gets the path to the temporary scratch directory.
    /// </summary>
    string TempDirectory { get; }

    /// <summary>
    /// Ensures that all required local application data directories exist on disk.
    /// </summary>
    void EnsureDirectoriesCreated();
}
