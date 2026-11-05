using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Library;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class ScanRootServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _databaseService;
    private readonly ScanRootRepository _scanRootRepository;
    private readonly ScanRootService _scanRootService;

    public ScanRootServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ScanRootServiceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_roots.db");

        var storage = new StoragePathService(_tempDir);
        _databaseService = new DatabaseService(storage, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _scanRootRepository = new ScanRootRepository(_databaseService);
        _scanRootService = new ScanRootService(_scanRootRepository, NullLogger<ScanRootService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            _databaseService.DisposeAsync().AsTask().GetAwaiter().GetResult();
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void ValidatePath_EmptyOrNull_ReturnsFalse(string? path)
    {
        var isValid = _scanRootService.ValidatePath(path!, out var normalized, out var error);
        Assert.False(isValid);
        Assert.NotNull(error);
        Assert.Empty(normalized);
    }

    [Theory]
    [InlineData(@"\\server\share\movies")]
    [InlineData(@"//nas/media")]
    [InlineData(@"http://example.com/media")]
    [InlineData(@"smb://nas/media")]
    public void ValidatePath_NetworkOrRemoteUrl_ReturnsFalse(string path)
    {
        var isValid = _scanRootService.ValidatePath(path, out var normalized, out var error);
        Assert.False(isValid);
        Assert.Contains("Network locations", error);
        Assert.Empty(normalized);
    }

    [Theory]
    [InlineData(@"relative\path\movies")]
    [InlineData(@"movies")]
    public void ValidatePath_RelativePath_ReturnsFalse(string path)
    {
        var isValid = _scanRootService.ValidatePath(path, out var normalized, out var error);
        Assert.False(isValid);
        Assert.Contains("absolute", error);
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    [InlineData(@"D:\")]
    public void ValidatePath_WholeDriveRoot_ReturnsFalse(string driveRoot)
    {
        var isValid = _scanRootService.ValidatePath(driveRoot, out var normalized, out var error);
        Assert.False(isValid);
        Assert.Contains("entire disk root", error);
    }

    [Fact]
    public void ValidatePath_ValidLocalFolder_ReturnsTrueAndNormalizedPath()
    {
        var validPath = Path.Combine(_tempDir, "Movies");
        var isValid = _scanRootService.ValidatePath(validPath, out var normalized, out var error);

        Assert.True(isValid);
        Assert.Null(error);
        Assert.Equal(Path.GetFullPath(validPath).TrimEnd('\\', '/'), normalized);
    }

    [Fact]
    public async Task AddRootAsync_ValidPath_InsertsAndReturnsRoot()
    {
        var folder = Path.Combine(_tempDir, "TV_Shows");
        var root = await _scanRootService.AddRootAsync(folder);

        Assert.NotNull(root);
        Assert.True(root.Id > 0);
        Assert.Equal(folder, root.Path);
        Assert.True(root.IsEnabled);

        var allRoots = await _scanRootService.GetRootsAsync();
        Assert.Single(allRoots);
        Assert.Equal(root.Id, allRoots[0].Id);
    }

    [Fact]
    public async Task AddRootAsync_DuplicatePath_IsIdempotent()
    {
        var folder = Path.Combine(_tempDir, "Action_Movies");

        var root1 = await _scanRootService.AddRootAsync(folder);
        var root2 = await _scanRootService.AddRootAsync(folder);

        Assert.Equal(root1.Id, root2.Id);

        var allRoots = await _scanRootService.GetRootsAsync();
        Assert.Single(allRoots);
    }

    [Fact]
    public async Task AddRootAsync_ReEnablesDisabledRoot()
    {
        var folder = Path.Combine(_tempDir, "Documentaries");
        var root = await _scanRootService.AddRootAsync(folder);

        await _scanRootService.SetRootEnabledAsync(root.Id, false);
        var rootsAfterDisable = await _scanRootService.GetRootsAsync();
        Assert.False(rootsAfterDisable[0].IsEnabled);

        // Re-adding re-enables it
        var readded = await _scanRootService.AddRootAsync(folder);
        Assert.True(readded.IsEnabled);
    }

    [Fact]
    public async Task RemoveRootAsync_RemovesRootSuccessfully()
    {
        var folder = Path.Combine(_tempDir, "Anime");
        var root = await _scanRootService.AddRootAsync(folder);

        var removed = await _scanRootService.RemoveRootAsync(root.Id);
        Assert.True(removed);

        var allRoots = await _scanRootService.GetRootsAsync();
        Assert.Empty(allRoots);
    }
}
