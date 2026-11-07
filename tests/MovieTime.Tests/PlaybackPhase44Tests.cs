using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.ViewModels;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Playback;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class PlaybackPhase44Tests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _databaseService;
    private readonly MovieRepository _movieRepository;
    private readonly MediaFileRepository _mediaFileRepository;
    private readonly PlaybackProgressRepository _progressRepository;
    private readonly FakeVlcPlayerService _fakePlayer;
    private readonly FakeFilePickerService _fakePicker;

    public PlaybackPhase44Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PlaybackPhase44Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_phase44.db");

        var storage = new StoragePathService(_tempDir);
        _databaseService = new DatabaseService(storage, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _movieRepository = new MovieRepository(_databaseService);
        _mediaFileRepository = new MediaFileRepository(_databaseService);
        _progressRepository = new PlaybackProgressRepository(_databaseService);
        _fakePlayer = new FakeVlcPlayerService();
        _fakePicker = new FakeFilePickerService();
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

    private PlayerViewModel CreateViewModel()
    {
        return new PlayerViewModel(
            _fakePlayer,
            _movieRepository,
            _mediaFileRepository,
            _progressRepository,
            NullLogger<PlayerViewModel>.Instance,
            dispatcher: null,
            filePickerService: _fakePicker);
    }

    [Fact]
    public void YouTubeShortcuts_TogglePlayPause_InvokesPlayerService()
    {
        var vm = CreateViewModel();

        vm.PlayPauseCommand.Execute(null);

        Assert.True(_fakePlayer.TogglePlayPauseCalled);
    }

    [Fact]
    public void YouTubeShortcuts_ToggleMute_InvokesPlayerService()
    {
        var vm = CreateViewModel();
        Assert.False(vm.IsMuted);

        vm.ToggleMuteCommand.Execute(null);

        Assert.True(vm.IsMuted);
        Assert.True(_fakePlayer.IsMuted);

        vm.ToggleMuteCommand.Execute(null);

        Assert.False(vm.IsMuted);
        Assert.False(_fakePlayer.IsMuted);
    }

    [Fact]
    public void YouTubeShortcuts_SeekRelative_10Seconds_For_J_And_L()
    {
        var vm = CreateViewModel();
        _fakePlayer.SimulateDurationChanged(100_000); // 100 seconds
        vm.SeekToRatio(0.5); // 50s

        // Seek forward 10s (L key)
        vm.SeekForwardCommand.Execute(null);
        Assert.Equal(60_000, vm.PositionMs);
        Assert.Equal(10_000, _fakePlayer.LastSeekRelativeDelta);

        // Seek backward 10s (J key)
        vm.SeekBackwardCommand.Execute(null);
        Assert.Equal(50_000, vm.PositionMs);
        Assert.Equal(-10_000, _fakePlayer.LastSeekRelativeDelta);
    }

    [Fact]
    public void YouTubeShortcuts_SeekRelative_5Seconds_For_Left_And_Right_Arrows()
    {
        var vm = CreateViewModel();
        _fakePlayer.SimulateDurationChanged(100_000); // 100 seconds
        vm.SeekToRatio(0.5); // 50s

        // Seek forward 5s (Right Arrow)
        vm.SeekForward5sCommand.Execute(null);
        Assert.Equal(55_000, vm.PositionMs);
        Assert.Equal(5_000, _fakePlayer.LastSeekRelativeDelta);

        // Seek backward 5s (Left Arrow)
        vm.SeekBackward5sCommand.Execute(null);
        Assert.Equal(50_000, vm.PositionMs);
        Assert.Equal(-5_000, _fakePlayer.LastSeekRelativeDelta);
    }

    [Theory]
    [InlineData(0, 0)]       // 0 key -> 0%
    [InlineData(1, 10_000)]  // 1 key -> 10%
    [InlineData(2, 20_000)]  // 2 key -> 20%
    [InlineData(5, 50_000)]  // 5 key -> 50%
    [InlineData(9, 90_000)]  // 9 key -> 90%
    public void YouTubeShortcuts_SeekToPercentage_ValidDigits_SeeksAccurately(int digit, long expectedPositionMs)
    {
        var vm = CreateViewModel();
        _fakePlayer.SimulateDurationChanged(100_000); // 100 seconds

        vm.SeekToPercentage(digit * 10);

        Assert.Equal(expectedPositionMs, vm.PositionMs);
        Assert.Equal(expectedPositionMs, _fakePlayer.PositionMs);
        Assert.Equal((double)expectedPositionMs / 100_000, vm.ProgressRatio, 3);
    }

    [Fact]
    public void YouTubeShortcuts_SeekToPercentage_WithZeroDuration_DoesNotFailOrThrow()
    {
        var vm = CreateViewModel();
        _fakePlayer.SimulateDurationChanged(0);

        vm.SeekToPercentage(50);

        Assert.Equal(0, vm.PositionMs);
    }

    [Fact]
    public async Task ExternalAudio_LoadAndRemove_UpdatesViewModelAndService()
    {
        var vm = CreateViewModel();
        var fakeAudioPath = @"C:\Media\Movie.audio.ita.mka";
        _fakePicker.FileToReturn = fakeAudioPath;

        // 1. Load external audio
        await vm.LoadExternalAudioAsync();

        Assert.Equal(fakeAudioPath, vm.ExternalAudioPath);
        Assert.Equal("Movie.audio.ita.mka", vm.ExternalAudioFileName);
        Assert.True(vm.HasExternalAudio);
        Assert.Equal(fakeAudioPath, _fakePlayer.ExternalAudioPath);

        // 2. Remove external audio
        await vm.RemoveExternalAudioAsync();

        Assert.Null(vm.ExternalAudioPath);
        Assert.Equal("None", vm.ExternalAudioFileName);
        Assert.False(vm.HasExternalAudio);
        Assert.Null(_fakePlayer.ExternalAudioPath);
    }

    [Fact]
    public void AudioDelay_AdjustAndReset_AppliesDeltaAndFormatting()
    {
        var vm = CreateViewModel();
        Assert.Equal(0, vm.AudioDelayMs);
        Assert.Equal("0 ms", vm.AudioDelayDisplay);

        // Adjust +100ms
        vm.AdjustAudioDelay(100);
        Assert.Equal(100, vm.AudioDelayMs);
        Assert.Equal("+100 ms", vm.AudioDelayDisplay);
        Assert.Equal(100, _fakePlayer.AudioDelayMs);

        // Adjust -200ms -> -100ms total
        vm.AdjustAudioDelay(-200);
        Assert.Equal(-100, vm.AudioDelayMs);
        Assert.Equal("-100 ms", vm.AudioDelayDisplay);
        Assert.Equal(-100, _fakePlayer.AudioDelayMs);

        // Reset to 0
        vm.ResetAudioDelayCommand.Execute(null);
        Assert.Equal(0, vm.AudioDelayMs);
        Assert.Equal("0 ms", vm.AudioDelayDisplay);
        Assert.Equal(0, _fakePlayer.AudioDelayMs);
    }

    [Fact]
    public async Task ExternalSubtitle_LoadAndRemove_UpdatesViewModelAndService()
    {
        var vm = CreateViewModel();
        var fakeSubPath = @"C:\Media\Movie.spa.srt";
        _fakePicker.FileToReturn = fakeSubPath;

        // 1. Load external subtitle
        await vm.LoadExternalSubtitleAsync();

        Assert.Equal(fakeSubPath, vm.ExternalSubtitlePath);
        Assert.Equal("Movie.spa.srt", vm.ExternalSubtitleFileName);
        Assert.True(vm.HasExternalSubtitle);
        Assert.Equal(fakeSubPath, _fakePlayer.ExternalSubtitlePath);

        // 2. Remove external subtitle
        await vm.RemoveExternalSubtitleAsync();

        Assert.Null(vm.ExternalSubtitlePath);
        Assert.Equal("None", vm.ExternalSubtitleFileName);
        Assert.False(vm.HasExternalSubtitle);
        Assert.Null(_fakePlayer.ExternalSubtitlePath);
    }

    [Fact]
    public async Task SubtitleAppearance_CustomizationCommands_UpdateSettingsAndNotifyService()
    {
        var vm = CreateViewModel();

        // 1. Font Size
        await vm.SetSubtitleFontSizeAsync(SubtitleFontSize.Large);
        Assert.Equal(SubtitleFontSize.Large, vm.SubtitleFontSize);
        Assert.Equal(SubtitleFontSize.Large, _fakePlayer.CurrentSubtitleSettings.FontSize);

        // 2. Color
        await vm.SetSubtitleColorAsync(SubtitleColor.Yellow);
        Assert.Equal(SubtitleColor.Yellow, vm.SubtitleColor);
        Assert.Equal(SubtitleColor.Yellow, _fakePlayer.CurrentSubtitleSettings.Color);

        // 3. Outline
        await vm.SetSubtitleOutlineAsync(SubtitleOutline.Thick);
        Assert.Equal(SubtitleOutline.Thick, vm.SubtitleOutline);
        Assert.Equal(SubtitleOutline.Thick, _fakePlayer.CurrentSubtitleSettings.Outline);

        // 4. Background
        await vm.SetSubtitleBackgroundAsync(SubtitleBackground.Dark);
        Assert.Equal(SubtitleBackground.Dark, vm.SubtitleBackground);
        Assert.Equal(SubtitleBackground.Dark, _fakePlayer.CurrentSubtitleSettings.Background);

        // 5. Reset to defaults
        await vm.ResetSubtitleSettingsAsync();
        Assert.Equal(SubtitleFontSize.Medium, vm.SubtitleFontSize);
        Assert.Equal(SubtitleColor.White, vm.SubtitleColor);
        Assert.Equal(SubtitleOutline.Medium, vm.SubtitleOutline);
        Assert.Equal(SubtitleBackground.Off, vm.SubtitleBackground);
    }

    [Fact]
    public void SubtitleSettings_FreetypeHelpers_ProvideAccurateValues()
    {
        var settings = new SubtitleSettings
        {
            FontSize = SubtitleFontSize.ExtraLarge,
            Color = SubtitleColor.Cyan,
            Outline = SubtitleOutline.Thin,
            Background = SubtitleBackground.Subtle,
            Position = SubtitlePosition.Higher
        };

        Assert.Equal(52, settings.GetFontSizePoints());
        Assert.Equal(0x0000FFFF, settings.GetColorRgb());
        Assert.Equal("#00E5FF", settings.GetColorHex());
        Assert.Equal(1, settings.GetOutlineThickness());
        Assert.Equal(90, settings.GetBackgroundOpacity());
        Assert.Equal(45, settings.GetSubMarginPixels());
    }

    [Fact]
    public async Task SubtitleSettingsService_SavesAndLoadsCorrectly()
    {
        var storage = new StoragePathService(_tempDir);
        var service = new SubtitleSettingsService(storage, NullLogger<SubtitleSettingsService>.Instance);

        var original = new SubtitleSettings
        {
            FontSize = SubtitleFontSize.Large,
            Color = SubtitleColor.Green,
            Outline = SubtitleOutline.Off,
            Background = SubtitleBackground.Dark,
            Position = SubtitlePosition.Lower
        };

        await service.SaveSettingsAsync(original);

        var loaded = service.GetSettings();

        Assert.Equal(SubtitleFontSize.Large, loaded.FontSize);
        Assert.Equal(SubtitleColor.Green, loaded.Color);
        Assert.Equal(SubtitleOutline.Off, loaded.Outline);
        Assert.Equal(SubtitleBackground.Dark, loaded.Background);
        Assert.Equal(SubtitlePosition.Lower, loaded.Position);
    }
}

internal sealed class FakeFilePickerService : IFilePickerService
{
    public string? FileToReturn { get; set; }
    public string? LastTitle { get; private set; }
    public string[]? LastExtensions { get; private set; }

    public Task<string?> PickOpenFileAsync(string title, string[] fileTypeExtensions)
    {
        LastTitle = title;
        LastExtensions = fileTypeExtensions;
        return Task.FromResult(FileToReturn);
    }
}
