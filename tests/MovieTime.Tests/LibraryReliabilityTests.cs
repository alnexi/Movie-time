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
using MovieTime.Core.Library.Models;
using MovieTime.Core.Parsing;
using MovieTime.Core.Scanning;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class LibraryReliabilityTests : IDisposable
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

    public LibraryReliabilityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LibraryReliabilityTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_reliability.db");

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
        var parser = new FilenameParserEngine(NullLogger<FilenameParserEngine>.Instance, storage);
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
        catch { }
    }

    private MoviesViewModel CreateViewModel()
    {
        return new MoviesViewModel(
            _databaseService,
            _movieRepository,
            _mediaFileRepository,
            _scanRootService,
            _importService,
            null,
            null,
            NullLogger<MoviesViewModel>.Instance);
    }

    [Fact]
    public async Task B01_UnlimitedMovies_SupportsAtLeast25Movies_WithSearchAndSort()
    {
        // Arrange: Insert 30 distinct movies with media files
        const int totalMovies = 30;
        for (int i = 1; i <= totalMovies; i++)
        {
            var movie = new Movie
            {
                Title = $"Blockbuster Feature {i:D2}",
                ReleaseYear = 1990 + i,
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            };
            await _movieRepository.InsertAsync(movie);

            var file = new MediaFile
            {
                ScanRootId = 1,
                MovieId = movie.Id,
                FilePath = Path.Combine(_tempDir, $"Blockbuster.Feature.{i:D2}.mkv"),
                FileName = $"Blockbuster.Feature.{i:D2}.mkv",
                FileExtension = ".mkv",
                VideoWidth = 1920,
                VideoHeight = 1080,
                SizeBytes = 1024L * 1024 * 500,
                IsMissing = false,
                LastScannedAt = DateTime.UtcNow
            };
            await _mediaFileRepository.InsertAsync(file);
        }

        var vm = CreateViewModel();

        // Act: Load movies
        await vm.InitializeAsync();

        // Assert: All 30 movies are present without truncation (No 10-movie cap!)
        Assert.False(vm.IsLibraryEmpty);
        Assert.Equal(totalMovies, vm.AllMovies.Count);
        Assert.Equal(totalMovies, vm.DisplayedMovies.Count);

        // Search finds movie #25 beyond the first 10
        vm.SearchQuery = "Feature 25";
        Assert.Single(vm.DisplayedMovies);
        Assert.Equal("Blockbuster Feature 25", vm.DisplayedMovies[0].Title);

        // Clear search
        vm.SearchQuery = string.Empty;
        Assert.Equal(totalMovies, vm.DisplayedMovies.Count);

        // Sorting by Year descending works across all 30
        vm.SetSortCommand.Execute("Year");
        Assert.Equal("Blockbuster Feature 30", vm.DisplayedMovies[0].Title);
        Assert.Equal(2020, vm.DisplayedMovies[0].ReleaseYear);
    }

    [Fact]
    public async Task B02_MissingMedia_ExcludesMissingMoviesFromActiveLibraryViews()
    {
        // Arrange: Insert 2 movies, 1 active and 1 missing
        var movie1 = new Movie { Title = "Active Movie", ReleaseYear = 2021 };
        await _movieRepository.InsertAsync(movie1);
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            ScanRootId = 1,
            MovieId = movie1.Id,
            FilePath = Path.Combine(_tempDir, "Active.Movie.2021.mkv"),
            FileName = "Active.Movie.2021.mkv",
            FileExtension = ".mkv",
            IsMissing = false
        });

        var movie2 = new Movie { Title = "Deleted Movie", ReleaseYear = 2020 };
        await _movieRepository.InsertAsync(movie2);
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            ScanRootId = 1,
            MovieId = movie2.Id,
            FilePath = Path.Combine(_tempDir, "Deleted.Movie.2020.mkv"),
            FileName = "Deleted.Movie.2020.mkv",
            FileExtension = ".mkv",
            IsMissing = true // Confirmed missing from disk
        });

        var vm = CreateViewModel();

        // Act
        await vm.InitializeAsync();

        // Assert: Only the active movie appears in active views
        Assert.Single(vm.AllMovies);
        Assert.Single(vm.DisplayedMovies);
        Assert.Equal("Active Movie", vm.DisplayedMovies[0].Title);
    }

    [Fact]
    public async Task B02_DriveSafety_OfflineDrive_DoesNotMarkRecordsMissing()
    {
        // Arrange: An existing media file
        var root = new ScanRoot
        {
            Path = @"Z:\NonExistentDriveLetter\MLibrary",
            NormalizedPath = @"Z:\NonExistentDriveLetter\MLibrary",
            IsEnabled = true
        };
        await _scanRootRepository.InsertAsync(root);

        var movie = new Movie { Title = "Safe Movie", ReleaseYear = 2022 };
        await _movieRepository.InsertAsync(movie);

        var file = new MediaFile
        {
            ScanRootId = root.Id,
            MovieId = movie.Id,
            FilePath = @"Z:\NonExistentDriveLetter\MLibrary\Safe.Movie.2022.mkv",
            FileName = "Safe.Movie.2022.mkv",
            FileExtension = ".mkv",
            IsMissing = false
        };
        await _mediaFileRepository.InsertAsync(file);

        // Act: Run import on inaccessible root
        var result = await _importService.ImportAsync();

        // Assert: Root skipped with error, file is NOT marked missing
        Assert.Contains(result.Errors, e => e.Path.Contains("NonExistentDriveLetter"));
        var reloaded = await _mediaFileRepository.GetByIdAsync(file.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded.IsMissing);
    }

    [Fact]
    public async Task B03_MultiDriveDistinctMovies_SameFilenameOnDifferentDrives_CreatesDistinctMovies()
    {
        // Root 1 on Drive D
        var rootD = Path.Combine(_tempDir, "DriveD_MLibrary");
        Directory.CreateDirectory(rootD);
        var scanRootD = new ScanRoot { Path = rootD, NormalizedPath = rootD, IsEnabled = true };
        await _scanRootRepository.InsertAsync(scanRootD);

        // Root 2 on Drive E
        var rootE = Path.Combine(_tempDir, "DriveE_MLibrary");
        Directory.CreateDirectory(rootE);
        var scanRootE = new ScanRoot { Path = rootE, NormalizedPath = rootE, IsEnabled = true };
        await _scanRootRepository.InsertAsync(scanRootE);

        // Create identical movie files in both directories
        var fileD = Path.Combine(rootD, "Inception.2010.1080p.mkv");
        File.WriteAllText(fileD, "fake video data D");

        var fileE = Path.Combine(rootE, "Inception.2010.1080p.mkv");
        File.WriteAllText(fileE, "fake video data E");

        // Act: Run import
        var result = await _importService.ImportAsync();

        // Assert: Two distinct Movie records are created
        var allMovies = await _movieRepository.GetAllAsync();
        var inceptionMovies = allMovies.Where(m => m.Title.Equals("Inception", StringComparison.OrdinalIgnoreCase)).ToList();

        Assert.Equal(2, inceptionMovies.Count);
        Assert.NotEqual(inceptionMovies[0].Id, inceptionMovies[1].Id);

        var filesD = await _mediaFileRepository.GetByMovieIdAsync(inceptionMovies[0].Id);
        var filesE = await _mediaFileRepository.GetByMovieIdAsync(inceptionMovies[1].Id);
        Assert.Single(filesD);
        Assert.Single(filesE);
        Assert.NotEqual(filesD[0].ScanRootId, filesE[0].ScanRootId);
    }
}
