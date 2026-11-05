using System;
using System.IO;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public class StoragePathServiceTests : IDisposable
{
    private readonly string _tempTestDir;
    private readonly StoragePathService _storagePathService;

    public StoragePathServiceTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "MovieTime_Tests_" + Guid.NewGuid().ToString("N"));
        _storagePathService = new StoragePathService(_tempTestDir);
    }

    [Fact]
    public void StoragePathService_ShouldConstructExpectedSubdirectories()
    {
        Assert.Equal(_tempTestDir, _storagePathService.RootDataPath);
        Assert.Equal(Path.Combine(_tempTestDir, "Database"), _storagePathService.DatabaseDirectory);
        Assert.Equal(Path.Combine(_tempTestDir, "Database", "movietime.db"), _storagePathService.DatabaseFilePath);
        Assert.Equal(Path.Combine(_tempTestDir, "Artwork", "Posters"), _storagePathService.PostersDirectory);
        Assert.Equal(Path.Combine(_tempTestDir, "Artwork", "Backdrops"), _storagePathService.BackdropsDirectory);
        Assert.Equal(Path.Combine(_tempTestDir, "Artwork", "Thumbnails"), _storagePathService.ThumbnailsDirectory);
        Assert.Equal(Path.Combine(_tempTestDir, "Logs"), _storagePathService.LogsDirectory);
        Assert.Equal(Path.Combine(_tempTestDir, "Config"), _storagePathService.ConfigDirectory);
        Assert.Equal(Path.Combine(_tempTestDir, "Config", "naming_patterns.json"), _storagePathService.NamingPatternsFilePath);
        Assert.Equal(Path.Combine(_tempTestDir, "Temp"), _storagePathService.TempDirectory);
    }

    [Fact]
    public void EnsureDirectoriesCreated_ShouldCreateAllPhysicalDirectories()
    {
        // Act
        _storagePathService.EnsureDirectoriesCreated();

        // Assert
        Assert.True(Directory.Exists(_storagePathService.RootDataPath));
        Assert.True(Directory.Exists(_storagePathService.DatabaseDirectory));
        Assert.True(Directory.Exists(_storagePathService.PostersDirectory));
        Assert.True(Directory.Exists(_storagePathService.BackdropsDirectory));
        Assert.True(Directory.Exists(_storagePathService.ThumbnailsDirectory));
        Assert.True(Directory.Exists(_storagePathService.LogsDirectory));
        Assert.True(Directory.Exists(_storagePathService.ConfigDirectory));
        Assert.True(Directory.Exists(_storagePathService.TempDirectory));
    }

    [Fact]
    public void DefaultConstructor_ShouldUseLocalAppDataPath()
    {
        var defaultService = new StoragePathService();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var expectedRoot = Path.Combine(localAppData, "MovieTime");

        Assert.Equal(expectedRoot, defaultService.RootDataPath);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempTestDir))
        {
            try
            {
                Directory.Delete(_tempTestDir, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors in temp folder
            }
        }
    }
}
