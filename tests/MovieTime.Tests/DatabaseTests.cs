using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Database;
using MovieTime.Core.Entities;
using MovieTime.Core.Extensions;
using MovieTime.Core.Services;
using SQLite;
using Xunit;

namespace MovieTime.Tests;

public sealed class DatabaseTests : IDisposable
{
    private readonly string _tempDirectory;
    private readonly string _databaseFilePath;
    private readonly IStoragePathService _storagePathService;
    private readonly ILogger<DatabaseService> _logger;

    public DatabaseTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), "MovieTimeDbTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDirectory);
        _databaseFilePath = Path.Combine(_tempDirectory, "test_movietime.db");

        _storagePathService = new StoragePathService(_tempDirectory);
        _logger = NullLogger<DatabaseService>.Instance;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDirectory))
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in temp directory
        }
    }

    private DatabaseService CreateDatabaseService()
    {
        return new DatabaseService(_storagePathService, _logger, _databaseFilePath);
    }

    [Fact]
    public async Task InitializeAsync_CreatesDatabaseFileAndSetsVersion()
    {
        await using var dbService = CreateDatabaseService();

        Assert.False(dbService.IsInitialized);
        Assert.Equal(0, dbService.CurrentSchemaVersion);

        await dbService.InitializeAsync();

        Assert.True(dbService.IsInitialized);
        Assert.Equal(2, dbService.CurrentSchemaVersion);
        Assert.True(File.Exists(_databaseFilePath));

        // Verify SQLite PRAGMAs directly on connection
        var conn = dbService.GetConnection();
        var userVersion = await conn.ExecuteScalarAsync<int>("PRAGMA user_version;");
        Assert.Equal(2, userVersion);

        var journalMode = await conn.ExecuteScalarAsync<string>("PRAGMA journal_mode;");
        Assert.Equal("wal", journalMode.ToLowerInvariant());
    }

    [Fact]
    public async Task InitializeAsync_CreatesAllRequiredTablesAndIndexes()
    {
        await using var dbService = CreateDatabaseService();
        await dbService.InitializeAsync();

        var conn = dbService.GetConnection();

        // Query sqlite_master for table definitions
        var tables = await conn.QueryScalarsAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%';"
        );

        Assert.Contains("Movies", tables);
        Assert.Contains("Series", tables);
        Assert.Contains("Seasons", tables);
        Assert.Contains("Episodes", tables);
        Assert.Contains("MediaFiles", tables);
        Assert.Contains("PlaybackProgress", tables);
        Assert.Contains("WatchHistory", tables);
        Assert.Contains("ScanRoots", tables);

        // Query sqlite_master for custom compound indices
        var indices = await conn.QueryScalarsAsync<string>(
            "SELECT name FROM sqlite_master WHERE type='index' AND name NOT LIKE 'sqlite_%';"
        );

        Assert.Contains("IX_Seasons_Series_Number", indices);
        Assert.Contains("IX_Episodes_Series_Season_Number", indices);
        Assert.Contains("IX_MediaFiles_MovieId", indices);
        Assert.Contains("IX_MediaFiles_EpisodeId", indices);
        Assert.Contains("IX_MediaFiles_IsMissing", indices);
        Assert.Contains("IX_ScanRoots_NormalizedPath", indices);
    }

    [Fact]
    public async Task InitializeAsync_CalledMultipleTimes_IsIdempotent()
    {
        await using var dbService = CreateDatabaseService();

        // Call twice sequentially
        await dbService.InitializeAsync();
        await dbService.InitializeAsync();

        Assert.True(dbService.IsInitialized);
        Assert.Equal(2, dbService.CurrentSchemaVersion);
    }

    [Fact]
    public async Task InitializeAsync_ConcurrentCalls_AreSafeAndExecuteOnce()
    {
        await using var dbService = CreateDatabaseService();

        // Launch 10 simultaneous initialization tasks
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => Task.Run(() => dbService.InitializeAsync()))
            .ToArray();

        await Task.WhenAll(tasks);

        Assert.True(dbService.IsInitialized);
        Assert.Equal(2, dbService.CurrentSchemaVersion);
    }

    [Fact]
    public async Task CrudOperations_Movie_InsertRetrieveUpdateDelete()
    {
        await using var dbService = CreateDatabaseService();
        await dbService.InitializeAsync();

        var movie = new Movie
        {
            Title = "Inception",
            SortTitle = "Inception",
            ReleaseYear = 2010,
            Edition = "Theatrical",
            PosterPath = @"Artwork\Posters\inception.jpg",
            BackdropPath = @"Artwork\Backdrops\inception.jpg",
            IsPosterFallback = false,
            IsBackdropFallback = false
        };

        // 1. Insert
        var insertedRows = await dbService.InsertAsync(movie);
        Assert.Equal(1, insertedRows);
        Assert.True(movie.Id > 0);

        // 2. Retrieve by ID
        var retrieved = await dbService.GetByIdAsync<Movie>(movie.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Inception", retrieved.Title);
        Assert.Equal(2010, retrieved.ReleaseYear);

        // 3. Update
        retrieved.Edition = "10th Anniversary Remaster";
        var updatedRows = await dbService.UpdateAsync(retrieved);
        Assert.Equal(1, updatedRows);

        var updated = await dbService.GetByIdAsync<Movie>(movie.Id);
        Assert.NotNull(updated);
        Assert.Equal("10th Anniversary Remaster", updated.Edition);

        // 4. Delete
        var deletedRows = await dbService.DeleteAsync(updated);
        Assert.Equal(1, deletedRows);

        var deleted = await dbService.GetByIdAsync<Movie>(movie.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task CrudOperations_SeriesSeasonEpisode_RelationshipConsistency()
    {
        await using var dbService = CreateDatabaseService();
        await dbService.InitializeAsync();

        // Insert Series
        var series = new Series
        {
            Title = "Severance",
            SortTitle = "Severance",
            StartYear = 2022
        };
        await dbService.InsertAsync(series);
        Assert.True(series.Id > 0);

        // Insert Season
        var season = new Season
        {
            SeriesId = series.Id,
            SeasonNumber = 1,
            Title = "Season 1"
        };
        await dbService.InsertAsync(season);
        Assert.True(season.Id > 0);

        // Insert Episode
        var episode1 = new Episode
        {
            SeriesId = series.Id,
            SeasonId = season.Id,
            EpisodeNumber = 1,
            Title = "Good News About Hell"
        };
        var episode2 = new Episode
        {
            SeriesId = series.Id,
            SeasonId = season.Id,
            EpisodeNumber = 2,
            Title = "Half Loop"
        };
        await dbService.InsertAsync(episode1);
        await dbService.InsertAsync(episode2);

        // Query episodes for season
        var episodes = await dbService.Table<Episode>()
            .Where(e => e.SeriesId == series.Id && e.SeasonId == season.Id)
            .OrderBy(e => e.EpisodeNumber)
            .ToListAsync();

        Assert.Equal(2, episodes.Count);
        Assert.Equal("Good News About Hell", episodes[0].Title);
        Assert.Equal("Half Loop", episodes[1].Title);
    }

    [Fact]
    public async Task CrudOperations_MediaFile_SupportsMultipleVersionsPerMovie()
    {
        await using var dbService = CreateDatabaseService();
        await dbService.InitializeAsync();

        var movie = new Movie { Title = "Interstellar", ReleaseYear = 2014 };
        await dbService.InsertAsync(movie);

        // Two versions of the same movie: 1080p and 4K HDR
        var file1080P = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = @"C:\Media\Movies\Interstellar.2014.1080p.mkv",
            FileName = "Interstellar.2014.1080p.mkv",
            FileExtension = ".mkv",
            SizeBytes = 8_000_000_000,
            VideoCodec = "H.264",
            VideoWidth = 1920,
            VideoHeight = 1080,
            AudioCodec = "DTS"
        };

        var file4K = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = @"C:\Media\Movies\Interstellar.2014.2160p.UHD.mkv",
            FileName = "Interstellar.2014.2160p.UHD.mkv",
            FileExtension = ".mkv",
            SizeBytes = 45_000_000_000,
            VideoCodec = "HEVC",
            VideoWidth = 3840,
            VideoHeight = 2160,
            AudioCodec = "TrueHD Atmos"
        };

        await dbService.InsertAsync(file1080P);
        await dbService.InsertAsync(file4K);

        var movieFiles = await dbService.Table<MediaFile>()
            .Where(f => f.MovieId == movie.Id)
            .ToListAsync();

        Assert.Equal(2, movieFiles.Count);
        Assert.Contains(movieFiles, f => f.VideoWidth == 1920);
        Assert.Contains(movieFiles, f => f.VideoWidth == 3840);
    }

    [Fact]
    public async Task CrudOperations_PlaybackProgressAndWatchHistory()
    {
        await using var dbService = CreateDatabaseService();
        await dbService.InitializeAsync();

        var mediaFile = new MediaFile
        {
            FilePath = @"C:\Media\Movies\Dune.mkv",
            FileName = "Dune.mkv",
            FileExtension = ".mkv",
            SizeBytes = 12_000_000_000
        };
        await dbService.InsertAsync(mediaFile);

        // 1. Record progress at 50%
        var progress = new PlaybackProgress
        {
            MediaFileId = mediaFile.Id,
            PositionTicks = TimeSpan.FromMinutes(75).Ticks,
            DurationTicks = TimeSpan.FromMinutes(150).Ticks,
            PositionPercentage = 0.50,
            IsFinished = false,
            LastWatchedAt = DateTime.UtcNow,
            PlayCount = 1
        };
        await dbService.InsertAsync(progress);

        var retrievedProgress = await dbService.Table<PlaybackProgress>()
            .Where(p => p.MediaFileId == mediaFile.Id)
            .FirstOrDefaultAsync();

        Assert.NotNull(retrievedProgress);
        Assert.Equal(0.50, retrievedProgress.PositionPercentage);
        Assert.False(retrievedProgress.IsFinished);

        // 2. Update progress to finished (e.g. 95%)
        retrievedProgress.PositionPercentage = 0.95;
        retrievedProgress.IsFinished = true;
        await dbService.UpdateAsync(retrievedProgress);

        // 3. Record watch history session
        var history = new WatchHistory
        {
            MediaFileId = mediaFile.Id,
            StartedAt = DateTime.UtcNow.AddHours(-3),
            CompletedAt = DateTime.UtcNow,
            WatchedDurationTicks = TimeSpan.FromMinutes(145).Ticks,
            StoppedPositionTicks = TimeSpan.FromMinutes(145).Ticks,
            WasCompleted = true
        };
        await dbService.InsertAsync(history);

        var retrievedHistory = await dbService.GetByIdAsync<WatchHistory>(history.Id);
        Assert.NotNull(retrievedHistory);
        Assert.True(retrievedHistory.WasCompleted);
    }

    [Fact]
    public async Task Persistence_DataPersistsAcrossDatabaseReopen()
    {
        // 1. First connection: write data
        await using (var dbService1 = CreateDatabaseService())
        {
            await dbService1.InitializeAsync();

            var movie = new Movie { Title = "Blade Runner 2049", ReleaseYear = 2017 };
            await dbService1.InsertAsync(movie);
        }

        // 2. Second connection: reopen same database file and verify data
        await using (var dbService2 = CreateDatabaseService())
        {
            await dbService2.InitializeAsync();

            Assert.True(dbService2.IsInitialized);
            Assert.Equal(2, dbService2.CurrentSchemaVersion);

            var movies = await dbService2.GetAllAsync<Movie>();
            Assert.Single(movies);
            Assert.Equal("Blade Runner 2049", movies[0].Title);
            Assert.Equal(2017, movies[0].ReleaseYear);
        }
    }

    [Fact]
    public async Task Migration_FromVersion1ToVersion2_UpgradesCleanlyAndPreservesData()
    {
        var rawDbPath = Path.Combine(_tempDirectory, "v1_to_v2_migration.db");

        // 1. Manually create an authentic version 1 schema (before ScanRoots and IsMissing existed)
        SQLitePCL.Batteries_V2.Init();
        var rawConn = new SQLiteAsyncConnection(rawDbPath, SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex, storeDateTimeAsTicks: true);
        
        await rawConn.ExecuteAsync(@"
            CREATE TABLE IF NOT EXISTS MediaFiles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MovieId INTEGER,
                EpisodeId INTEGER,
                FilePath TEXT NOT NULL UNIQUE,
                FileName TEXT NOT NULL,
                FileExtension TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                DurationTicks INTEGER NOT NULL DEFAULT 0,
                LastScannedAt INTEGER NOT NULL
            );");
        await rawConn.ExecuteAsync("PRAGMA user_version = 1;");

        // Insert a record into MediaFiles using raw SQL in authentic v1 state
        await rawConn.ExecuteAsync(
            "INSERT INTO MediaFiles (FilePath, FileName, FileExtension, SizeBytes, LastScannedAt) VALUES (?, ?, ?, ?, ?);",
            @"C:\Movies\TestMovie.mkv", "TestMovie.mkv", ".mkv", 1024, DateTime.UtcNow.Ticks);

        await rawConn.CloseAsync();

        // 2. Open with DatabaseService, which will run SchemaVersion2Migration
        await using var dbService = new DatabaseService(_storagePathService, _logger, rawDbPath);
        await dbService.InitializeAsync();

        // 3. Verify upgraded to version 2
        Assert.Equal(2, dbService.CurrentSchemaVersion);

        // Verify ScanRoots table now exists
        var conn = dbService.GetConnection();
        var tables = await conn.QueryScalarsAsync<string>("SELECT name FROM sqlite_master WHERE type='table' AND name='ScanRoots';");
        Assert.Contains("ScanRoots", tables);

        // Verify existing MediaFile record was preserved and defaulted properly
        var mediaFiles = await dbService.GetAllAsync<MediaFile>();
        Assert.Single(mediaFiles);
        Assert.Equal(@"C:\Movies\TestMovie.mkv", mediaFiles[0].FilePath);
        Assert.False(mediaFiles[0].IsMissing);
        Assert.Null(mediaFiles[0].ScanRootId);
    }

    [Fact]
    public async Task DependencyInjection_ResolvesDatabaseService()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMovieTimeCore(customStorageRoot: _tempDirectory);

        var provider = services.BuildServiceProvider();
        var dbService = provider.GetRequiredService<IDatabaseService>();

        Assert.NotNull(dbService);
        Assert.Equal(Path.Combine(_tempDirectory, "Database", "movietime.db"), dbService.DatabasePath);

        await dbService.InitializeAsync();
        Assert.True(dbService.IsInitialized);
    }
}
