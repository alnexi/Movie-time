using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.ViewModels;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Library;
using MovieTime.Core.Parsing;
using MovieTime.Core.Scanning;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class HomeViewModelTests : IDisposable
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

    public HomeViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HomeViewModelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_home.db");

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

    private HomeViewModel CreateViewModel()
    {
        return new HomeViewModel(
            _databaseService,
            _movieRepository,
            _seriesRepository,
            _seasonRepository,
            _episodeRepository,
            _mediaFileRepository,
            _scanRootService,
            _importService,
            NullLogger<HomeViewModel>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_WhenLibraryEmpty_SetsShowcaseFallback()
    {
        var vm = CreateViewModel();

        await vm.InitializeAsync();

        Assert.True(vm.IsLibraryEmpty);
        Assert.NotNull(vm.Hero);
        Assert.Equal("KILLERS OF THE FLOWER MOON", vm.Hero.Title);
        Assert.Equal("Crime", vm.Hero.Genre);
        Assert.Equal("R", vm.Hero.RatingBadge);
        Assert.Equal("Go to Movie", vm.Hero.ActionButtonText);
        Assert.True(vm.Hero.IsSample);

        Assert.NotEmpty(vm.Rows);
        var continueWatchingRow = vm.Rows.FirstOrDefault(r => r.Title == "Continue Watching");
        Assert.NotNull(continueWatchingRow);
        Assert.NotEmpty(continueWatchingRow.Items);

        var firstItem = continueWatchingRow.Items.First();
        Assert.True(firstItem.IsSample);
    }

    [Fact]
    public void ToggleSidebar_SwitchesState()
    {
        var vm = CreateViewModel();

        Assert.False(vm.IsSidebarOpen);

        vm.ToggleSidebarCommand.Execute(null);
        Assert.True(vm.IsSidebarOpen);

        vm.ToggleSidebarCommand.Execute(null);
        Assert.False(vm.IsSidebarOpen);

        vm.OpenSidebarCommand.Execute(null);
        Assert.True(vm.IsSidebarOpen);

        vm.CloseSidebarCommand.Execute(null);
        Assert.False(vm.IsSidebarOpen);
    }

    [Fact]
    public void SelectCategory_UpdatesActiveCategoryAndSidebarItems()
    {
        var vm = CreateViewModel();

        vm.SelectCategoryCommand.Execute("Movies");
        Assert.Equal("Movies", vm.ActiveCategory);

        var moviesItem = vm.SidebarItems.FirstOrDefault(s => s.Title == "Movies");
        Assert.NotNull(moviesItem);
        Assert.True(moviesItem.IsSelected);

        var homeItem = vm.SidebarItems.FirstOrDefault(s => s.Title == "Home");
        Assert.NotNull(homeItem);
        Assert.False(homeItem.IsSelected);
    }

    [Fact]
    public async Task LoadLibraryContentAsync_WithRealData_PopulatesRealCards()
    {
        // Insert actual movie and media file
        var movie = new Movie
        {
            Title = "Inception",
            ReleaseYear = 2010,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        var mediaFile = new MediaFile
        {
            ScanRootId = 1,
            FilePath = @"C:\Movies\Inception.2010.1080p.mkv",
            FileName = "Inception.2010.1080p.mkv",
            FileExtension = ".mkv",
            MovieId = movie.Id,
            SizeBytes = 1024 * 1024 * 100,
            LastScannedAt = DateTime.UtcNow
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        Assert.False(vm.IsLibraryEmpty);
        Assert.Equal("INCEPTION", vm.Hero.Title);
        Assert.False(vm.Hero.IsSample);
        Assert.Equal("Play Movie", vm.Hero.ActionButtonText);

        var allMoviesRow = vm.Rows.FirstOrDefault(r => r.Title == "Movies");
        Assert.NotNull(allMoviesRow);
        Assert.Contains(allMoviesRow.Items, i => i.Title == "Inception");
    }

    [Fact]
    public void MediaCardViewModel_ProgressWidth_Calculations()
    {
        var card = new MediaCardViewModel
        {
            Title = "Test Movie",
            ProgressPercentage = 0.45
        };

        Assert.True(card.HasProgress);
        Assert.Equal(45.0, card.ProgressWidthPercentage);

        card.ProgressPercentage = 1.5; // clamped to 100%
        Assert.Equal(100.0, card.ProgressWidthPercentage);

        card.ProgressPercentage = null;
        Assert.False(card.HasProgress);
        Assert.Equal(0.0, card.ProgressWidthPercentage);
    }
}
