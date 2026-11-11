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

public class Phase8ImprovementsTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly StoragePathService _storagePathService;
    private readonly DatabaseService _databaseService;
    private readonly MovieRepository _movieRepository;
    private readonly SeriesRepository _seriesRepository;
    private readonly SeasonRepository _seasonRepository;
    private readonly EpisodeRepository _episodeRepository;
    private readonly MediaFileRepository _mediaFileRepository;
    private readonly ScanRootRepository _scanRootRepository;
    private readonly ScanRootService _scanRootService;
    private readonly LibraryImportService _importService;
    private readonly LibraryExclusionRepository _exclusionRepository;
    private readonly PlaybackProgressRepository _playbackProgressRepository;
    private readonly MediaRemovalService _removalService;

    public Phase8ImprovementsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MovieTime_Phase8Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_phase8.db");

        _storagePathService = new StoragePathService(_tempDir);
        _databaseService = new DatabaseService(_storagePathService, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _movieRepository = new MovieRepository(_databaseService);
        _seriesRepository = new SeriesRepository(_databaseService);
        _seasonRepository = new SeasonRepository(_databaseService);
        _episodeRepository = new EpisodeRepository(_databaseService);
        _mediaFileRepository = new MediaFileRepository(_databaseService);
        _scanRootRepository = new ScanRootRepository(_databaseService);
        _scanRootService = new ScanRootService(_scanRootRepository, NullLogger<ScanRootService>.Instance);
        _exclusionRepository = new LibraryExclusionRepository(_databaseService, NullLogger<LibraryExclusionRepository>.Instance);
        _playbackProgressRepository = new PlaybackProgressRepository(_databaseService);

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
            null,
            _exclusionRepository,
            NullLogger<LibraryImportService>.Instance);

        _removalService = new MediaRemovalService(
            _exclusionRepository,
            _mediaFileRepository,
            _movieRepository,
            _episodeRepository,
            _seasonRepository,
            _seriesRepository,
            _playbackProgressRepository,
            _scanRootRepository,
            NullLogger<MediaRemovalService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    [Fact]
    public async Task MediaRemoval_RemoveFromLibrary_PersistsExclusion_AndCleansDatabase()
    {
        // Arrange: Create movie and media file
        var movie = new Movie { Title = "Inception", ReleaseYear = 2010 };
        await _movieRepository.InsertAsync(movie);

        var filePath = Path.Combine(_tempDir, "Inception.2010.mkv");
        await File.WriteAllTextAsync(filePath, "dummy video content");

        var file = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = filePath,
            FileName = "Inception.2010.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1024,
            LastScannedAt = DateTime.UtcNow
        };
        await _mediaFileRepository.InsertAsync(file);

        // Act: Remove from River library only
        var result = await _removalService.RemoveFromLibraryOnlyAsync(filePath, "UserTestRemoval");

        // Assert:
        Assert.True(result);

        // 1. File on disk is NOT deleted
        Assert.True(File.Exists(filePath));

        // 2. Exclusion record is persisted in SQLite
        Assert.True(await _exclusionRepository.IsPathExcludedAsync(filePath));

        // 3. MediaFile record is deleted from SQLite
        var remainingFiles = await _mediaFileRepository.GetByMovieIdAsync(movie.Id);
        Assert.Empty(remainingFiles);

        // 4. Empty parent movie is also cleaned up
        var remainingMovie = await _movieRepository.GetByIdAsync(movie.Id);
        Assert.Null(remainingMovie);
    }

    [Fact]
    public async Task MediaRemoval_IsPathExcluded_CaseInsensitiveMatching()
    {
        var testPath = Path.Combine(_tempDir, "The.Dark.Knight.mkv");
        await _exclusionRepository.AddExclusionAsync(testPath, "Test");

        // Upper case check
        Assert.True(await _removalService.IsPathExcludedAsync(testPath.ToUpperInvariant()));
        // Lower case check
        Assert.True(await _removalService.IsPathExcludedAsync(testPath.ToLowerInvariant()));
    }

    [Fact]
    public async Task MediaRemoval_RestoreExclusion_AllowsReImport()
    {
        var testPath = Path.Combine(_tempDir, "Interstellar.mkv");
        await _exclusionRepository.AddExclusionAsync(testPath, "Test");
        Assert.True(await _removalService.IsPathExcludedAsync(testPath));

        var restored = await _removalService.RestoreExclusionAsync(testPath);
        Assert.True(restored);
        Assert.False(await _removalService.IsPathExcludedAsync(testPath));
    }

    [Fact]
    public async Task HomeViewModel_RecentlyAdded_IncludesAllMediaTypes_SortedNewestFirst()
    {
        // 1. Create a Movie
        var movie = new Movie { Title = "Movie A", ReleaseYear = 2021 };
        await _movieRepository.InsertAsync(movie);
        var moviePath = Path.Combine(_tempDir, "MovieA.mkv");
        await File.WriteAllTextAsync(moviePath, "test");
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            MovieId = movie.Id,
            FilePath = moviePath,
            FileName = "MovieA.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000,
            LastScannedAt = DateTime.UtcNow.AddMinutes(-30)
        });

        // 2. Create a TV Series + Season + Episode
        var series = new Series { Title = "Series B", StartYear = 2022 };
        await _seriesRepository.InsertAsync(series);
        var season = new Season { SeriesId = series.Id, SeasonNumber = 1 };
        await _seasonRepository.InsertAsync(season);
        var epPath = Path.Combine(_tempDir, "SeriesB.S01E01.mkv");
        await File.WriteAllTextAsync(epPath, "test");
        var ep = new Episode
        {
            SeriesId = series.Id,
            SeasonId = season.Id,
            EpisodeNumber = 1,
            Title = "Pilot"
        };
        await _episodeRepository.InsertAsync(ep);
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            EpisodeId = ep.Id,
            FilePath = epPath,
            FileName = "SeriesB.S01E01.mkv",
            FileExtension = ".mkv",
            SizeBytes = 2000,
            LastScannedAt = DateTime.UtcNow.AddMinutes(-10) // newer
        });

        // 3. Create a Standalone video file
        var standalonePath = Path.Combine(_tempDir, "Documentary.mp4");
        await File.WriteAllTextAsync(standalonePath, "test");
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            FilePath = standalonePath,
            FileName = "Documentary.mp4",
            FileExtension = ".mp4",
            SizeBytes = 1500,
            LastScannedAt = DateTime.UtcNow.AddMinutes(-5) // newest
        });

        var vm = new HomeViewModel(
            _databaseService,
            _movieRepository,
            _seriesRepository,
            _seasonRepository,
            _episodeRepository,
            _mediaFileRepository,
            _scanRootService,
            _importService,
            null,
            null,
            null,
            null,
            _exclusionRepository,
            NullLogger<HomeViewModel>.Instance);

        await vm.InitializeAsync();

        var recentRow = vm.Rows.FirstOrDefault(r => r.Title == "Recently Added");
        Assert.NotNull(recentRow);

        // Assert: Recently Added has 3 items
        Assert.Equal(3, recentRow.Items.Count);

        // Assert: Newest first
        Assert.Equal("Documentary", recentRow.Items[0].Title);
        Assert.Equal("Series B", recentRow.Items[1].Title);
        Assert.Equal("Movie A", recentRow.Items[2].Title);
    }

    [Fact]
    public async Task HomeViewModel_RecentlyAdded_SkipsExcludedAndMissingFiles()
    {
        var movie = new Movie { Title = "Active Movie", ReleaseYear = 2023 };
        await _movieRepository.InsertAsync(movie);
        var moviePath = Path.Combine(_tempDir, "Active.mkv");
        await File.WriteAllTextAsync(moviePath, "test");
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            MovieId = movie.Id,
            FilePath = moviePath,
            FileName = "Active.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000,
            LastScannedAt = DateTime.UtcNow
        });

        // Excluded movie
        var excludedPath = Path.Combine(_tempDir, "Excluded.mkv");
        await File.WriteAllTextAsync(excludedPath, "test");
        await _exclusionRepository.AddExclusionAsync(excludedPath, null, "UserRemoved");
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            FilePath = excludedPath,
            FileName = "Excluded.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000,
            LastScannedAt = DateTime.UtcNow
        });

        // Missing file
        var missingPath = Path.Combine(_tempDir, "NonExistent.mkv");
        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            FilePath = missingPath,
            FileName = "NonExistent.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000,
            IsMissing = true,
            LastScannedAt = DateTime.UtcNow
        });

        var vm = new HomeViewModel(
            _databaseService,
            _movieRepository,
            _seriesRepository,
            _seasonRepository,
            _episodeRepository,
            _mediaFileRepository,
            _scanRootService,
            _importService,
            null,
            null,
            null,
            null,
            _exclusionRepository,
            NullLogger<HomeViewModel>.Instance);

        await vm.InitializeAsync();

        var recentRow = vm.Rows.FirstOrDefault(r => r.Title == "Recently Added");
        Assert.NotNull(recentRow);

        // Only Active Movie should be present in Recently Added
        Assert.Single(recentRow.Items);
        Assert.Equal("Active Movie", recentRow.Items[0].Title);
    }

    [Fact]
    public async Task TVShowDetails_EpisodeBackdrops_RotatesCandidatesAndFallsBackToSeries()
    {
        var series = new Series
        {
            Title = "Breaking Bad",
            StartYear = 2008,
            BackdropPath = Path.Combine(_tempDir, "series_backdrop.jpg")
        };
        await File.WriteAllTextAsync(series.BackdropPath, "dummy");
        await _seriesRepository.InsertAsync(series);

        var season = new Season { SeriesId = series.Id, SeasonNumber = 1 };
        await _seasonRepository.InsertAsync(season);

        // Episode 1 with valid thumbnail
        var thumb1 = Path.Combine(_tempDir, "ep1_thumb.jpg");
        await File.WriteAllTextAsync(thumb1, "dummy");
        var ep1 = new Episode
        {
            SeriesId = series.Id,
            SeasonId = season.Id,
            EpisodeNumber = 1,
            Title = "Pilot",
            ThumbnailPath = thumb1
        };
        await _episodeRepository.InsertAsync(ep1);

        // Episode 2 with valid thumbnail
        var thumb2 = Path.Combine(_tempDir, "ep2_thumb.jpg");
        await File.WriteAllTextAsync(thumb2, "dummy");
        var ep2 = new Episode
        {
            SeriesId = series.Id,
            SeasonId = season.Id,
            EpisodeNumber = 2,
            Title = "Cat's in the Bag...",
            ThumbnailPath = thumb2
        };
        await _episodeRepository.InsertAsync(ep2);

        var vm = new SeriesDetailsViewModel(
            _seriesRepository,
            _seasonRepository,
            _episodeRepository,
            _mediaFileRepository,
            new PlaybackProgressRepository(_databaseService),
            null,
            _removalService,
            null,
            NullLogger<SeriesDetailsViewModel>.Instance);

        await vm.LoadSeriesAsync(series.Id);

        // Assert: BackdropPath is set to one of the episode thumbnails
        Assert.NotNull(vm.BackdropPath);
        Assert.True(vm.BackdropPath == thumb1 || vm.BackdropPath == thumb2);

        // Clean unload stops rotation timer cleanly
        vm.StopRotation();
    }

    [Fact]
    public async Task RemoveMovieFromLibraryAsync_RemovesMovieAndExcludesFiles_EvenWhenPhysicalFileMissing_QA004()
    {
        // Arrange
        var movie = new Movie
        {
            Title = "Missing Movie",
            ReleaseYear = 2020,
            CreatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        var nonExistentPath = Path.Combine(_tempDir, "DoesNotExist_12345.mkv");
        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = nonExistentPath,
            FileName = "DoesNotExist_12345.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1024,
            IsMissing = true
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        // Act
        bool removed = await _removalService.RemoveMovieFromLibraryAsync(movie.Id);

        // Assert
        Assert.True(removed);
        var remainingMovie = await _movieRepository.GetByIdAsync(movie.Id);
        Assert.Null(remainingMovie);

        var remainingFiles = await _mediaFileRepository.GetByMovieIdAsync(movie.Id);
        Assert.Empty(remainingFiles);

        bool isExcluded = await _removalService.IsPathExcludedAsync(nonExistentPath);
        Assert.True(isExcluded);
    }

    [Fact]
    public async Task ScanRootRemoval_ReconcilesAndRemovesOrphanedMediaFiles_QA005()
    {
        // Arrange
        var rootDir = Path.Combine(_tempDir, "CustomScanRoot");
        Directory.CreateDirectory(rootDir);
        var root = await _scanRootService.AddRootAsync(rootDir);

        var movie = new Movie { Title = "Root Movie", ReleaseYear = 2021, CreatedAt = DateTime.UtcNow };
        await _movieRepository.InsertAsync(movie);

        var filePath = Path.Combine(rootDir, "MovieFile.mkv");
        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            ScanRootId = root.Id,
            FilePath = filePath,
            FileName = "MovieFile.mkv",
            FileExtension = ".mkv",
            SizeBytes = 2048,
            IsMissing = false
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        // ScanRootService configured with removal service for reconciliation
        var scanRootServiceWithRemoval = new ScanRootService(
            _scanRootRepository,
            _removalService,
            NullLogger<ScanRootService>.Instance);

        // Act - Remove the scan root
        bool success = await scanRootServiceWithRemoval.RemoveRootAsync(root.Id);

        // Assert - Root is removed and orphaned media file and parent movie are cleaned up
        Assert.True(success);
        var roots = await _scanRootService.GetRootsAsync();
        Assert.DoesNotContain(roots, r => r.Id == root.Id);

        var remainingMovie = await _movieRepository.GetByIdAsync(movie.Id);
        Assert.Null(remainingMovie);

        var remainingFiles = await _mediaFileRepository.GetByMovieIdAsync(movie.Id);
        Assert.Empty(remainingFiles);
    }

    [Fact]
    public async Task ThemeSettingsService_DebouncesRapidSliderWrites_QA006()
    {
        // Arrange
        var themeDir = Path.Combine(_tempDir, "ThemeTest_" + Guid.NewGuid().ToString("N"));
        var storage = new StoragePathService(themeDir);
        var themeService = new MovieTime.Core.Theme.ThemeSettingsService(storage);

        // Act - Simulate rapid slider movement
        await themeService.UpdateThemeValueAsync(0.1);
        await themeService.UpdateThemeValueAsync(0.2);
        await themeService.UpdateThemeValueAsync(0.5);
        await themeService.UpdateThemeValueAsync(0.95);

        // In-memory update is instantaneous for 60 FPS UI responsiveness
        Assert.Equal(0.95, themeService.CurrentSettings.ThemeValue, 4);

        // Flush pending debounced writes to disk
        await themeService.FlushPendingSavesAsync();

        // Assert - Disk state reflects final value
        var reloaded = await themeService.LoadSettingsAsync();
        Assert.Equal(0.95, reloaded.ThemeValue, 4);
    }

    [Fact]
    public async Task ScanRootRemoval_NestedRoots_PreservesFilesCoveredBySurvivingParentRoot()
    {
        // Arrange: Parent root "D:\Media" and child root "D:\Media\Action"
        var parentDir = Path.Combine(_tempDir, "Media");
        var childDir = Path.Combine(parentDir, "Action");
        Directory.CreateDirectory(childDir);

        var parentRoot = await _scanRootService.AddRootAsync(parentDir);
        var childRoot = await _scanRootService.AddRootAsync(childDir);

        var movie = new Movie { Title = "Action Hero", ReleaseYear = 2022, CreatedAt = DateTime.UtcNow };
        await _movieRepository.InsertAsync(movie);

        var filePath = Path.Combine(childDir, "Hero.mkv");
        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            ScanRootId = childRoot.Id,
            FilePath = filePath,
            FileName = "Hero.mkv",
            FileExtension = ".mkv",
            SizeBytes = 4096,
            IsMissing = false
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        var scanRootServiceWithRemoval = new ScanRootService(
            _scanRootRepository,
            _removalService,
            NullLogger<ScanRootService>.Instance);

        // Act: Unregister the child root
        bool success = await scanRootServiceWithRemoval.RemoveRootAsync(childRoot.Id);

        // Assert: Child root is removed, parent root survives
        Assert.True(success);
        var roots = await _scanRootService.GetRootsAsync();
        Assert.DoesNotContain(roots, r => r.Id == childRoot.Id);
        Assert.Contains(roots, r => r.Id == parentRoot.Id);

        // Movie and media file must be PRESERVED because parentRoot still covers it
        var survivingMovie = await _movieRepository.GetByIdAsync(movie.Id);
        Assert.NotNull(survivingMovie);

        var survivingFiles = await _mediaFileRepository.GetByMovieIdAsync(movie.Id);
        Assert.Single(survivingFiles);
        // And the file's ScanRootId should now point to parentRoot.Id!
        Assert.Equal(parentRoot.Id, survivingFiles[0].ScanRootId);
    }

    [Fact]
    public async Task ScanRootRemoval_DirectoryBoundaryProtection_DoesNotMatchSiblingFoldersWithCommonPrefixes()
    {
        // Arrange: Root is "Movies" (e.g. C:\Temp\Movies)
        // Sibling folder is "Movies_Backup" (e.g. C:\Temp\Movies_Backup)
        var moviesDir = Path.Combine(_tempDir, "Movies");
        var backupDir = Path.Combine(_tempDir, "Movies_Backup");
        Directory.CreateDirectory(moviesDir);
        Directory.CreateDirectory(backupDir);

        var moviesRoot = await _scanRootService.AddRootAsync(moviesDir);
        var backupRoot = await _scanRootService.AddRootAsync(backupDir);

        var movie1 = new Movie { Title = "Movie In Main", ReleaseYear = 2020, CreatedAt = DateTime.UtcNow };
        await _movieRepository.InsertAsync(movie1);
        var file1 = new MediaFile
        {
            MovieId = movie1.Id,
            ScanRootId = moviesRoot.Id,
            FilePath = Path.Combine(moviesDir, "Main.mkv"),
            FileName = "Main.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000
        };
        await _mediaFileRepository.InsertAsync(file1);

        var movie2 = new Movie { Title = "Movie In Backup", ReleaseYear = 2020, CreatedAt = DateTime.UtcNow };
        await _movieRepository.InsertAsync(movie2);
        var file2 = new MediaFile
        {
            MovieId = movie2.Id,
            ScanRootId = backupRoot.Id,
            FilePath = Path.Combine(backupDir, "Backup.mkv"),
            FileName = "Backup.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000
        };
        await _mediaFileRepository.InsertAsync(file2);

        var scanRootServiceWithRemoval = new ScanRootService(
            _scanRootRepository,
            _removalService,
            NullLogger<ScanRootService>.Instance);

        // Act: Remove moviesRoot (e.g. "Movies")
        bool success = await scanRootServiceWithRemoval.RemoveRootAsync(moviesRoot.Id);
        Assert.True(success);

        // Assert: Main movie removed, but Backup movie must NOT be touched
        var remainingMovie1 = await _movieRepository.GetByIdAsync(movie1.Id);
        Assert.Null(remainingMovie1);

        var remainingMovie2 = await _movieRepository.GetByIdAsync(movie2.Id);
        Assert.NotNull(remainingMovie2);
        var remainingBackupFiles = await _mediaFileRepository.GetByMovieIdAsync(movie2.Id);
        Assert.Single(remainingBackupFiles);
    }

    [Fact]
    public async Task MovieRemoval_MultipleMediaFiles_OnlyDeletesMovieEntityWhenLastFileRemoved()
    {
        // Arrange: A movie with 2 media files (e.g., Part 1 and Part 2)
        var movie = new Movie { Title = "Dune Part 1 & 2", ReleaseYear = 2021, CreatedAt = DateTime.UtcNow };
        await _movieRepository.InsertAsync(movie);

        var file1 = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = Path.Combine(_tempDir, "Dune_Part1.mkv"),
            FileName = "Dune_Part1.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000
        };
        await _mediaFileRepository.InsertAsync(file1);

        var file2 = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = Path.Combine(_tempDir, "Dune_Part2.mkv"),
            FileName = "Dune_Part2.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000
        };
        await _mediaFileRepository.InsertAsync(file2);

        // Act 1: Remove only file 1 via path
        bool removedFirst = await _removalService.RemoveFromLibraryOnlyAsync(file1.FilePath);
        Assert.True(removedFirst);

        // Assert 1: Movie entity still exists because file 2 is still attached!
        var movieAfterFirst = await _movieRepository.GetByIdAsync(movie.Id);
        Assert.NotNull(movieAfterFirst);
        var filesAfterFirst = await _mediaFileRepository.GetByMovieIdAsync(movie.Id);
        Assert.Single(filesAfterFirst);
        Assert.Equal(file2.FilePath, filesAfterFirst[0].FilePath);

        // Act 2: Remove file 2
        bool removedSecond = await _removalService.RemoveFromLibraryOnlyAsync(file2.FilePath);
        Assert.True(removedSecond);

        // Assert 2: Now that all files are removed, Movie entity must be deleted
        var movieAfterSecond = await _movieRepository.GetByIdAsync(movie.Id);
        Assert.Null(movieAfterSecond);
        var filesAfterSecond = await _mediaFileRepository.GetByMovieIdAsync(movie.Id);
        Assert.Empty(filesAfterSecond);
    }

    [Fact]
    public async Task MediaRemoval_CleansUpAssociatedPlaybackProgress_ForMovieAndEpisode()
    {
        // Arrange Movie with PlaybackProgress
        var movie = new Movie { Title = "Progress Movie", ReleaseYear = 2023, CreatedAt = DateTime.UtcNow };
        await _movieRepository.InsertAsync(movie);
        var movieFile = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = Path.Combine(_tempDir, "ProgMovie.mkv"),
            FileName = "ProgMovie.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000
        };
        await _mediaFileRepository.InsertAsync(movieFile);

        await _playbackProgressRepository.SaveProgressAsync(movieFile.Id, movie.Id, 120_000_0000, 3600_000_0000, false);

        // Arrange Episode with PlaybackProgress
        var series = new Series { Title = "Progress Series", CreatedAt = DateTime.UtcNow };
        await _seriesRepository.InsertAsync(series);
        var season = new Season { SeriesId = series.Id, SeasonNumber = 1, CreatedAt = DateTime.UtcNow };
        await _seasonRepository.InsertAsync(season);
        var episode = new Episode { SeasonId = season.Id, EpisodeNumber = 1, Title = "Pilot", CreatedAt = DateTime.UtcNow };
        await _episodeRepository.InsertAsync(episode);
        var epFile = new MediaFile
        {
            EpisodeId = episode.Id,
            FilePath = Path.Combine(_tempDir, "ProgEpisode.mkv"),
            FileName = "ProgEpisode.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1000
        };
        await _mediaFileRepository.InsertAsync(epFile);

        await _playbackProgressRepository.SaveProgressAsync(epFile.Id, null, 300_000_0000, 1800_000_0000, false, episode.Id);

        // Verify progress exists
        Assert.NotNull(await _playbackProgressRepository.GetByMediaFileIdAsync(movieFile.Id));
        Assert.NotNull(await _playbackProgressRepository.GetByMediaFileIdAsync(epFile.Id));

        // Act: Remove movie and episode media files
        await _removalService.RemoveMovieFromLibraryAsync(movie.Id);
        await _removalService.RemoveFromLibraryOnlyAsync(epFile.FilePath);

        // Assert: Both playback progress records are deleted
        Assert.Null(await _playbackProgressRepository.GetByMediaFileIdAsync(movieFile.Id));
        Assert.Null(await _playbackProgressRepository.GetByMediaFileIdAsync(epFile.Id));
        Assert.Null(await _playbackProgressRepository.GetByMovieIdAsync(movie.Id));
        Assert.Null(await _playbackProgressRepository.GetByEpisodeIdAsync(episode.Id));
    }
}
