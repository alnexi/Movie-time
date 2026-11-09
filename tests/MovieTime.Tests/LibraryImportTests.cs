using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
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

public sealed class LibraryImportTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _databaseService;
    private readonly ScanRootRepository _scanRootRepository;
    private readonly MovieRepository _movieRepository;
    private readonly SeriesRepository _seriesRepository;
    private readonly SeasonRepository _seasonRepository;
    private readonly EpisodeRepository _episodeRepository;
    private readonly MediaFileRepository _mediaFileRepository;
    private readonly ScanRootService _scanRootService;
    private readonly MediaScannerService _scannerService;
    private readonly FilenameParserEngine _parserEngine;
    private readonly LibraryImportService _importService;

    public LibraryImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "LibraryImportTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_import.db");

        var storage = new StoragePathService(_tempDir);
        _databaseService = new DatabaseService(storage, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _scanRootRepository = new ScanRootRepository(_databaseService);
        _movieRepository = new MovieRepository(_databaseService);
        _seriesRepository = new SeriesRepository(_databaseService);
        _seasonRepository = new SeasonRepository(_databaseService);
        _episodeRepository = new EpisodeRepository(_databaseService);
        _mediaFileRepository = new MediaFileRepository(_databaseService);

        _scanRootService = new ScanRootService(_scanRootRepository, NullLogger<ScanRootService>.Instance);
        _scannerService = new MediaScannerService(NullLogger<MediaScannerService>.Instance);
        _parserEngine = new FilenameParserEngine(NullLogger<FilenameParserEngine>.Instance, storage);

        _importService = new LibraryImportService(
            _scanRootService,
            _scannerService,
            _parserEngine,
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

    private void CreateDummyVideoFile(string relativePath, int sizeBytes = 1024)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var bytes = new byte[sizeBytes];
        File.WriteAllBytes(fullPath, bytes);
    }

    #region Category 1: Initial Import of Movies and TV

    [Fact]
    public async Task ImportAsync_InitialMovieImport_CreatesMovieAndMediaFileRecords()
    {
        var moviesDir = Path.Combine(_tempDir, "Movies");
        CreateDummyVideoFile(Path.Combine("Movies", "The.Dark.Knight.2008.1080p.mkv"));
        CreateDummyVideoFile(Path.Combine("Movies", "Inception.2010.720p.mp4"));

        await _scanRootService.AddRootAsync(moviesDir);

        var result = await _importService.ImportAsync();

        Assert.Equal(2, result.FilesDiscovered);
        Assert.Equal(2, result.FilesImported);
        Assert.Equal(2, result.MoviesCreated);
        Assert.Equal(0, result.FilesUnchanged);
        Assert.Equal(0, result.FilesMissing);

        var allMovies = await _movieRepository.GetAllAsync();
        Assert.Equal(2, allMovies.Count);

        var tdk = allMovies.FirstOrDefault(m => m.Title == "The Dark Knight");
        Assert.NotNull(tdk);
        Assert.Equal(2008, tdk.ReleaseYear);

        var allFiles = await _mediaFileRepository.GetAllAsync();
        Assert.Equal(2, allFiles.Count);
        Assert.All(allFiles, f => Assert.False(f.IsMissing));
        Assert.All(allFiles, f => Assert.NotNull(f.MovieId));
    }

    [Fact]
    public async Task ImportAsync_InitialTvEpisodes_CreatesSeriesSeasonEpisodeHierarchy()
    {
        var tvDir = Path.Combine(_tempDir, "TV_Shows");
        CreateDummyVideoFile(Path.Combine("TV_Shows", "Breaking.Bad.S01E01.Pilot.mkv"));
        CreateDummyVideoFile(Path.Combine("TV_Shows", "Breaking.Bad.S01E02.Cat's.in.the.Bag.mkv"));

        await _scanRootService.AddRootAsync(tvDir);

        var result = await _importService.ImportAsync();

        Assert.Equal(2, result.FilesDiscovered);
        Assert.Equal(2, result.FilesImported);
        Assert.Equal(1, result.SeriesCreated);
        Assert.Equal(2, result.EpisodesCreated);

        var allSeries = await _seriesRepository.GetAllAsync();
        Assert.Single(allSeries);
        Assert.Equal("Breaking Bad", allSeries[0].Title);

        var seasons = await _seasonRepository.GetBySeriesIdAsync(allSeries[0].Id);
        Assert.Single(seasons);
        Assert.Equal(1, seasons[0].SeasonNumber);

        var episodes = await _episodeRepository.GetBySeasonIdAsync(seasons[0].Id);
        Assert.Equal(2, episodes.Count);
        Assert.Equal(1, episodes[0].EpisodeNumber);
        Assert.Equal(2, episodes[1].EpisodeNumber);

        var mediaFiles = await _mediaFileRepository.GetAllAsync();
        Assert.Equal(2, mediaFiles.Count);
        Assert.All(mediaFiles, f => Assert.NotNull(f.EpisodeId));
        Assert.All(mediaFiles, f => Assert.Null(f.MovieId));
    }

    [Fact]
    public async Task ImportAsync_FolderGuidedTvEpisodes_ResolvesSeriesAndSeasonFromDirectories()
    {
        var tvDir = Path.Combine(_tempDir, "Library_TV");
        CreateDummyVideoFile(Path.Combine("Library_TV", "Severance", "Season 01", "01 - Good News About Hell.mkv"));

        await _scanRootService.AddRootAsync(tvDir);

        var result = await _importService.ImportAsync();

        Assert.Equal(1, result.FilesImported);
        Assert.Equal(1, result.SeriesCreated);

        var series = await _seriesRepository.FindByTitleAsync("Severance");
        Assert.NotNull(series);

        var season = await _seasonRepository.GetBySeriesAndNumberAsync(series.Id, 1);
        Assert.NotNull(season);

        var episode = await _episodeRepository.GetBySeasonAndNumberAsync(season.Id, 1);
        Assert.NotNull(episode);
        Assert.Equal("Good News About Hell", episode.Title);
    }

    [Fact]
    public async Task ImportAsync_UnknownFiles_AreStoredSafelyWithoutFalseClassification()
    {
        var mediaDir = Path.Combine(_tempDir, "Mixed_Media");
        CreateDummyVideoFile(Path.Combine("Mixed_Media", "random_clip_001.mp4"));

        await _scanRootService.AddRootAsync(mediaDir);

        var result = await _importService.ImportAsync();

        Assert.Equal(1, result.FilesDiscovered);
        Assert.Equal(1, result.FilesImported);
        Assert.Equal(1, result.UnknownFiles);
        Assert.Equal(0, result.MoviesCreated);
        Assert.Equal(0, result.SeriesCreated);

        var files = await _mediaFileRepository.GetAllAsync();
        Assert.Single(files);
        Assert.Null(files[0].MovieId);
        Assert.Null(files[0].EpisodeId);
        Assert.Equal("random_clip_001.mp4", files[0].FileName);
        Assert.False(files[0].IsMissing);
    }

    [Fact]
    public async Task ImportAsync_MultipleFilesForOneMovie_AssociatesBothToSingleMovieRecord()
    {
        var moviesDir = Path.Combine(_tempDir, "Movie_Versions");
        CreateDummyVideoFile(Path.Combine("Movie_Versions", "Gladiator.2000.1080p.BluRay.mkv"), sizeBytes: 2048);
        CreateDummyVideoFile(Path.Combine("Movie_Versions", "Gladiator.2000.2160p.UHD.mkv"), sizeBytes: 4096);

        await _scanRootService.AddRootAsync(moviesDir);

        var result = await _importService.ImportAsync();

        Assert.Equal(2, result.FilesDiscovered);
        Assert.Equal(2, result.FilesImported);
        Assert.Equal(1, result.MoviesCreated);

        var movies = await _movieRepository.GetAllAsync();
        Assert.Single(movies);
        Assert.Equal("Gladiator", movies[0].Title);

        var files = await _mediaFileRepository.GetByMovieIdAsync(movies[0].Id);
        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.FileName.Contains("1080p"));
        Assert.Contains(files, f => f.FileName.Contains("2160p"));
    }

    #endregion

    #region Category 2: Idempotency, Updates & Missing File Reconciliation

    [Fact]
    public async Task ImportAsync_RepeatedScan_IsIdempotentAndReportsUnchangedFiles()
    {
        var moviesDir = Path.Combine(_tempDir, "Idempotency_Test");
        CreateDummyVideoFile(Path.Combine("Idempotency_Test", "Interstellar.2014.1080p.mkv"));

        await _scanRootService.AddRootAsync(moviesDir);

        // Run 1
        var result1 = await _importService.ImportAsync();
        Assert.Equal(1, result1.FilesImported);
        Assert.Equal(1, result1.MoviesCreated);

        // Run 2: Exact same files, no modifications
        var result2 = await _importService.ImportAsync();
        Assert.Equal(1, result2.FilesDiscovered);
        Assert.Equal(0, result2.FilesImported);
        Assert.Equal(0, result2.MoviesCreated);
        Assert.Equal(1, result2.FilesUnchanged);
        Assert.Equal(0, result2.FilesMissing);

        var allMovies = await _movieRepository.GetAllAsync();
        Assert.Single(allMovies);
    }

    [Fact]
    public async Task ImportAsync_FileModifiedOnDisk_UpdatesMetadataAndReportsUpdated()
    {
        var moviesDir = Path.Combine(_tempDir, "Update_Test");
        var filePath = Path.Combine(moviesDir, "Dune.2021.1080p.mkv");
        CreateDummyVideoFile(Path.Combine("Update_Test", "Dune.2021.1080p.mkv"), sizeBytes: 1000);

        await _scanRootService.AddRootAsync(moviesDir);

        // Initial scan
        var result1 = await _importService.ImportAsync();
        Assert.Equal(1, result1.FilesImported);

        // Modify file on disk (change size and timestamp)
        File.WriteAllBytes(filePath, new byte[2500]);
        File.SetLastWriteTimeUtc(filePath, DateTime.UtcNow.AddMinutes(5));

        // Second scan
        var result2 = await _importService.ImportAsync();
        Assert.Equal(1, result2.FilesUpdated);
        Assert.Equal(0, result2.FilesImported);

        var mediaFile = await _mediaFileRepository.GetByPathAsync(Path.GetFullPath(filePath));
        Assert.NotNull(mediaFile);
        Assert.Equal(2500, mediaFile.SizeBytes);
    }

    [Fact]
    public async Task ImportAsync_MissingFileReconciliation_MarksMissingWithoutDeletingRecords()
    {
        var moviesDir = Path.Combine(_tempDir, "Reconciliation_Test");
        var fileA = Path.Combine(moviesDir, "MovieA.2020.mkv");
        var fileB = Path.Combine(moviesDir, "MovieB.2021.mkv");

        CreateDummyVideoFile(Path.Combine("Reconciliation_Test", "MovieA.2020.mkv"));
        CreateDummyVideoFile(Path.Combine("Reconciliation_Test", "MovieB.2021.mkv"));

        await _scanRootService.AddRootAsync(moviesDir);

        // Initial scan: both present
        var result1 = await _importService.ImportAsync();
        Assert.Equal(2, result1.FilesImported);
        Assert.Equal(2, result1.MoviesCreated);

        // Delete MovieB from disk
        File.Delete(fileB);

        // Completed scan: MovieB is now missing
        var result2 = await _importService.ImportAsync();
        Assert.Equal(1, result2.FilesDiscovered);
        Assert.Equal(1, result2.FilesUnchanged);
        Assert.Equal(1, result2.FilesMissing);

        // Verify database state: Movie record is NOT deleted, MediaFile has IsMissing = true
        var movies = await _movieRepository.GetAllAsync();
        Assert.Equal(2, movies.Count); // Movie record preserved!

        var mediaA = await _mediaFileRepository.GetByPathAsync(Path.GetFullPath(fileA));
        Assert.NotNull(mediaA);
        Assert.False(mediaA.IsMissing);

        var mediaB = await _mediaFileRepository.GetByPathAsync(Path.GetFullPath(fileB));
        Assert.NotNull(mediaB);
        Assert.True(mediaB.IsMissing);

        // Recreate MovieB on disk and scan again: should be restored to available
        CreateDummyVideoFile(Path.Combine("Reconciliation_Test", "MovieB.2021.mkv"));
        var result3 = await _importService.ImportAsync();

        Assert.Equal(2, result3.FilesDiscovered);
        var restoredB = await _mediaFileRepository.GetByPathAsync(Path.GetFullPath(fileB));
        Assert.NotNull(restoredB);
        Assert.False(restoredB.IsMissing);
    }

    [Fact]
    public async Task ImportAsync_CancelledScan_PreservesPreviousStateAndDoesNotMarkMissing()
    {
        var moviesDir = Path.Combine(_tempDir, "Cancel_Test");
        CreateDummyVideoFile(Path.Combine("Cancel_Test", "Film.2018.mkv"));

        await _scanRootService.AddRootAsync(moviesDir);
        await _importService.ImportAsync();

        // Run second scan with an already cancelled token
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var result = await _importService.ImportAsync(cancellationToken: cts.Token);

        Assert.True(result.IsCancelled);
        Assert.Equal(0, result.FilesMissing);

        var files = await _mediaFileRepository.GetAllAsync();
        Assert.Single(files);
        Assert.False(files[0].IsMissing);
    }

    [Fact]
    public async Task ImportAsync_InaccessibleRoot_SkipsGracefullyAndDoesNotMarkMissing()
    {
        var realDir = Path.Combine(_tempDir, "Real_Dir");
        CreateDummyVideoFile(Path.Combine("Real_Dir", "RealMovie.2019.mkv"));
        var root1 = await _scanRootService.AddRootAsync(realDir);

        await _importService.ImportAsync();

        // Add a root that physically does not exist (e.g. unplugged external drive)
        var fakeDir = Path.Combine(_tempDir, "Unplugged_Drive_Folder");
        await _scanRootRepository.InsertAsync(new ScanRoot
        {
            Path = fakeDir,
            NormalizedPath = Path.GetFullPath(fakeDir),
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        });

        // Scan both roots
        var result = await _importService.ImportAsync();

        // Inaccessible root was recorded as an error and skipped
        Assert.Contains(result.Errors, e => e.Path == fakeDir);
        Assert.Equal(0, result.FilesMissing);

        // Existing file is still present
        var existingFile = (await _mediaFileRepository.GetAllAsync()).First();
        Assert.False(existingFile.IsMissing);
    }

    [Fact]
    public async Task ImportAsync_OverlappingRoots_DoesNotProduceDuplicateImports()
    {
        var parentDir = Path.Combine(_tempDir, "Library_Parent");
        var subDir = Path.Combine(parentDir, "Sub_Movies");

        CreateDummyVideoFile(Path.Combine("Library_Parent", "Sub_Movies", "The.Matrix.1999.1080p.mkv"));

        // User configures both parent and child directory as roots
        await _scanRootService.AddRootAsync(parentDir);
        await _scanRootService.AddRootAsync(subDir);

        var result = await _importService.ImportAsync();

        // File must only be discovered and imported once
        Assert.Equal(1, result.FilesDiscovered);
        Assert.Equal(1, result.FilesImported);
        Assert.Equal(1, result.MoviesCreated);

        var movies = await _movieRepository.GetAllAsync();
        Assert.Single(movies);

        var mediaFiles = await _mediaFileRepository.GetAllAsync();
        Assert.Single(mediaFiles);
    }

    [Fact]
    public async Task ImportAsync_PreservesPlaybackProgressAcrossScans()
    {
        var moviesDir = Path.Combine(_tempDir, "Progress_Test");
        CreateDummyVideoFile(Path.Combine("Progress_Test", "Memento.2000.1080p.mkv"));

        await _scanRootService.AddRootAsync(moviesDir);
        await _importService.ImportAsync();

        var mediaFile = (await _mediaFileRepository.GetAllAsync()).First();

        // Insert playback progress
        var progress = new PlaybackProgress
        {
            MediaFileId = mediaFile.Id,
            MovieId = mediaFile.MovieId,
            PositionTicks = TimeSpan.FromMinutes(45).Ticks,
            DurationTicks = TimeSpan.FromMinutes(90).Ticks,
            PositionPercentage = 0.50,
            IsFinished = false,
            LastWatchedAt = DateTime.UtcNow,
            PlayCount = 1
        };
        await _databaseService.InsertAsync(progress);

        // Run another import
        var scanResult = await _importService.ImportAsync();
        Assert.Equal(1, scanResult.FilesUnchanged);

        // Playback progress is completely preserved
        var savedProgress = await _databaseService.Table<PlaybackProgress>()
            .Where(p => p.MediaFileId == mediaFile.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(savedProgress);
        Assert.Equal(0.50, savedProgress.PositionPercentage);
        Assert.Equal(progress.PositionTicks, savedProgress.PositionTicks);
    }

    [Fact]
    public async Task RemoveRoot_PreservesMediaRecordsAndUserData()
    {
        var moviesDir = Path.Combine(_tempDir, "Removal_Test");
        CreateDummyVideoFile(Path.Combine("Removal_Test", "Alien.1979.mkv"));

        var root = await _scanRootService.AddRootAsync(moviesDir);
        await _importService.ImportAsync();

        // Remove root
        var removed = await _scanRootService.RemoveRootAsync(root.Id);
        Assert.True(removed);

        // Media records and movie remain intact
        var movies = await _movieRepository.GetAllAsync();
        Assert.Single(movies);
        Assert.Equal("Alien", movies[0].Title);

        var files = await _mediaFileRepository.GetAllAsync();
        Assert.Single(files);
        Assert.Equal("Alien.1979.mkv", files[0].FileName);
    }

    [Fact]
    public async Task ImportAsync_MoviesWithoutYears_AreSuccessfullyImportedAndLinked()
    {
        var moviesDir = Path.Combine(_tempDir, "Movies_No_Year");
        CreateDummyVideoFile(Path.Combine("Movies_No_Year", "Gladiator.mkv"));
        CreateDummyVideoFile(Path.Combine("Movies_No_Year", "The Dark Knight.mp4"));
        CreateDummyVideoFile(Path.Combine("Movies_No_Year", "Inception.mkv"));

        await _scanRootService.AddRootAsync(moviesDir);
        var result = await _importService.ImportAsync();

        Assert.Equal(3, result.FilesDiscovered);
        Assert.Equal(3, result.FilesImported);
        Assert.Equal(3, result.MoviesClassified);
        Assert.Equal(3, result.MoviesCreated);
        Assert.Equal(0, result.UnknownFiles);

        var movies = await _movieRepository.GetAllAsync();
        Assert.Equal(3, movies.Count);
        Assert.Contains(movies, m => m.Title == "Gladiator" && m.ReleaseYear == null);
        Assert.Contains(movies, m => m.Title == "The Dark Knight" && m.ReleaseYear == null);
        Assert.Contains(movies, m => m.Title == "Inception" && m.ReleaseYear == null);

        var files = await _mediaFileRepository.GetAllAsync();
        Assert.Equal(3, files.Count);
        Assert.All(files, f => Assert.NotNull(f.MovieId));
    }

    [Fact]
    public async Task ImportAsync_PreviouslyUnlinkedFiles_AreReEvaluatedAndLinkedOnSubsequentScan()
    {
        var moviesDir = Path.Combine(_tempDir, "Unlinked_Reevaluation");
        var filePath = Path.Combine(_tempDir, "Unlinked_Reevaluation", "Gladiator.mkv");
        CreateDummyVideoFile(Path.Combine("Unlinked_Reevaluation", "Gladiator.mkv"));
        var root = await _scanRootService.AddRootAsync(moviesDir);

        // Simulate an existing media file that was previously saved with MovieId = null
        var unlinkedFile = new MovieTime.Core.Entities.MediaFile
        {
            ScanRootId = root.Id,
            FilePath = filePath,
            FileName = "Gladiator.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1024,
            FileCreatedAt = DateTime.UtcNow,
            FileModifiedAt = DateTime.UtcNow,
            LastScannedAt = DateTime.UtcNow,
            MovieId = null,
            EpisodeId = null,
            IsMissing = false
        };
        await _mediaFileRepository.InsertAsync(unlinkedFile);

        // Run import - the unlinked file must be re-evaluated and linked to a Movie entity
        var result = await _importService.ImportAsync();

        Assert.Equal(1, result.FilesDiscovered);
        Assert.Equal(1, result.FilesUpdated);
        Assert.Equal(1, result.MoviesClassified);
        Assert.Equal(1, result.MoviesCreated);

        var movies = await _movieRepository.GetAllAsync();
        Assert.Single(movies);
        Assert.Equal("Gladiator", movies[0].Title);

        var updatedFile = await _mediaFileRepository.GetByPathAsync(filePath);
        Assert.NotNull(updatedFile);
        Assert.Equal(movies[0].Id, updatedFile.MovieId);
    }

    [Fact]
    public async Task ImportAsync_DiagnosticReport_TracksAllClassificationsAndDiagnosticsAccurately()
    {
        var mixedDir = Path.Combine(_tempDir, "Diagnostic_Mixed");
        CreateDummyVideoFile(Path.Combine("Diagnostic_Mixed", "Interstellar.2014.1080p.mkv"));
        CreateDummyVideoFile(Path.Combine("Diagnostic_Mixed", "The.Wire.S01E08.Lessons.720p.mkv"));
        CreateDummyVideoFile(Path.Combine("Diagnostic_Mixed", "sample.mkv"));

        await _scanRootService.AddRootAsync(mixedDir);
        var result = await _importService.ImportAsync();

        Assert.Equal(3, result.FilesDiscovered);
        Assert.Equal(1, result.MoviesClassified);
        Assert.Equal(1, result.TvEpisodesClassified);
        Assert.Equal(1, result.UnknownFiles);
        Assert.Single(result.Diagnostics);
        Assert.Equal("sample.mkv", result.Diagnostics[0].FileName);
        Assert.Equal("Unknown", result.Diagnostics[0].Classification);
        Assert.Contains("Sample", result.Diagnostics[0].Reason);

        var summary = result.FormatDiagnosticSummary();
        Assert.Contains("Movies Classified: 1", summary);
        Assert.Contains("TV Episodes Classified: 1", summary);
        Assert.Contains("Unknown: 1", summary);
    }

    #endregion
}
