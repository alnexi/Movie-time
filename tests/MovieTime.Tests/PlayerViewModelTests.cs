using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.ViewModels;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Playback;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class PlayerViewModelTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _databaseService;
    private readonly MovieRepository _movieRepository;
    private readonly MediaFileRepository _mediaFileRepository;
    private readonly PlaybackProgressRepository _progressRepository;
    private readonly FakeVlcPlayerService _fakePlayer;

    public PlayerViewModelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PlayerViewModelTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_player.db");

        var storage = new StoragePathService(_tempDir);
        _databaseService = new DatabaseService(storage, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _movieRepository = new MovieRepository(_databaseService);
        _mediaFileRepository = new MediaFileRepository(_databaseService);
        _progressRepository = new PlaybackProgressRepository(_databaseService);
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
            // best effort
        }
    }

    private PlayerViewModel CreateViewModel()
    {
        return new PlayerViewModel(
            _fakePlayer,
            _movieRepository,
            _mediaFileRepository,
            _progressRepository,
            NullLogger<PlayerViewModel>.Instance);
    }

    [Theory]
    [InlineData(0, "00:00")]
    [InlineData(45000, "00:45")]
    [InlineData(273000, "04:33")]
    [InlineData(3600000, "01:00:00")]
    [InlineData(5045000, "01:24:05")]
    [InlineData(-500, "00:00")]
    public void FormatTime_ProducesCorrectDisplays(long milliseconds, string expected)
    {
        var result = PlayerViewModel.FormatTime(milliseconds);
        Assert.Equal(expected, result);
    }

    [Fact]
    public async Task InitializeAsync_WithMissingFile_SetsError()
    {
        var vm = CreateViewModel();
        await vm.InitializeAsync(@"C:\NonExistent\film.mkv");

        Assert.True(vm.HasError);
        Assert.NotNull(vm.ErrorMessage);
        Assert.Contains("could not be found", vm.ErrorMessage);
    }

    [Fact]
    public async Task InitializeAsync_WithValidFile_LoadsMovieAndMediaInfo()
    {
        var testFile = Path.Combine(_tempDir, "Inception.2010.mkv");
        File.WriteAllBytes(testFile, new byte[64]);

        var movie = new Movie
        {
            Title = "Inception",
            ReleaseYear = 2010,
            Edition = "Theatrical"
        };
        await _movieRepository.InsertAsync(movie);

        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = testFile,
            FileName = "Inception.2010.mkv",
            FileExtension = ".mkv",
            VideoWidth = 3840,
            VideoHeight = 2160,
            VideoCodec = "HEVC",
            AudioCodec = "DTS 5.1",
            SizeBytes = 1024L * 1024L * 1024L * 8L,
            ContainerFormat = "mkv"
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        var vm = CreateViewModel();
        await vm.InitializeAsync(testFile, movie.Id);

        Assert.False(vm.HasError);
        Assert.Equal("Inception", vm.MovieTitle);
        Assert.Equal("2010 • Theatrical • 4K UHD • HEVC", vm.ContextSubtitle);
        Assert.Equal("3840 x 2160", vm.ResolutionDisplay);
        Assert.Equal("HEVC", vm.VideoCodecDisplay);
        Assert.Equal("DTS 5.1", vm.AudioCodecDisplay);
        Assert.Equal("MKV", vm.FormatDisplay);
        Assert.Equal("8.0 GB", vm.FileSizeDisplay);
        Assert.Equal(testFile, _fakePlayer.LastOpenedFile);
    }

    [Fact]
    public async Task InitializeAsync_WithSavedProgress_RestoresResumePosition()
    {
        var testFile = Path.Combine(_tempDir, "Dune.2021.mkv");
        File.WriteAllBytes(testFile, new byte[64]);

        var movie = new Movie { Title = "Dune", ReleaseYear = 2021 };
        await _movieRepository.InsertAsync(movie);

        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = testFile,
            FileName = "Dune.2021.mkv",
            FileExtension = ".mkv"
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        // Saved progress: 45 minutes into 2 hour film
        long posTicks = TimeSpan.FromMinutes(45).Ticks;
        long durTicks = TimeSpan.FromHours(2).Ticks;
        await _progressRepository.SaveProgressAsync(mediaFile.Id, movie.Id, posTicks, durTicks, false);

        var vm = CreateViewModel();
        await vm.InitializeAsync(testFile, movie.Id);

        long expectedResumeMs = posTicks / TimeSpan.TicksPerMillisecond;
        Assert.Equal(expectedResumeMs, _fakePlayer.LastStartPositionMs);
    }

    [Fact]
    public void PlayPauseCommand_TogglesServiceState()
    {
        var vm = CreateViewModel();

        vm.PlayPauseCommand.Execute(null);
        Assert.True(_fakePlayer.TogglePlayPauseCalled);
    }

    [Fact]
    public void SeekForwardAndBackward_CallsServiceWithCorrectDeltas()
    {
        var vm = CreateViewModel();

        vm.SeekForwardCommand.Execute(null);
        Assert.Equal(10000, _fakePlayer.LastSeekRelativeDelta);

        vm.SeekBackwardCommand.Execute(null);
        Assert.Equal(-10000, _fakePlayer.LastSeekRelativeDelta);
    }

    [Fact]
    public void VolumeAndMute_UpdatesServiceAndNotifiesProperties()
    {
        var vm = CreateViewModel();

        vm.SetVolume(75);
        Assert.Equal(75, _fakePlayer.Volume);
        Assert.Equal(75, vm.Volume);

        vm.ToggleMuteCommand.Execute(null);
        Assert.True(_fakePlayer.IsMuted);
        Assert.True(vm.IsMuted);
    }

    [Fact]
    public void ToggleFullscreen_UpdatesPropertyAndFiresEvent()
    {
        var vm = CreateViewModel();
        bool? toggledState = null;
        vm.FullscreenToggled += val => toggledState = val;

        vm.ToggleFullscreenCommand.Execute(null);

        Assert.True(vm.IsFullscreen);
        Assert.True(toggledState);
    }

    [Fact]
    public void BackCommand_StopsPlayerAndRaisesNavigation()
    {
        var vm = CreateViewModel();
        string? navigationTarget = null;
        vm.NavigationRequested += target => navigationTarget = target;

        vm.BackCommand.Execute(null);

        Assert.True(_fakePlayer.StopCalled);
        Assert.Equal("Movies", navigationTarget);
    }

    [Fact]
    public void TrackSelection_CallsServiceAndClosesMenus()
    {
        var vm = CreateViewModel();
        vm.IsAudioMenuOpen = true;
        vm.IsSubtitleMenuOpen = true;

        var audioTrack = new PlayerTrackInfo(2, "English 5.1", "en", false);
        vm.SelectAudioTrackCommand.Execute(audioTrack);
        Assert.Equal(2, _fakePlayer.LastAudioTrackId);
        Assert.False(vm.IsAudioMenuOpen);

        var subTrack = new PlayerTrackInfo(1, "English CC", "en", false);
        vm.SelectSubtitleTrackCommand.Execute(subTrack);
        Assert.Equal(1, _fakePlayer.LastSubtitleTrackId);
        Assert.False(vm.IsSubtitleMenuOpen);
    }
}

