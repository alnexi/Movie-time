using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.ViewModels;
using MovieTime.Core.Artwork;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Library;
using MovieTime.Core.Parsing;
using MovieTime.Core.Scanning;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class HomeExperienceTests : IDisposable
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
    private readonly StoragePathService _storagePathService;
    private readonly ArtworkStorageService _artworkStorageService;

    public HomeExperienceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HomeExperienceTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_home_exp.db");

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
        catch { }
    }

    private HomeViewModel CreateViewModel(IHoverPreviewService? hoverService = null)
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
            null,
            hoverService,
            _artworkStorageService,
            null,
            NullLogger<HomeViewModel>.Instance);
    }

    [Fact]
    public async Task DynamicHero_PopulatesUpToSevenEligibleMovies()
    {
        // Arrange: Insert 8 movies with media files
        for (int i = 1; i <= 8; i++)
        {
            var movie = new Movie
            {
                Title = $"Movie {i}",
                ReleaseYear = 2020 + i,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
            await _movieRepository.InsertAsync(movie);

            var file = new MediaFile
            {
                MovieId = movie.Id,
                FilePath = $@"C:\Movies\Movie{i}.mkv",
                FileName = $"Movie{i}.mkv",
                FileExtension = ".mkv",
                SizeBytes = 1000 * i,
                VideoHeight = 1080,
                LastScannedAt = DateTime.UtcNow
            };
            await _mediaFileRepository.InsertAsync(file);
        }

        var vm = CreateViewModel();

        // Act
        await vm.InitializeAsync();

        // Assert: Capped at 7
        Assert.Equal(7, vm.HeroItems.Count);
        Assert.Equal("MOVIE 1", vm.Hero.Title);
        Assert.Equal(0, vm.CurrentHeroIndex);
        Assert.Equal(7, vm.Hero.Dots.Count);
        Assert.True(vm.Hero.Dots[0].IsActive);
        Assert.False(vm.Hero.Dots[1].IsActive);
    }

    [Fact]
    public async Task DynamicHero_RotationAdvancesAndUpdatesDots()
    {
        for (int i = 1; i <= 3; i++)
        {
            var movie = new Movie { Title = $"Film {i}", ReleaseYear = 2020 + i, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
            await _movieRepository.InsertAsync(movie);

            var file = new MediaFile
            {
                MovieId = movie.Id,
                FilePath = $@"C:\Movies\Film{i}.mkv",
                FileName = $"Film{i}.mkv",
                FileExtension = ".mkv",
                SizeBytes = 1000,
                VideoHeight = 1080,
                LastScannedAt = DateTime.UtcNow
            };
            await _mediaFileRepository.InsertAsync(file);
        }

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        // Advance to next hero item
        vm.NextHeroItem();

        Assert.Equal(1, vm.CurrentHeroIndex);
        Assert.Equal("FILM 2", vm.Hero.Title);
        Assert.False(vm.Hero.Dots[0].IsActive);
        Assert.True(vm.Hero.Dots[1].IsActive);
        Assert.False(vm.Hero.Dots[2].IsActive);

        // Switch directly to index 2
        vm.SwitchToHeroIndex(2);
        Assert.Equal(2, vm.CurrentHeroIndex);
        Assert.Equal("FILM 3", vm.Hero.Title);
        Assert.True(vm.Hero.Dots[2].IsActive);

        // Clamping check
        vm.SwitchToHeroIndex(99);
        Assert.Equal(2, vm.CurrentHeroIndex);
    }

    [Fact]
    public async Task DynamicHero_PauseAndResume_UpdatesPausedState()
    {
        var movie = new Movie { Title = "Interstellar", ReleaseYear = 2014, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await _movieRepository.InsertAsync(movie);
        var file = new MediaFile { MovieId = movie.Id, FilePath = @"C:\Movies\Interstellar.mkv", FileName = "Interstellar.mkv", FileExtension = ".mkv", SizeBytes = 1000, LastScannedAt = DateTime.UtcNow };
        await _mediaFileRepository.InsertAsync(file);

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        Assert.False(vm.IsRotationPaused);

        vm.Hero.PauseCommand?.Execute(null);
        Assert.True(vm.IsRotationPaused);

        vm.Hero.ResumeCommand?.Execute(null);
        Assert.False(vm.IsRotationPaused);
    }

    [Fact]
    public async Task DynamicHero_PlayAndDetailsCommands_TriggerExpectedEvents()
    {
        var movie = new Movie { Title = "Blade Runner 2049", ReleaseYear = 2017, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await _movieRepository.InsertAsync(movie);
        var file = new MediaFile { MovieId = movie.Id, FilePath = @"C:\Movies\BR2049.mkv", FileName = "BR2049.mkv", FileExtension = ".mkv", SizeBytes = 2000, LastScannedAt = DateTime.UtcNow };
        await _mediaFileRepository.InsertAsync(file);

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        string? playedPath = null;
        int? playedId = null;
        vm.PlaybackRequested += (path, id) =>
        {
            playedPath = path;
            playedId = id;
        };

        string? navigatedTarget = null;
        vm.NavigationRequested += target => navigatedTarget = target;

        // Act 1: PlayCommand
        vm.Hero.PlayCommand?.Execute(null);
        Assert.Equal(@"C:\Movies\BR2049.mkv", playedPath);
        Assert.Equal(movie.Id, playedId);

        // Act 2: DetailsCommand
        vm.Hero.DetailsCommand?.Execute(null);
        Assert.Equal($"MovieDetails:{movie.Id}", navigatedTarget);
    }

    [Fact]
    public async Task ContinueWatching_WiredWithRealMovieIdAndPlaybackCommands()
    {
        var movie = new Movie { Title = "Dune", ReleaseYear = 2021, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, BackdropPath = @"C:\Artwork\dune_backdrop.jpg" };
        await _movieRepository.InsertAsync(movie);

        var file = new MediaFile { MovieId = movie.Id, FilePath = @"C:\Movies\Dune.mkv", FileName = "Dune.mkv", FileExtension = ".mkv", SizeBytes = 3000, LastScannedAt = DateTime.UtcNow };
        await _mediaFileRepository.InsertAsync(file);

        // Seed playback progress: 45 minutes into 2-hour movie (37.5%)
        var progress = new PlaybackProgress
        {
            MovieId = movie.Id,
            MediaFileId = file.Id,
            PositionTicks = TimeSpan.FromMinutes(45).Ticks,
            DurationTicks = TimeSpan.FromHours(2).Ticks,
            PositionPercentage = 0.375,
            LastWatchedAt = DateTime.UtcNow
        };
        await _databaseService.InsertAsync(progress);

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        var continueRow = vm.Rows.FirstOrDefault(r => r.Title == "Continue Watching");
        Assert.NotNull(continueRow);
        Assert.Single(continueRow.Items);

        var card = continueRow.Items[0];
        Assert.Equal(movie.Id, card.Id);
        Assert.Equal(movie.Id, card.MovieId);
        Assert.Equal(file.Id, card.MediaFileId);
        Assert.Equal(@"C:\Movies\Dune.mkv", card.FilePath);
        Assert.Equal(0.375, card.ProgressPercentage);
        Assert.Equal("Dune", card.Title);
        Assert.Contains("38%", card.Subtitle);
        Assert.Equal(@"C:\Artwork\dune_backdrop.jpg", card.BackdropPath);

        // Test clicking card invokes Movie Details
        string? navTarget = null;
        vm.NavigationRequested += t => navTarget = t;
        card.SelectCommand?.Execute(card);
        Assert.Equal($"MovieDetails:{movie.Id}", navTarget);

        // Test clicking card play invokes PlaybackRequested with resume file and movie ID
        string? playPath = null;
        int? playId = null;
        vm.PlaybackRequested += (p, id) =>
        {
            playPath = p;
            playId = id;
        };
        card.PlayCommand?.Execute(null);
        Assert.Equal(@"C:\Movies\Dune.mkv", playPath);
        Assert.Equal(movie.Id, playId);
    }

    [Fact]
    public async Task MoviesShelf_CardsHaveSelectAndPlayCommands()
    {
        var movie = new Movie { Title = "Arrival", ReleaseYear = 2016, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        await _movieRepository.InsertAsync(movie);
        var file = new MediaFile { MovieId = movie.Id, FilePath = @"C:\Movies\Arrival.mkv", FileName = "Arrival.mkv", FileExtension = ".mkv", SizeBytes = 2500, LastScannedAt = DateTime.UtcNow };
        await _mediaFileRepository.InsertAsync(file);

        var vm = CreateViewModel();
        await vm.InitializeAsync();

        var moviesRow = vm.Rows.FirstOrDefault(r => r.Title == "Movies");
        Assert.NotNull(moviesRow);
        var card = moviesRow.Items.FirstOrDefault(c => c.Title == "Arrival");
        Assert.NotNull(card);

        string? navTarget = null;
        vm.NavigationRequested += t => navTarget = t;
        card.SelectCommand?.Execute(card);
        Assert.Equal($"MovieDetails:{movie.Id}", navTarget);

        string? playPath = null;
        int? playId = null;
        vm.PlaybackRequested += (p, id) =>
        {
            playPath = p;
            playId = id;
        };
        card.PlayCommand?.Execute(null);
        Assert.Equal(@"C:\Movies\Arrival.mkv", playPath);
        Assert.Equal(movie.Id, playId);
    }

    [Fact]
    public async Task HoverPreviewService_CachesPositionThumbnailAndExtractsFrames()
    {
        // Mock fake frame extractor
        var mockExtractor = new FakeVideoFrameExtractor();
        var hoverService = new HoverPreviewService(_artworkStorageService, mockExtractor, NullLogger<HoverPreviewService>.Instance);

        // Create temporary dummy video file
        var dummyVideoPath = Path.Combine(_tempDir, "sample_video.mp4");
        File.WriteAllBytes(dummyVideoPath, new byte[1024]);

        var fi = new FileInfo(dummyVideoPath);

        // 1. Extract position thumbnail
        var thumbPath = await hoverService.GetOrExtractPositionThumbnailAsync(
            dummyVideoPath,
            fi.Length,
            fi.LastWriteTimeUtc,
            120);

        Assert.NotNull(thumbPath);
        Assert.True(File.Exists(thumbPath));

        // Calling again should return cached path without re-extracting
        mockExtractor.ExtractCallCount = 0;
        var cachedThumb = await hoverService.GetOrExtractPositionThumbnailAsync(
            dummyVideoPath,
            fi.Length,
            fi.LastWriteTimeUtc,
            120);

        Assert.Equal(thumbPath, cachedThumb);
        Assert.Equal(0, mockExtractor.ExtractCallCount);

        // 2. Extract 4 preview frames
        var frames = await hoverService.GetOrExtractPreviewFramesAsync(
            dummyVideoPath,
            fi.Length,
            fi.LastWriteTimeUtc,
            120);

        Assert.Equal(4, frames.Count);
        foreach (var framePath in frames)
        {
            Assert.True(File.Exists(framePath));
        }

        // Calling preview again should return cached set
        mockExtractor.ExtractFramesCallCount = 0;
        var cachedFrames = await hoverService.GetOrExtractPreviewFramesAsync(
            dummyVideoPath,
            fi.Length,
            fi.LastWriteTimeUtc,
            120);

        Assert.Equal(4, cachedFrames.Count);
        Assert.Equal(0, mockExtractor.ExtractFramesCallCount);
    }

    private sealed class FakeVideoFrameExtractor : IVideoFrameExtractor
    {
        public int ExtractCallCount { get; set; }
        public int ExtractFramesCallCount { get; set; }

        private static readonly byte[] ValidJpeg = GenerateFakeJpegBytes();

        private static byte[] GenerateFakeJpegBytes()
        {
            var bytes = new byte[300];
            bytes[0] = 0xFF; bytes[1] = 0xD8; bytes[2] = 0xFF; bytes[3] = 0xE0; // SOI + APP0
            bytes[298] = 0xFF; bytes[299] = 0xD9; // EOI
            return bytes;
        }

        public Task<CandidateVideoFrame?> ExtractFrameAtTimeAsync(
            string filePath,
            TimeSpan targetTime,
            int targetWidth = 520,
            int targetHeight = 292,
            CancellationToken ct = default)
        {
            ExtractCallCount++;
            var frame = new CandidateVideoFrame
            {
                ImageBytes = ValidJpeg,
                Width = targetWidth,
                Height = targetHeight,
                Timestamp = targetTime
            };
            return Task.FromResult<CandidateVideoFrame?>(frame);
        }

        public Task<IReadOnlyList<CandidateVideoFrame>> ExtractFramesAroundTimeAsync(
            string filePath,
            TimeSpan startTime,
            int frameCount,
            TimeSpan interval,
            int targetWidth = 520,
            int targetHeight = 292,
            CancellationToken ct = default)
        {
            ExtractFramesCallCount++;
            var list = new List<CandidateVideoFrame>();
            for (int i = 0; i < frameCount; i++)
            {
                list.Add(new CandidateVideoFrame
                {
                    ImageBytes = ValidJpeg,
                    Width = targetWidth,
                    Height = targetHeight,
                    Timestamp = startTime + TimeSpan.FromSeconds(i)
                });
            }
            return Task.FromResult<IReadOnlyList<CandidateVideoFrame>>(list);
        }

        public Task<IReadOnlyList<CandidateVideoFrame>> ExtractCandidateFramesAsync(
            string filePath,
            long durationMs,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CandidateVideoFrame>>(Array.Empty<CandidateVideoFrame>());
        }
    }
}
