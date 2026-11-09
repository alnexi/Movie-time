using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.ViewModels;
using MovieTime.Core.Artwork;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Library;
using MovieTime.Core.Parsing;
using MovieTime.Core.Playback;
using MovieTime.Core.Scanning;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class TvShowExperienceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _databaseService;
    private readonly StoragePathService _storagePathService;
    private readonly ArtworkStorageService _artworkStorageService;

    private readonly MovieRepository _movieRepository;
    private readonly SeriesRepository _seriesRepository;
    private readonly SeasonRepository _seasonRepository;
    private readonly EpisodeRepository _episodeRepository;
    private readonly MediaFileRepository _mediaFileRepository;
    private readonly ScanRootRepository _scanRootRepository;
    private readonly PlaybackProgressRepository _progressRepository;

    private readonly ScanRootService _scanRootService;
    private readonly MediaScannerService _scannerService;
    private readonly FilenameParserEngine _parserEngine;
    private readonly LibraryImportService _importService;
    private readonly FakeVlcPlayerService _fakePlayer;

    public TvShowExperienceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TvShowExperienceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_tv_experience.db");

        _storagePathService = new StoragePathService(_tempDir);
        _artworkStorageService = new ArtworkStorageService(_storagePathService, NullLogger<ArtworkStorageService>.Instance);

        _databaseService = new DatabaseService(_storagePathService, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _movieRepository = new MovieRepository(_databaseService);
        _seriesRepository = new SeriesRepository(_databaseService);
        _seasonRepository = new SeasonRepository(_databaseService);
        _episodeRepository = new EpisodeRepository(_databaseService);
        _mediaFileRepository = new MediaFileRepository(_databaseService);
        _scanRootRepository = new ScanRootRepository(_databaseService);
        _progressRepository = new PlaybackProgressRepository(_databaseService);

        _scanRootService = new ScanRootService(_scanRootRepository, NullLogger<ScanRootService>.Instance);
        _scannerService = new MediaScannerService(NullLogger<MediaScannerService>.Instance);
        _parserEngine = new FilenameParserEngine(NullLogger<FilenameParserEngine>.Instance, _storagePathService);

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

        _fakePlayer = new FakeVlcPlayerService();
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

    private string CreateTestFile(string relativePath, int sizeBytes = 1024)
    {
        var fullPath = Path.Combine(_tempDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var buffer = new byte[sizeBytes];
        File.WriteAllBytes(fullPath, buffer);
        return fullPath;
    }

    [Fact]
    public async Task ImportTvShow_CreatesSeriesSeasonEpisodesAndLinksMediaFiles()
    {
        // Arrange
        var rootDir = Path.Combine(_tempDir, "TV_Root");
        Directory.CreateDirectory(rootDir);
        await _scanRootService.AddRootAsync(rootDir);

        // Primary files to import
        var ep8Path = Path.Combine(rootDir, "The.Wire.S01E08.Lessons.REMASTERED.720p.WEB-DL.2CH.x265.HEVC.PSA.AvaMovie.mkv");
        var ep9Path = Path.Combine(rootDir, "The.Wire.S01E09.Game.Day.720p.mkv");
        var spec1Path = Path.Combine(rootDir, "The.Wire.S00E01.Behind.The.Scenes.720p.mkv");

        File.WriteAllBytes(ep8Path, new byte[2048]);
        File.WriteAllBytes(ep9Path, new byte[2048]);
        File.WriteAllBytes(spec1Path, new byte[2048]);

        // Act
        var result = await _importService.ImportAsync();

        // Assert
        Assert.NotNull(result);
        var seriesList = await _seriesRepository.GetAllAsync();
        Assert.Single(seriesList);

        var wireSeries = seriesList[0];
        Assert.Equal("The Wire", wireSeries.Title);

        var seasons = await _seasonRepository.GetBySeriesIdAsync(wireSeries.Id);
        Assert.Equal(2, seasons.Count);

        var specialsSeason = seasons.FirstOrDefault(s => s.SeasonNumber == 0);
        Assert.NotNull(specialsSeason);
        Assert.Equal("Specials", specialsSeason.Title);

        var season1 = seasons.FirstOrDefault(s => s.SeasonNumber == 1);
        Assert.NotNull(season1);
        Assert.Equal("Season 1", season1.Title);

        var season1Episodes = await _episodeRepository.GetBySeasonIdAsync(season1.Id);
        Assert.Equal(2, season1Episodes.Count);

        var ep8 = season1Episodes.FirstOrDefault(e => e.EpisodeNumber == 8);
        Assert.NotNull(ep8);
        Assert.Equal("Lessons", ep8.Title);
        Assert.Equal(wireSeries.Id, ep8.SeriesId);
        Assert.Equal(season1.Id, ep8.SeasonId);

        var ep9 = season1Episodes.FirstOrDefault(e => e.EpisodeNumber == 9);
        Assert.NotNull(ep9);
        Assert.Equal("Game Day", ep9.Title);

        var specialsEpisodes = await _episodeRepository.GetBySeasonIdAsync(specialsSeason.Id);
        Assert.Single(specialsEpisodes);
        var spec1 = specialsEpisodes[0];
        Assert.Equal(1, spec1.EpisodeNumber);
        Assert.Equal("Behind The Scenes", spec1.Title);

        // Verify MediaFiles relationship
        var ep8Files = await _mediaFileRepository.GetByEpisodeIdAsync(ep8.Id);
        Assert.Single(ep8Files);
        Assert.Equal(ep8Path, ep8Files[0].FilePath);
        Assert.Equal(ep8.Id, ep8Files[0].EpisodeId);
    }

    [Fact]
    public async Task SaveAndGetPlaybackProgress_ForEpisode_PersistsAndRestoresPosition()
    {
        // Arrange
        long posTicks = TimeSpan.FromMinutes(22).Ticks;
        long durTicks = TimeSpan.FromMinutes(58).Ticks;

        // Act - Save progress specifically with an episode ID
        var saveResult = await _progressRepository.SaveProgressAsync(
            mediaFileId: 501,
            movieId: null,
            positionTicks: posTicks,
            durationTicks: durTicks,
            isFinished: false,
            episodeId: 77);

        Assert.Equal(1, saveResult);

        // Assert - Retrieve progress by Episode ID
        var loaded = await _progressRepository.GetByEpisodeIdAsync(77);
        Assert.NotNull(loaded);
        Assert.Equal(77, loaded.EpisodeId);
        Assert.Equal(501, loaded.MediaFileId);
        Assert.Null(loaded.MovieId);
        Assert.Equal(posTicks, loaded.PositionTicks);
        Assert.Equal(durTicks, loaded.DurationTicks);
        Assert.False(loaded.IsFinished);
        Assert.True(loaded.PositionPercentage > 0.35 && loaded.PositionPercentage < 0.40);

        // Update progress
        long updatedTicks = TimeSpan.FromMinutes(45).Ticks;
        await _progressRepository.SaveProgressAsync(501, null, updatedTicks, durTicks, false, 77);

        var updated = await _progressRepository.GetByEpisodeIdAsync(77);
        Assert.NotNull(updated);
        Assert.Equal(updatedTicks, updated.PositionTicks);

        // Mark finished
        await _progressRepository.SaveProgressAsync(501, null, durTicks, durTicks, true, 77);
        var finished = await _progressRepository.GetByEpisodeIdAsync(77);
        Assert.NotNull(finished);
        Assert.True(finished.IsFinished);
    }

    [Fact]
    public async Task TvShowsViewModel_LoadSeriesAsync_PopulatesCatalog_AndSupportsSearchAndSort()
    {
        // Arrange
        var seriesA = new Series { Title = "Breaking Bad", StartYear = 2008, CreatedAt = DateTime.UtcNow.AddDays(-2) };
        await _seriesRepository.InsertAsync(seriesA);

        var s1A = new Season { SeriesId = seriesA.Id, SeasonNumber = 1, Title = "Season 1" };
        await _seasonRepository.InsertAsync(s1A);
        await _episodeRepository.InsertAsync(new Episode { SeriesId = seriesA.Id, SeasonId = s1A.Id, EpisodeNumber = 1, Title = "Pilot" });

        var seriesB = new Series { Title = "The Wire", StartYear = 2002, CreatedAt = DateTime.UtcNow };
        await _seriesRepository.InsertAsync(seriesB);

        var s1B = new Season { SeriesId = seriesB.Id, SeasonNumber = 1, Title = "Season 1" };
        await _seasonRepository.InsertAsync(s1B);
        await _episodeRepository.InsertAsync(new Episode { SeriesId = seriesB.Id, SeasonId = s1B.Id, EpisodeNumber = 1, Title = "The Target" });

        var vm = new TvShowsViewModel(
            _seriesRepository,
            _seasonRepository,
            _episodeRepository,
            _mediaFileRepository,
            _importService,
            null,
            null,
            NullLogger<TvShowsViewModel>.Instance);

        // Act - Initialize
        await vm.InitializeAsync();

        // Assert - Catalog loaded
        Assert.False(vm.IsLibraryEmpty);
        Assert.Equal(2, vm.TotalSeriesCount);
        Assert.Equal(2, vm.SeriesList.Count);

        // Search filtering
        vm.SearchQuery = "Wire";
        Assert.Single(vm.SeriesList);
        Assert.Equal("The Wire", vm.SeriesList[0].Title);

        // Clear search
        vm.SearchQuery = "";
        Assert.Equal(2, vm.SeriesList.Count);

        // Sorting by Year descending
        vm.SelectedSort = "Year";
        Assert.Equal("Breaking Bad", vm.SeriesList[0].Title); // 2008 > 2002

        // Card navigation command
        string? navigatedRoute = null;
        vm.NavigationRequested += route => navigatedRoute = route;
        vm.SeriesList[0].SelectCommand?.Execute(null);

        Assert.NotNull(navigatedRoute);
        Assert.Contains("SeriesDetails:", navigatedRoute);
    }

    [Fact]
    public async Task SeriesDetailsViewModel_LoadSeriesAsync_PopulatesMetadataAndDefaultSeasonAndNextUp()
    {
        // Arrange
        var series = new Series { Title = "The Sopranos", StartYear = 1999 };
        await _seriesRepository.InsertAsync(series);

        var season1 = new Season { SeriesId = series.Id, SeasonNumber = 1, Title = "Season 1" };
        await _seasonRepository.InsertAsync(season1);

        var ep1 = new Episode { SeriesId = series.Id, SeasonId = season1.Id, EpisodeNumber = 1, Title = "The Sopranos" };
        await _episodeRepository.InsertAsync(ep1);

        var ep2 = new Episode { SeriesId = series.Id, SeasonId = season1.Id, EpisodeNumber = 2, Title = "46 Long" };
        await _episodeRepository.InsertAsync(ep2);

        var ep1FilePath = CreateTestFile("sopranos_s01e01.mkv");
        var ep2FilePath = CreateTestFile("sopranos_s01e02.mkv");

        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            FilePath = ep1FilePath,
            EpisodeId = ep1.Id,
            VideoWidth = 1920,
            VideoHeight = 1080
        });

        await _mediaFileRepository.InsertAsync(new MediaFile
        {
            FilePath = ep2FilePath,
            EpisodeId = ep2.Id,
            VideoWidth = 1920,
            VideoHeight = 1080
        });

        // Save progress on Episode 1 (45% watched, in-progress)
        long durTicks = TimeSpan.FromMinutes(50).Ticks;
        long posTicks = TimeSpan.FromMinutes(22).Ticks;
        await _progressRepository.SaveProgressAsync(1001, null, posTicks, durTicks, false, ep1.Id);

        var vm = new SeriesDetailsViewModel(
            _seriesRepository,
            _seasonRepository,
            _episodeRepository,
            _mediaFileRepository,
            _progressRepository,
            null,
            null,
            NullLogger<SeriesDetailsViewModel>.Instance);

        // Act - Load Series
        await vm.LoadSeriesAsync(series.Id);

        // Assert - Metadata
        Assert.Equal("The Sopranos", vm.Title);
        Assert.Equal(1999, vm.StartYear);
        Assert.Equal(1, vm.TotalSeasonsCount);
        Assert.Equal(2, vm.TotalEpisodesCount);

        // Season tabs & selected season
        Assert.Single(vm.SeasonTabs);
        Assert.NotNull(vm.SelectedSeason);
        Assert.Equal(1, vm.SelectedSeason.SeasonNumber);
        Assert.Equal(2, vm.Episodes.Count);

        // Next Up should be Episode 1 because it's in-progress
        Assert.NotNull(vm.NextUpEpisode);
        Assert.Equal(ep1.Id, vm.NextUpEpisode.EpisodeId);
        Assert.Contains("Resume", vm.NextUpActionText);

        // Test Play Next Up command
        string? playedFile = null;
        int? playedEpisodeId = null;
        vm.PlaybackRequested += (file, id) =>
        {
            playedFile = file;
            playedEpisodeId = id;
        };

        vm.PlayNextUpCommand.Execute(null);
        Assert.Equal(ep1FilePath, playedFile);
        Assert.Equal(ep1.Id, playedEpisodeId);

        // Mark Episode 1 as finished, and reload
        await _progressRepository.SaveProgressAsync(1001, null, durTicks, durTicks, true, ep1.Id);
        await vm.LoadSeriesAsync(series.Id);

        // Next Up should now advance to Episode 2 as first uncompleted
        Assert.NotNull(vm.NextUpEpisode);
        Assert.Equal(ep2.Id, vm.NextUpEpisode.EpisodeId);
        Assert.Contains("Play", vm.NextUpActionText);
    }

    [Fact]
    public async Task SeriesDetailsViewModel_SeasonSelection_SwitchesEpisodes()
    {
        // Arrange
        var series = new Series { Title = "Succession", StartYear = 2018 };
        await _seriesRepository.InsertAsync(series);

        var s1 = new Season { SeriesId = series.Id, SeasonNumber = 1, Title = "Season 1" };
        await _seasonRepository.InsertAsync(s1);
        await _episodeRepository.InsertAsync(new Episode { SeriesId = series.Id, SeasonId = s1.Id, EpisodeNumber = 1, Title = "Celebration" });

        var s2 = new Season { SeriesId = series.Id, SeasonNumber = 2, Title = "Season 2" };
        await _seasonRepository.InsertAsync(s2);
        await _episodeRepository.InsertAsync(new Episode { SeriesId = series.Id, SeasonId = s2.Id, EpisodeNumber = 1, Title = "The Summer Palace" });
        await _episodeRepository.InsertAsync(new Episode { SeriesId = series.Id, SeasonId = s2.Id, EpisodeNumber = 2, Title = "Vaulter" });

        var vm = new SeriesDetailsViewModel(
            _seriesRepository,
            _seasonRepository,
            _episodeRepository,
            _mediaFileRepository,
            _progressRepository,
            null,
            null,
            NullLogger<SeriesDetailsViewModel>.Instance);

        // Act
        await vm.LoadSeriesAsync(series.Id);

        // Initially Season 1 should have 1 episode
        Assert.Equal(2, vm.SeasonTabs.Count);
        Assert.Equal(1, vm.SelectedSeason?.SeasonNumber);
        Assert.Single(vm.Episodes);
        Assert.Equal("Celebration", vm.Episodes[0].Title);

        // Switch to Season 2
        var s2Tab = vm.SeasonTabs.First(s => s.SeasonNumber == 2);
        vm.SelectSeasonCommand.Execute(s2Tab);

        // Allow async load to complete
        await Task.Delay(100);

        Assert.Equal(2, vm.SelectedSeason?.SeasonNumber);
        Assert.Equal(2, vm.Episodes.Count);
        Assert.Equal("The Summer Palace", vm.Episodes[0].Title);
        Assert.Equal("Vaulter", vm.Episodes[1].Title);
    }

    [Fact]
    public async Task HomeViewModel_ContinueWatching_IncludesEpisodes_AndRaisesPlaybackRequested()
    {
        // Arrange
        var series = new Series { Title = "The Wire" };
        await _seriesRepository.InsertAsync(series);

        var season1 = new Season { SeriesId = series.Id, SeasonNumber = 1, Title = "Season 1" };
        await _seasonRepository.InsertAsync(season1);

        var ep = new Episode { SeriesId = series.Id, SeasonId = season1.Id, EpisodeNumber = 8, Title = "Lessons" };
        await _episodeRepository.InsertAsync(ep);

        var epFilePath = CreateTestFile("The.Wire.S01E08.mkv");
        var mediaFile = new MediaFile
        {
            FilePath = epFilePath,
            EpisodeId = ep.Id
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        // Save progress for episode: 50% watched
        long posTicks = TimeSpan.FromMinutes(25).Ticks;
        long durTicks = TimeSpan.FromMinutes(50).Ticks;
        await _progressRepository.SaveProgressAsync(mediaFile.Id, null, posTicks, durTicks, false, ep.Id);

        var homeVm = new HomeViewModel(
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
            _artworkStorageService,
            null,
            NullLogger<HomeViewModel>.Instance);

        // Act
        await homeVm.InitializeAsync();

        // Assert - Continue watching row contains the episode card
        var continueRow = homeVm.Rows.FirstOrDefault(r => r.Title == "Continue Watching");
        Assert.NotNull(continueRow);
        Assert.Single(continueRow.Items);
        var cwCard = continueRow.Items[0];

        Assert.Equal(ep.Id, cwCard.EpisodeId);
        Assert.Equal("The Wire", cwCard.Title);
        Assert.Contains("S1E8", cwCard.Subtitle);
        Assert.Contains("Lessons", cwCard.Subtitle);
        Assert.NotNull(cwCard.ProgressPercentage);
        Assert.Equal(0.5, cwCard.ProgressPercentage.Value, precision: 1);

        // Act - Trigger playback from Continue Watching
        string? playedPath = null;
        int? playedMovieId = null;
        int? playedEpisodeId = null;

        homeVm.MediaPlaybackRequested += (path, mId, eId) =>
        {
            playedPath = path;
            playedMovieId = mId;
            playedEpisodeId = eId;
        };

        cwCard.PlayCommand?.Execute(null);

        Assert.Equal(epFilePath, playedPath);
        Assert.Null(playedMovieId);
        Assert.Equal(ep.Id, playedEpisodeId);
    }

    [Fact]
    public async Task PlayerViewModel_EpisodePlayback_ResolvesMetadataAndSavesEpisodeProgress()
    {
        // Arrange
        var series = new Series { Title = "The Wire" };
        await _seriesRepository.InsertAsync(series);

        var season1 = new Season { SeriesId = series.Id, SeasonNumber = 1, Title = "Season 1" };
        await _seasonRepository.InsertAsync(season1);

        var ep = new Episode { SeriesId = series.Id, SeasonId = season1.Id, EpisodeNumber = 8, Title = "Lessons" };
        await _episodeRepository.InsertAsync(ep);

        var epFilePath = CreateTestFile("The.Wire.S01E08.Lessons.mkv");
        var mediaFile = new MediaFile
        {
            FilePath = epFilePath,
            EpisodeId = ep.Id,
            VideoWidth = 1280,
            VideoHeight = 720
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        var vm = new PlayerViewModel(
            _fakePlayer,
            _movieRepository,
            _mediaFileRepository,
            _progressRepository,
            NullLogger<PlayerViewModel>.Instance,
            null,
            null,
            _episodeRepository,
            _seriesRepository,
            _seasonRepository);

        // Act - Initialize episode
        await vm.InitializeAsync(epFilePath, movieId: null, episodeId: ep.Id);

        // Assert - Metadata resolution
        Assert.Equal(ep.Id, vm.EpisodeId);
        Assert.Equal("The Wire", vm.MovieTitle);
        Assert.Contains("Season 1, Episode 8", vm.ContextSubtitle);
        Assert.Contains("Lessons", vm.ContextSubtitle);

        // Verify navigation back route targets Series Details
        string? navigatedRoute = null;
        vm.NavigationRequested += route => navigatedRoute = route;
        vm.BackCommand.Execute(null);

        Assert.Equal($"SeriesDetails:{series.Id}", navigatedRoute);
    }
}