/// <summary>
/// Test fake implementing <see cref="IVlcPlayerService"/> for deterministic ViewModel testing.
/// </summary>
internal sealed class FakeVlcPlayerService : IVlcPlayerService
{
    public bool IsInitialized => true;
    public PlayerState State { get; set; } = PlayerState.Idle;
    public bool IsPlaying => State == PlayerState.Playing;
    public long PositionMs { get; set; }
    public long DurationMs { get; set; }
    public float PositionRatio { get; set; }
    public int Volume { get; set; } = 100;
    public bool IsMuted { get; set; }

    public string? LastOpenedFile { get; private set; }
    public long LastStartPositionMs { get; private set; }
    public bool TogglePlayPauseCalled { get; private set; }
    public long LastSeekRelativeDelta { get; private set; }
    public bool StopCalled { get; private set; }
    public int LastAudioTrackId { get; private set; } = -1;
    public int LastSubtitleTrackId { get; private set; } = -1;

    public IReadOnlyList<PlayerTrackInfo> AudioTracks { get; set; } = Array.Empty<PlayerTrackInfo>();
    public IReadOnlyList<PlayerTrackInfo> SubtitleTracks { get; set; } = Array.Empty<PlayerTrackInfo>();
    public int CurrentAudioTrackId => LastAudioTrackId;
    public int CurrentSubtitleTrackId => LastSubtitleTrackId;

#pragma warning disable CS0067
    public event Action<PlayerState>? StateChanged;
    public event Action<long, float>? PositionChanged;
    public event Action<long>? DurationChanged;
    public event Action? TracksChanged;
    public event Action<string>? PlaybackError;
    public event Action? MediaEnded;
#pragma warning restore CS0067

    public Task<bool> OpenMediaAsync(string filePath, long startPositionMs = 0)
    {
        LastOpenedFile = filePath;
        LastStartPositionMs = startPositionMs;
        State = PlayerState.Playing;
        StateChanged?.Invoke(State);
        return Task.FromResult(true);
    }

    public void Play()
    {
        State = PlayerState.Playing;
        StateChanged?.Invoke(State);
    }

    public void Pause()
    {
        State = PlayerState.Paused;
        StateChanged?.Invoke(State);
    }

    public void TogglePlayPause()
    {
        TogglePlayPauseCalled = true;
        State = State == PlayerState.Playing ? PlayerState.Paused : PlayerState.Playing;
        StateChanged?.Invoke(State);
    }

    public void Stop()
    {
        StopCalled = true;
        State = PlayerState.Stopped;
        StateChanged?.Invoke(State);
    }

    public void Seek(long positionMs)
    {
        PositionMs = positionMs;
        PositionChanged?.Invoke(PositionMs, PositionRatio);
    }

    public void SeekRelative(long deltaMs)
    {
        LastSeekRelativeDelta = deltaMs;
    }

    public void SetVolume(int volume)
    {
        Volume = volume;
    }

    public void ToggleMute()
    {
        IsMuted = !IsMuted;
    }

    public bool SetAudioTrack(int trackId)
    {
        LastAudioTrackId = trackId;
        return true;
    }

    public bool SetSubtitleTrack(int trackId)
    {
        LastSubtitleTrackId = trackId;
        return true;
    }

    public void Dispose()
    {
        Stop();
    }
}
