namespace MovieTime.Core.Services;

/// <summary>
/// Default implementation of <see cref="IStoragePathService"/>.
/// Manages standard subdirectories under %LocalAppData%\MovieTime.
/// </summary>
public sealed class StoragePathService : IStoragePathService
{
    public string RootDataPath { get; }
    public string DatabaseDirectory => Path.Combine(RootDataPath, "Database");
    public string DatabaseFilePath => Path.Combine(DatabaseDirectory, "movietime.db");
    public string PostersDirectory => Path.Combine(RootDataPath, "Artwork", "Posters");
    public string BackdropsDirectory => Path.Combine(RootDataPath, "Artwork", "Backdrops");
    public string ThumbnailsDirectory => Path.Combine(RootDataPath, "Artwork", "Thumbnails");
    public string LogsDirectory => Path.Combine(RootDataPath, "Logs");
    public string ConfigDirectory => Path.Combine(RootDataPath, "Config");
    public string NamingPatternsFilePath => Path.Combine(ConfigDirectory, "naming_patterns.json");
    public string TempDirectory => Path.Combine(RootDataPath, "Temp");

    public StoragePathService(string? customBasePath = null)
    {
        RootDataPath = string.IsNullOrWhiteSpace(customBasePath)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppConstants.LocalDataFolder)
            : customBasePath;
    }

    public void EnsureDirectoriesCreated()
    {
        Directory.CreateDirectory(RootDataPath);
        Directory.CreateDirectory(DatabaseDirectory);
        Directory.CreateDirectory(PostersDirectory);
        Directory.CreateDirectory(BackdropsDirectory);
        Directory.CreateDirectory(ThumbnailsDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(ConfigDirectory);
        Directory.CreateDirectory(TempDirectory);
    }
}
