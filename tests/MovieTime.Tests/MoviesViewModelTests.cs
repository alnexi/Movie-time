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

public sealed class MoviesViewModelTests : IDisposable
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

    public MoviesViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MoviesViewModelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_movies.db");

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

    private MoviesViewModel CreateViewModel()
    {
        return new MoviesViewModel(
            _databaseService,
            _movieRepository,
            _mediaFileRepository,
            _scanRootService,
            _importService,
            NullLogger<MoviesViewModel>.Instance);
    }

    [Fact]
    public async Task InitializeAsync_WhenDatabaseEmpty_ReportsEmptyLibrary()
    {
        var vm = CreateViewModel();

        await vm.InitializeAsync();

        Assert.True(vm.IsLibraryEmpty);
        Assert.Empty(vm.AllMovies);
        Assert.Empty(vm.DisplayedMovies);
        Assert.Equal("No Movies", vm.MovieCountSummary);
        Assert.False(vm.HasSearchQuery);
    }

    [Fact]
    public async Task LoadMoviesAsync_WithRealData_PopulatesMoviesAndMetadata()
    {
        // 1. Insert real movie
        var movie = new Movie
        {
            Title = "Blade Runner 2049",
            ReleaseYear = 2017,
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        // 2. Insert associated 4K media file
        var file = new MediaFile
        {
            ScanRootId = 1,
            MovieId = movie.Id,
            FilePath = @"D:\Media\Movies\Blade Runner 2049 (2017)\Blade.Runner.2049.2017.2160p.mkv",
            FileName = "Blade.Runner.2049.2017.2160p.mkv",
            FileExtension = ".mkv",
            VideoWidth = 3840,
            VideoHeight = 2160,
            VideoCodec = "HEVC",
            SizeBytes = 1024L * 1024 * 1024 * 18,
            LastScannedAt = DateTime.UtcNow
        };
        await _mediaFileRepository.InsertAsync(file);

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        Assert.False(vm.IsLibraryEmpty);
        Assert.Single(vm.AllMovies);
        Assert.Single(vm.DisplayedMovies);

        var card = vm.DisplayedMovies.First();
        Assert.Equal("Blade Runner 2049", card.Title);
        Assert.Equal(2017, card.ReleaseYear);
        Assert.Equal("2017", card.YearDisplay);
        Assert.Equal("4K HDR", card.QualityTag);
        Assert.Equal("HEVC", card.VideoCodec);
        Assert.Contains("2017", card.MetadataSubtitle);
        Assert.Contains("4K HDR", card.MetadataSubtitle);
    }

    [Fact]
    public async Task SearchQuery_FiltersLocallyByTitle()
    {
        await _movieRepository.InsertAsync(new Movie { Title = "The Matrix", ReleaseYear = 1999 });
        await _movieRepository.InsertAsync(new Movie { Title = "The Matrix Reloaded", ReleaseYear = 2003 });
        await _movieRepository.InsertAsync(new Movie { Title = "Interstellar", ReleaseYear = 2014 });

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        Assert.Equal(3, vm.AllMovies.Count);
        Assert.Equal(3, vm.DisplayedMovies.Count);

        // Apply search query
        vm.SearchQuery = "matrix";

        Assert.Equal(2, vm.DisplayedMovies.Count);
        Assert.All(vm.DisplayedMovies, m => Assert.Contains("Matrix", m.Title));

        // Clear query
        vm.ClearSearchCommand.Execute(null);

        Assert.Empty(vm.SearchQuery);
        Assert.Equal(3, vm.DisplayedMovies.Count);
    }

    [Fact]
    public async Task Sorting_AppliesCorrectOrdering()
    {
        await _movieRepository.InsertAsync(new Movie { Title = "Zootopia", ReleaseYear = 2016, CreatedAt = DateTime.UtcNow.AddDays(-10) });
        await _movieRepository.InsertAsync(new Movie { Title = "Avatar", ReleaseYear = 2009, CreatedAt = DateTime.UtcNow.AddDays(-2) });
        await _movieRepository.InsertAsync(new Movie { Title = "Inception", ReleaseYear = 2010, CreatedAt = DateTime.UtcNow.AddDays(-5) });

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        // 1. Sort by Title (Default A-Z)
        vm.SetSortCommand.Execute("Title");
        Assert.Equal("Avatar", vm.DisplayedMovies[0].Title);
        Assert.Equal("Inception", vm.DisplayedMovies[1].Title);
        Assert.Equal("Zootopia", vm.DisplayedMovies[2].Title);

        // 2. Sort by Year (Newest first)
        vm.SetSortCommand.Execute("Year");
        Assert.Equal("Zootopia", vm.DisplayedMovies[0].Title); // 2016
        Assert.Equal("Inception", vm.DisplayedMovies[1].Title); // 2010
        Assert.Equal("Avatar", vm.DisplayedMovies[2].Title);    // 2009

        // 3. Sort by Recently Added
        vm.SetSortCommand.Execute("RecentlyAdded");
        Assert.Equal("Avatar", vm.DisplayedMovies[0].Title);
    }

    [Fact]
    public async Task CategoryFiltering_FiltersExpectedSubsets()
    {
        var m1 = new Movie { Title = "Star Wars: A New Hope", ReleaseYear = 1977 };
        var m2 = new Movie { Title = "The Hangover", ReleaseYear = 2009 };
        await _movieRepository.InsertAsync(m1);
        await _movieRepository.InsertAsync(m2);

        // Give m1 4K resolution
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            MovieId = m1.Id,
            FilePath = "C:\\starwars.mkv",
            FileName = "starwars.mkv",
            FileExtension = ".mkv",
            VideoHeight = 2160,
            LastScannedAt = DateTime.UtcNow
        });

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        // Filter 4K
        vm.SelectCategoryCommand.Execute("4K");
        Assert.Single(vm.DisplayedMovies);
        Assert.Equal("Star Wars: A New Hope", vm.DisplayedMovies.First().Title);

        // Filter SciFi
        vm.SelectCategoryCommand.Execute("SciFi");
        Assert.Single(vm.DisplayedMovies);
        Assert.Equal("Star Wars: A New Hope", vm.DisplayedMovies.First().Title);

        // Reset to All
        vm.SelectCategoryCommand.Execute("All");
        Assert.Equal(2, vm.DisplayedMovies.Count);
    }

    [Fact]
    public async Task MovieSelection_OpensQuickActionAndPreservesIdentity()
    {
        var movie = new Movie { Title = "Dune: Part Two", ReleaseYear = 2024 };
        await _movieRepository.InsertAsync(movie);
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            MovieId = movie.Id,
            FilePath = @"C:\Media\Dune2.mkv",
            FileName = "Dune2.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000,
            LastScannedAt = DateTime.UtcNow
        });

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        var card = vm.DisplayedMovies.First();
        Assert.False(vm.IsQuickActionOpen);
        Assert.Null(vm.SelectedMovie);

        // Select the movie
        vm.SelectMovieCommand.Execute(card);

        Assert.True(vm.IsQuickActionOpen);
        Assert.NotNull(vm.SelectedMovie);
        Assert.Equal("Dune: Part Two", vm.SelectedMovie.Title);
        Assert.True(card.IsSelected);

        // Tap Play
        string? playedFile = null;
        int? playedId = null;
        vm.PlaybackRequested += (path, id) =>
        {
            playedFile = path;
            playedId = id;
        };
        vm.PlayMovieCommand.Execute(null);
        Assert.Equal(card.FilePath, playedFile);
        Assert.Equal(card.Id, playedId);

        // Tap Go to Details
        string? navigatedTarget = null;
        vm.NavigationRequested += target => navigatedTarget = target;
        vm.GoToDetailsCommand.Execute(null);
        Assert.Equal($"MovieDetails:{card.Id}", navigatedTarget);

        // Dismiss
        vm.DismissQuickActionCommand.Execute(null);
        Assert.False(vm.IsQuickActionOpen);
        Assert.Null(vm.SelectedMovie);
    }
}
