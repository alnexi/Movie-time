using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Library;
using MovieTime.Core.Library.Models;
using MovieTime.Core.Parsing;
using MovieTime.Core.Scanning;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class LiveLibraryVerificationTests
{
    [Fact]
    public async Task Live_VerifyScanAndImportOnRealMLibrary()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dbPath = Path.Combine(localAppData, "MovieTime", "Database", "movietime.db");

        if (!File.Exists(dbPath))
        {
            // If live DB doesn't exist, skip gracefully
            return;
        }

        var storagePathService = new StoragePathService();
        var databaseService = new DatabaseService(storagePathService, NullLogger<DatabaseService>.Instance);
        await databaseService.InitializeAsync();

        var scanRootRepo = new ScanRootRepository(databaseService);
        var movieRepo = new MovieRepository(databaseService);
        var mediaFileRepo = new MediaFileRepository(databaseService);
        var seriesRepo = new SeriesRepository(databaseService);
        var seasonRepo = new SeasonRepository(databaseService);
        var episodeRepo = new EpisodeRepository(databaseService);

        var scanRootService = new ScanRootService(scanRootRepo, NullLogger<ScanRootService>.Instance);
        var scannerService = new MediaScannerService(NullLogger<MediaScannerService>.Instance);
        var parserEngine = new FilenameParserEngine(NullLogger<FilenameParserEngine>.Instance, storagePathService);

        var importService = new LibraryImportService(
            scanRootService,
            scannerService,
            parserEngine,
            databaseService,
            movieRepo,
            seriesRepo,
            seasonRepo,
            episodeRepo,
            mediaFileRepo,
            NullLogger<LibraryImportService>.Instance
        );

        // Run real scan with ForceReparse = true to re-evaluate and re-classify all files
        var options = new LibraryImportOptions { ForceReparse = true };
        var result = await importService.ImportAsync(options);

        // Print diagnostic summary
        var summary = result.FormatDiagnosticSummary();
        Console.WriteLine(summary);

        // Verify that movies are classified and imported
        Assert.True(result.FilesDiscovered > 0, "Real scan should discover files in MLibrary");
        Assert.True(result.MoviesClassified > 0, "Movies should be classified");
        Assert.True(result.TvEpisodesClassified > 0, "TV episodes should be classified");

        var allMovies = await movieRepo.GetAllAsync();
        Assert.True(allMovies.Count >= 2, "All movies should be present in database");
    }
}
