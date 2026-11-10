using System;
using System.IO;
using System.Linq;
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

public sealed class MissingMediaReconciliationTests : IDisposable
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

    public MissingMediaReconciliationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ReconciliationTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_recon.db");

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
            null,
            NullLogger<LibraryImportService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            _databaseService.Dispose();
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore temp file cleanup exceptions in tests
        }
    }

    [Fact]
    public async Task MovedOrRenamedFile_PreservesMediaFileIdAndUpdatesPath()
    {
        // 1. Create a media folder and add a file
        var rootDir = Path.Combine(_tempDir, "MLibrary");
        Directory.CreateDirectory(rootDir);
        var initialPath = Path.Combine(rootDir, "Inception.2010.mkv");
        File.WriteAllText(initialPath, "Dummy Inception Video Stream Bytes 1234567890");

        // 2. Register root and import initial state
        await _scanRootService.AddRootAsync(rootDir);
        var res1 = await _importService.ImportAsync();
        Assert.Equal(1, res1.FilesImported);

        var allMediaFiles = await _mediaFileRepository.GetAllAsync();
        Assert.Single(allMediaFiles);
        var originalMediaFile = allMediaFiles[0];
        Assert.Equal("Inception.2010.mkv", originalMediaFile.FileName);
        Assert.False(originalMediaFile.IsMissing);

        var originalMovies = await _movieRepository.GetAllAsync();
        Assert.Single(originalMovies);
        var originalMovie = originalMovies[0];

        // 3. Rename/move the file within the root (keep content and timestamp unchanged)
        var newSubDir = Path.Combine(rootDir, "Sci-Fi");
        Directory.CreateDirectory(newSubDir);
        var renamedPath = Path.Combine(newSubDir, "Inception (2010) [1080p].mkv");
        File.Move(initialPath, renamedPath);

        // 4. Run second import: must detect move/rename without duplicating movie entity
        var res2 = await _importService.ImportAsync();
        Assert.Equal(1, res2.FilesMoved);

        var updatedFiles = await _mediaFileRepository.GetAllAsync();
        Assert.Single(updatedFiles);
        var updatedFile = updatedFiles[0];

        // Entity ID must be preserved
        Assert.Equal(originalMediaFile.Id, updatedFile.Id);
        Assert.Equal(Path.GetFullPath(renamedPath), updatedFile.FilePath);
        Assert.Equal("Inception (2010) [1080p].mkv", updatedFile.FileName);
        Assert.False(updatedFile.IsMissing);

        // Movie count must remain 1 (no duplicate entity)
        var currentMovies = await _movieRepository.GetAllAsync();
        Assert.Single(currentMovies);
        Assert.Equal(originalMovie.Id, currentMovies[0].Id);
    }

    [Fact]
    public async Task DeletedFile_MarksIsMissingTrue_AndRediscoveryRestoresIsMissingFalse()
    {
        var rootDir = Path.Combine(_tempDir, "MLibrary2");
        Directory.CreateDirectory(rootDir);
        var filePath = Path.Combine(rootDir, "Gladiator.2000.mkv");
        File.WriteAllText(filePath, "Gladiator Video Content Sample");

        await _scanRootService.AddRootAsync(rootDir);
        await _importService.ImportAsync();

        var initialFile = (await _mediaFileRepository.GetAllAsync()).First();
        Assert.False(initialFile.IsMissing);

        // Delete the file from the filesystem
        File.Delete(filePath);

        // Scan: must detect missing file and mark IsMissing = true
        var resMissing = await _importService.ImportAsync();
        Assert.Equal(1, resMissing.FilesMissing);

        var fileAfterDeletion = await _mediaFileRepository.GetByIdAsync(initialFile.Id);
        Assert.NotNull(fileAfterDeletion);
        Assert.True(fileAfterDeletion.IsMissing);

        // Restore the file to disk
        File.WriteAllText(filePath, "Gladiator Video Content Restored");

        // Scan: must rediscover file and restore IsMissing = false
        var resRestored = await _importService.ImportAsync();
        Assert.Equal(1, resRestored.FilesUpdated);

        var fileAfterRestore = await _mediaFileRepository.GetByIdAsync(initialFile.Id);
        Assert.NotNull(fileAfterRestore);
        Assert.False(fileAfterRestore.IsMissing);
    }

    [Fact]
    public async Task DriveSafety_InaccessibleRoot_PreservesRecordsWithoutMarkingMissing()
    {
        var rootDir = Path.Combine(_tempDir, "MLibrarySafety");
        Directory.CreateDirectory(rootDir);
        var filePath = Path.Combine(rootDir, "Interstellar.2014.mkv");
        File.WriteAllText(filePath, "Interstellar Video Content");

        await _scanRootService.AddRootAsync(rootDir);
        await _importService.ImportAsync();

        var initialFile = (await _mediaFileRepository.GetAllAsync()).First();
        Assert.False(initialFile.IsMissing);

        // Temporarily delete the root directory to simulate a disconnected drive
        Directory.Delete(rootDir, true);

        // Import must skip offline root and NOT mark files missing or delete records
        var resOffline = await _importService.ImportAsync();
        Assert.Equal(0, resOffline.FilesMissing);

        var fileAfterOffline = await _mediaFileRepository.GetByIdAsync(initialFile.Id);
        Assert.NotNull(fileAfterOffline);
        Assert.False(fileAfterOffline.IsMissing);
    }

    [Fact]
    public async Task MultiEditionMovie_WhenOneEditionMissing_PreservesMovieAndOtherFile()
    {
        var rootDir = Path.Combine(_tempDir, "MLibraryMulti");
        Directory.CreateDirectory(rootDir);

        var pathTheatrical = Path.Combine(rootDir, "Avatar.2009.Theatrical.mkv");
        var pathExtended = Path.Combine(rootDir, "Avatar.2009.Extended.mkv");

        File.WriteAllText(pathTheatrical, "Avatar Theatrical Edition");
        File.WriteAllText(pathExtended, "Avatar Extended Edition");

        await _scanRootService.AddRootAsync(rootDir);
        await _importService.ImportAsync();

        var allFiles = await _mediaFileRepository.GetAllAsync();
        Assert.Equal(2, allFiles.Count);

        // Delete only the theatrical cut
        File.Delete(pathTheatrical);

        await _importService.ImportAsync();

        var remainingFiles = await _mediaFileRepository.GetAllAsync();
        Assert.Equal(2, remainingFiles.Count);

        var theatricalRecord = remainingFiles.First(f => f.FilePath == Path.GetFullPath(pathTheatrical));
        var extendedRecord = remainingFiles.First(f => f.FilePath == Path.GetFullPath(pathExtended));

        Assert.True(theatricalRecord.IsMissing);
        Assert.False(extendedRecord.IsMissing);

        // Movie entity remains present and accessible via the extended edition
        var movie = (await _movieRepository.GetAllAsync()).First();
        Assert.NotNull(movie);
    }
}
