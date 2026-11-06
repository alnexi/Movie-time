using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.ViewModels;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class MovieDetailsViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _databaseService;
    private readonly MovieRepository _movieRepository;
    private readonly MediaFileRepository _mediaFileRepository;

    public MovieDetailsViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MovieDetailsTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_moviedetails.db");

        var storage = new StoragePathService(_tempDir);
        _databaseService = new DatabaseService(storage, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _movieRepository = new MovieRepository(_databaseService);
        _mediaFileRepository = new MediaFileRepository(_databaseService);
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

    private MovieDetailsViewModel CreateViewModel()
    {
        return new MovieDetailsViewModel(
            _movieRepository,
            _mediaFileRepository,
            NullLogger<MovieDetailsViewModel>.Instance);
    }

    [Fact]
    public async Task LoadMovieAsync_WithValidMovieAndFile_PopulatesRealData()
    {
        var movie = new Movie
        {
            Title = "Interstellar",
            ReleaseYear = 2014,
            Edition = "IMAX Edition",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = Path.Combine(_tempDir, "Interstellar.2014.IMAX.2160p.mkv"),
            FileName = "Interstellar.2014.IMAX.2160p.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1024L * 1024L * 1024L * 18L, // 18 GB
            VideoCodec = "HEVC",
            VideoWidth = 3840,
            VideoHeight = 2160,
            AudioCodec = "DTS-HD MA 5.1",
            ContainerFormat = "mkv",
            LastScannedAt = DateTime.UtcNow
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        var vm = CreateViewModel();
        await vm.LoadMovieAsync(movie.Id);

        Assert.Equal(movie.Id, vm.MovieId);
        Assert.True(vm.IsMovieFound);
        Assert.Equal("Interstellar", vm.Title);
        Assert.Equal(2014, vm.ReleaseYear);
        Assert.Equal("2014", vm.YearDisplay);
        Assert.True(vm.HasYear);
        Assert.Equal("IMAX Edition", vm.Edition);
        Assert.True(vm.HasEdition);

        // Derived media file metadata
        Assert.Equal("4K UHD", vm.QualityBadge);
        Assert.True(vm.HasQualityBadge);
        Assert.Equal("3840 x 2160 (4K UHD)", vm.ResolutionDisplay);
        Assert.True(vm.HasResolution);
        Assert.Equal("HEVC", vm.VideoCodecDisplay);
        Assert.True(vm.HasVideoCodec);
        Assert.Equal("DTS-HD MA 5.1", vm.AudioCodecDisplay);
        Assert.True(vm.HasAudioCodec);
        Assert.Equal("MKV", vm.FormatDisplay);
        Assert.True(vm.HasFormat);
        Assert.Equal("18.0 GB", vm.FileSizeDisplay);
        Assert.True(vm.HasFileSize);
        Assert.Equal(mediaFile.FilePath, vm.LocalFilePath);
        Assert.Equal("Interstellar.2014.IMAX.2160p.mkv", vm.LocalFileName);
        Assert.True(vm.HasLocalFile);
    }

    [Fact]
    public async Task LoadMovieAsync_WhenArtworkDoesNotExistOnDisk_SetsFallbackFlags()
    {
        var movie = new Movie
        {
            Title = "No Artwork Film",
            ReleaseYear = 2022,
            PosterPath = @"C:\NonExistent\poster.jpg",
            BackdropPath = @"C:\NonExistent\backdrop.jpg",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        var vm = CreateViewModel();
        await vm.LoadMovieAsync(movie.Id);

        // File.Exists returns false, so HasBackdrop and HasPoster must be false
        Assert.False(vm.HasBackdrop);
        Assert.False(vm.HasPoster);
        Assert.True(vm.NeedsBackdropFallback);
    }

    [Fact]
    public async Task LoadMovieAsync_WhenArtworkExistsOnDisk_SetsHasBackdropTrue()
    {
        var backdropFile = Path.Combine(_tempDir, "real_backdrop.jpg");
        File.WriteAllBytes(backdropFile, new byte[100]);

        var movie = new Movie
        {
            Title = "Backdrop Film",
            ReleaseYear = 2021,
            BackdropPath = backdropFile,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        var vm = CreateViewModel();
        await vm.LoadMovieAsync(movie.Id);

        Assert.True(vm.HasBackdrop);
        Assert.False(vm.NeedsBackdropFallback);
    }

    [Fact]
    public async Task LoadMovieAsync_WithMissingMetadata_DoesNotFabricateInformation()
    {
        var movie = new Movie
        {
            Title = "Minimal Movie",
            ReleaseYear = null,
            Edition = null,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        var file = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = Path.Combine(_tempDir, "Minimal.mkv"),
            FileName = "Minimal.mkv",
            FileExtension = ".mkv",
            SizeBytes = 0,
            VideoCodec = null,
            VideoWidth = null,
            VideoHeight = null,
            AudioCodec = null,
            ContainerFormat = null,
            LastScannedAt = DateTime.UtcNow
        };
        await _mediaFileRepository.InsertAsync(file);

        var vm = CreateViewModel();
        await vm.LoadMovieAsync(movie.Id);

        Assert.Null(vm.ReleaseYear);
        Assert.Equal(string.Empty, vm.YearDisplay);
        Assert.False(vm.HasYear);
        Assert.Null(vm.Edition);
        Assert.False(vm.HasEdition);
        Assert.Null(vm.QualityBadge);
        Assert.False(vm.HasQualityBadge);
        Assert.Null(vm.ResolutionDisplay);
        Assert.False(vm.HasResolution);
        Assert.Null(vm.VideoCodecDisplay);
        Assert.False(vm.HasVideoCodec);
        Assert.Null(vm.AudioCodecDisplay);
        Assert.False(vm.HasAudioCodec);
        Assert.False(vm.HasFileSize);
    }

    [Fact]
    public async Task LoadMovieAsync_WithMultipleFiles_AllowsSelectingEdition()
    {
        var movie = new Movie
        {
            Title = "Blade Runner",
            ReleaseYear = 1982,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        var file1080p = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = Path.Combine(_tempDir, "BladeRunner.1080p.mkv"),
            FileName = "BladeRunner.1080p.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1024L * 1024L * 1024L * 8L,
            VideoHeight = 1080,
            VideoWidth = 1920,
            VideoCodec = "AVC",
            LastScannedAt = DateTime.UtcNow
        };
        var file4K = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = Path.Combine(_tempDir, "BladeRunner.FinalCut.2160p.mkv"),
            FileName = "BladeRunner.FinalCut.2160p.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1024L * 1024L * 1024L * 25L,
            VideoHeight = 2160,
            VideoWidth = 3840,
            VideoCodec = "HEVC",
            LastScannedAt = DateTime.UtcNow
        };

        await _mediaFileRepository.InsertAsync(file1080p);
        await _mediaFileRepository.InsertAsync(file4K);

        var vm = CreateViewModel();
        await vm.LoadMovieAsync(movie.Id);

        Assert.True(vm.HasMultipleFiles);
        Assert.Equal(2, vm.MediaFiles.Count);

        // Default to first file (1080p)
        Assert.Equal(file1080p.Id, vm.SelectedMediaFile?.Id);
        Assert.Equal("1080p HD", vm.QualityBadge);

        // Select second file (4K)
        var file4kVm = vm.MediaFiles.First(f => f.Id == file4K.Id);
        vm.SelectMediaFile(file4kVm);

        Assert.Equal(file4K.Id, vm.SelectedMediaFile?.Id);
        Assert.Equal("4K UHD", vm.QualityBadge);
        Assert.Equal("3840 x 2160 (4K UHD)", vm.ResolutionDisplay);
        Assert.Equal("HEVC", vm.VideoCodecDisplay);
    }

    [Fact]
    public async Task PlayCommand_TriggersPlaybackEventWithActualFilePath()
    {
        var movie = new Movie
        {
            Title = "Dune",
            ReleaseYear = 2021,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        var expectedPath = Path.Combine(_tempDir, "Dune.2021.1080p.mkv");
        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = expectedPath,
            FileName = "Dune.2021.1080p.mkv",
            FileExtension = ".mkv",
            SizeBytes = 1024L * 1024L * 500L,
            LastScannedAt = DateTime.UtcNow
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        var vm = CreateViewModel();
        await vm.LoadMovieAsync(movie.Id);

        string? receivedPath = null;
        vm.PlaybackRequested += path => receivedPath = path;

        vm.PlayCommand.Execute(null);

        Assert.Equal(expectedPath, receivedPath);
        Assert.True(vm.HasPlaybackStatus);
        Assert.Contains("Ready for playback", vm.PlaybackStatusMessage);
        Assert.Contains("Dune.2021.1080p.mkv", vm.PlaybackStatusMessage);
    }

    [Fact]
    public void BackCommand_RaisesNavigationRequestedToMovies()
    {
        var vm = CreateViewModel();
        string? navigationTarget = null;
        vm.NavigationRequested += target => navigationTarget = target;

        vm.BackCommand.Execute(null);

        Assert.Equal("Movies", navigationTarget);
    }

    [Fact]
    public async Task Recommendations_OnlyIncludesOtherRealLocalMovies()
    {
        var movie1 = new Movie { Title = "Movie One", ReleaseYear = 2020 };
        var movie2 = new Movie { Title = "Movie Two", ReleaseYear = 2021 };
        var movie3 = new Movie { Title = "Movie Three", ReleaseYear = 2022 };
        await _movieRepository.InsertAsync(movie1);
        await _movieRepository.InsertAsync(movie2);
        await _movieRepository.InsertAsync(movie3);

        var vm = CreateViewModel();
        await vm.LoadMovieAsync(movie1.Id);

        Assert.True(vm.HasRecommendations);
        Assert.Equal(2, vm.RecommendedMovies.Count);
        Assert.DoesNotContain(vm.RecommendedMovies, r => r.Id == movie1.Id);
        Assert.Contains(vm.RecommendedMovies, r => r.Title == "Movie Two");
        Assert.Contains(vm.RecommendedMovies, r => r.Title == "Movie Three");
    }

    [Fact]
    public async Task Recommendations_WhenNoOtherMoviesExist_IsCleanlyEmpty()
    {
        var loneMovie = new Movie { Title = "Lone Movie", ReleaseYear = 2023 };
        await _movieRepository.InsertAsync(loneMovie);

        var vm = CreateViewModel();
        await vm.LoadMovieAsync(loneMovie.Id);

        Assert.False(vm.HasRecommendations);
        Assert.Empty(vm.RecommendedMovies);
    }

    [Fact]
    public async Task LoadMovieAsync_WithInvalidId_ResetsState()
    {
        var vm = CreateViewModel();
        await vm.LoadMovieAsync(9999); // Non-existent movie

        Assert.False(vm.IsMovieFound);
        Assert.Equal(0, vm.MovieId);
        Assert.Equal(string.Empty, vm.Title);
        Assert.Null(vm.ReleaseYear);
        Assert.Empty(vm.MediaFiles);
        Assert.Empty(vm.RecommendedMovies);
    }
}
