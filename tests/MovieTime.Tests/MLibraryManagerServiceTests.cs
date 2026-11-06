using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.ViewModels;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Library;
using MovieTime.Core.Parsing;
using MovieTime.Core.Scanning;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class MLibraryManagerServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _databaseService;
    private readonly MovieRepository _movieRepository;
    private readonly SeriesRepository _seriesRepository;
    private readonly SeasonRepository _seasonRepository;
    private readonly EpisodeRepository _episodeRepository;
    private readonly MediaFileRepository _mediaFileRepository;
    private readonly ScanRootRepository _scanRootRepository;
    private readonly ScanRootService _scanRootService;
    private readonly LibraryImportService _importService;
    private readonly MLibraryManagerService _libraryManagerService;

    public MLibraryManagerServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MLibraryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_mlibrary.db");

        var storage = new StoragePathService(_tempDir);
        _databaseService = new DatabaseService(storage, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _movieRepository = new MovieRepository(_databaseService);
        _seriesRepository = new SeriesRepository(_databaseService);
        _seasonRepository = new SeasonRepository(_databaseService);
        _episodeRepository = new EpisodeRepository(_databaseService);
        _mediaFileRepository = new MediaFileRepository(_databaseService);
        _scanRootRepository = new ScanRootRepository(_databaseService);
        _scanRootService = new ScanRootService(_scanRootRepository, NullLogger<ScanRootService>.Instance);

        var scanner = new MediaScannerService(NullLogger<MediaScannerService>.Instance);
        var parser = new FilenameParserEngine(NullLogger<FilenameParserEngine>.Instance);
        _importService = new LibraryImportService(
            _scanRootService,
            scanner,
            parser,
            _databaseService,
            _movieRepository,
            _seriesRepository,
            _seasonRepository,
            _episodeRepository,
            _mediaFileRepository,
            NullLogger<LibraryImportService>.Instance);

        _libraryManagerService = new MLibraryManagerService(
            _scanRootService,
            _importService,
            NullLogger<MLibraryManagerService>.Instance);
    }

    public void Dispose()
    {
        _databaseService.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    [Fact]
    public void GetAvailableDrives_ReturnsLocalDrivesWithMLibraryPaths()
    {
        var drives = _libraryManagerService.GetAvailableDrives();

        Assert.NotNull(drives);
        Assert.NotEmpty(drives);

        foreach (var drive in drives)
        {
            Assert.False(string.IsNullOrWhiteSpace(drive.DriveLetter));
            Assert.Contains("MLibrary", drive.DefaultMLibraryPath);
            Assert.False(string.IsNullOrWhiteSpace(drive.DisplayName));
            Assert.False(string.IsNullOrWhiteSpace(drive.CapacitySummary));
        }
    }

    [Fact]
    public async Task RegisterCustomLibraryPath_WhenFolderDoesNotExist_RequiresConfirmation()
    {
        var targetPath = Path.Combine(_tempDir, "CustomDrive", "MLibrary");
        Assert.False(Directory.Exists(targetPath));

        // When createFolder is false, should fail with DirectoryNotFoundException
        await Assert.ThrowsAsync<DirectoryNotFoundException>(async () =>
        {
            await _libraryManagerService.RegisterCustomLibraryPathAsync(targetPath, createFolder: false);
        });
    }

    [Fact]
    public async Task RegisterCustomLibraryPath_WithConfirmation_CreatesFolderAndRegisters()
    {
        var targetPath = Path.Combine(_tempDir, "ConfirmedDrive", "MLibrary");
        Assert.False(Directory.Exists(targetPath));

        var registered = await _libraryManagerService.RegisterCustomLibraryPathAsync(targetPath, createFolder: true);

        Assert.NotNull(registered);
        Assert.True(Directory.Exists(targetPath));
        Assert.Equal(targetPath, registered.Path);
        Assert.True(registered.FolderExists);
        Assert.True(registered.IsEnabled);

        var allLibraries = await _libraryManagerService.GetRegisteredLibrariesAsync();
        Assert.Single(allLibraries);
        Assert.Equal(targetPath, allLibraries[0].Path);
    }

    [Fact]
    public async Task RegisterCustomLibraryPath_WhenFolderAlreadyExists_RegistersDirectly()
    {
        var targetPath = Path.Combine(_tempDir, "ExistingDrive", "MLibrary");
        Directory.CreateDirectory(targetPath);

        var registered = await _libraryManagerService.RegisterCustomLibraryPathAsync(targetPath, createFolder: false);

        Assert.NotNull(registered);
        Assert.Equal(targetPath, registered.Path);
        Assert.True(registered.FolderExists);

        var allLibraries = await _libraryManagerService.GetRegisteredLibrariesAsync();
        Assert.Single(allLibraries);
    }

    [Fact]
    public async Task RemoveLibrary_UnregistersSuccessfully()
    {
        var targetPath = Path.Combine(_tempDir, "RemoveDrive", "MLibrary");
        var registered = await _libraryManagerService.RegisterCustomLibraryPathAsync(targetPath, createFolder: true);

        var initialList = await _libraryManagerService.GetRegisteredLibrariesAsync();
        Assert.Single(initialList);

        bool removed = await _libraryManagerService.RemoveLibraryAsync(registered.Id);
        Assert.True(removed);

        var afterList = await _libraryManagerService.GetRegisteredLibrariesAsync();
        Assert.Empty(afterList);

        // Physical folder on disk should still exist (non-destructive)
        Assert.True(Directory.Exists(targetPath));
    }

    [Fact]
    public async Task ToggleLibraryEnabled_UpdatesState()
    {
        var targetPath = Path.Combine(_tempDir, "ToggleDrive", "MLibrary");
        var registered = await _libraryManagerService.RegisterCustomLibraryPathAsync(targetPath, createFolder: true);
        Assert.True(registered.IsEnabled);

        bool disabled = await _libraryManagerService.SetLibraryEnabledAsync(registered.Id, false);
        Assert.True(disabled);

        var list = await _libraryManagerService.GetRegisteredLibrariesAsync();
        Assert.False(list[0].IsEnabled);

        bool reenabled = await _libraryManagerService.SetLibraryEnabledAsync(registered.Id, true);
        Assert.True(reenabled);

        list = await _libraryManagerService.GetRegisteredLibrariesAsync();
        Assert.True(list[0].IsEnabled);
    }

    [Fact]
    public async Task ScanningScopeIsolation_OnlyScansRegisteredMLibrary_IgnoresSiblingFolders()
    {
        // Simulate a drive root structure:
        // C:\MockDrive\
        // ├── MLibrary\
        // │   └── Inception (2010)\Inception.2010.1080p.mkv  <-- Inside MLibrary
        // └── Documents\
        //     └── PersonalVideo (2021)\Personal.2021.1080p.mkv  <-- Sibling folder (MUST BE IGNORED)

        var mockDrive = Path.Combine(_tempDir, "MockDrive");
        var mlibraryPath = Path.Combine(mockDrive, "MLibrary");
        var siblingPath = Path.Combine(mockDrive, "Documents");

        var inLibraryMovieDir = Path.Combine(mlibraryPath, "Inception (2010)");
        Directory.CreateDirectory(inLibraryMovieDir);
        File.WriteAllBytes(Path.Combine(inLibraryMovieDir, "Inception.2010.1080p.mkv"), new byte[2048]);

        var outsideMovieDir = Path.Combine(siblingPath, "Personal (2021)");
        Directory.CreateDirectory(outsideMovieDir);
        File.WriteAllBytes(Path.Combine(outsideMovieDir, "Personal.2021.1080p.mkv"), new byte[2048]);

        // Register ONLY the MLibrary folder
        await _libraryManagerService.RegisterCustomLibraryPathAsync(mlibraryPath, createFolder: false);

        // Scan libraries
        var scanResult = await _libraryManagerService.ScanLibrariesAsync();

        Assert.Equal(1, scanResult.FilesDiscovered);
        Assert.Equal(1, scanResult.MoviesCreated);

        var allMovies = await _movieRepository.GetAllAsync();
        Assert.Single(allMovies);
        Assert.Equal("Inception", allMovies[0].Title);

        // Verify the sibling movie outside MLibrary was NEVER touched or imported
        Assert.DoesNotContain(allMovies, m => m.Title.Contains("Personal", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SettingsViewModel_HandlesLoadAndDialogFlow()
    {
        var vm = new SettingsViewModel(_libraryManagerService, NullLogger<SettingsViewModel>.Instance);

        await vm.InitializeAsync();
        Assert.True(vm.IsLibraryEmpty);
        Assert.False(vm.HasRegisteredLibraries);

        // Open add dialog
        await vm.OpenAddDialogAsync();
        Assert.True(vm.IsAddDialogOpen);
        Assert.NotEmpty(vm.AvailableDrives);

        vm.CloseAddDialog();
        Assert.False(vm.IsAddDialogOpen);
    }
}
