using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.Common;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Playback;
using MovieTime.Core.Services;

namespace MovieTime.App.ViewModels;

/// <summary>
/// ViewModel managing the Apple TV-inspired native player HUD, transport controls,
/// timeline scrubbing, track selection, and progress synchronization.
/// </summary>
public sealed class PlayerViewModel : ObservableObject, IDisposable
{
    private readonly IVlcPlayerService _playerService;
    private readonly IMovieRepository _movieRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IPlaybackProgressRepository _progressRepository;
    private readonly IEpisodeRepository? _episodeRepository;
    private readonly ISeriesRepository? _seriesRepository;
    private readonly ISeasonRepository? _seasonRepository;
    private readonly IFilePickerService? _filePickerService;
    private readonly ILogger<PlayerViewModel> _logger;
    private readonly IDispatcherService? _dispatcher;

    private int _movieId;
    private int _episodeId;
    private int _seriesId;
    private int _mediaFileId;
    private string _filePath = string.Empty;
    private string _movieTitle = string.Empty;
    private string _contextSubtitle = string.Empty;
    private string _currentTimeDisplay = "00:00";
    private string _remainingTimeDisplay = "-00:00";
    private string _durationDisplay = "00:00";
    private double _progressRatio;
    private long _positionMs;
    private long _durationMs;
    private bool _isPlaying;
    private bool _isMuted;
    private int _volume = 100;
    private bool _isControlsVisible = true;
    private bool _isAudioMenuOpen;
    private bool _isSubtitleMenuOpen;
    private bool _isInfoOpen;
    private bool _isFullscreen;
    private bool _hasError;
    private string? _errorMessage;
    private bool _isDraggingSlider;
    private bool _isUpdatingFromPlayer;
    private string? _externalAudioPath;
    private string? _externalSubtitlePath;
    private long _audioDelayMs;
    private SubtitleSettings _subtitleSettings = new();
    private bool _isSubtitleAppearanceOpen;
    private bool _isKeyboardShortcutsOpen;

    public bool IsDraggingSlider => _isDraggingSlider;
    public bool IsUpdatingFromPlayer => _isUpdatingFromPlayer;

    // Info card metadata
    private string? _resolutionDisplay;
    private string? _videoCodecDisplay;
    private string? _audioCodecDisplay;
    private string? _formatDisplay;
    private string? _fileSizeDisplay;

    private DateTime _lastSavedProgressTime = DateTime.MinValue;

    public event Action<string>? NavigationRequested;
    public event Action<bool>? FullscreenToggled;

    public int MovieId => _movieId;
    public int EpisodeId => _episodeId;
    public int MediaFileId => _mediaFileId;
    public string FilePath => _filePath;

    public string MovieTitle
    {
        get => _movieTitle;
        set => SetProperty(ref _movieTitle, value);
    }

    public string ContextSubtitle
    {
        get => _contextSubtitle;
        set => SetProperty(ref _contextSubtitle, value);
    }

    public string CurrentTimeDisplay
    {
        get => _currentTimeDisplay;
        set => SetProperty(ref _currentTimeDisplay, value);
    }

    public string RemainingTimeDisplay
    {
        get => _remainingTimeDisplay;
        set => SetProperty(ref _remainingTimeDisplay, value);
    }

    public string DurationDisplay
    {
        get => _durationDisplay;
        set => SetProperty(ref _durationDisplay, value);
    }

    public double ProgressRatio
    {
        get => _progressRatio;
        set
        {
            if (SetProperty(ref _progressRatio, value))
            {
                OnPropertyChanged(nameof(ProgressPercent));
            }
        }
    }

    public double ProgressPercent => ProgressRatio * 100.0;

    public long PositionMs
    {
        get => _positionMs;
        private set => SetProperty(ref _positionMs, value);
    }

    public long DurationMs
    {
        get => _durationMs;
        private set => SetProperty(ref _durationMs, value);
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(IsPaused));
                OnPropertyChanged(nameof(PlayPauseGlyph));
            }
        }
    }

    public bool IsPaused => !IsPlaying;
    public string PlayPauseGlyph => IsPlaying ? "\uE769" : "\uE768"; // Pause vs Play

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (SetProperty(ref _isMuted, value))
            {
                OnPropertyChanged(nameof(VolumeGlyph));
            }
        }
    }

    public int Volume
    {
        get => _volume;
        set
        {
            if (SetProperty(ref _volume, value))
            {
                OnPropertyChanged(nameof(VolumeGlyph));
            }
        }
    }

    public string VolumeGlyph => IsMuted || Volume == 0 ? "\uE74F" : Volume < 50 ? "\uE993" : "\uE995";

    public bool IsControlsVisible
    {
        get => _isControlsVisible;
        set => SetProperty(ref _isControlsVisible, value);
    }

    public bool IsAudioMenuOpen
    {
        get => _isAudioMenuOpen;
        set
        {
            if (SetProperty(ref _isAudioMenuOpen, value) && value)
            {
                IsSubtitleMenuOpen = false;
                IsInfoOpen = false;
                IsControlsVisible = true;
            }
        }
    }

    public bool IsSubtitleMenuOpen
    {
        get => _isSubtitleMenuOpen;
        set
        {
            if (SetProperty(ref _isSubtitleMenuOpen, value) && value)
            {
                IsAudioMenuOpen = false;
                IsInfoOpen = false;
                IsControlsVisible = true;
            }
        }
    }

    public bool IsInfoOpen
    {
        get => _isInfoOpen;
        set
        {
            if (SetProperty(ref _isInfoOpen, value) && value)
            {
                IsAudioMenuOpen = false;
                IsSubtitleMenuOpen = false;
                IsControlsVisible = true;
            }
        }
    }

    public bool IsFullscreen
    {
        get => _isFullscreen;
        set => SetProperty(ref _isFullscreen, value);
    }

    public bool HasError
    {
        get => _hasError;
        set => SetProperty(ref _hasError, value);
    }

    public string? ErrorMessage
    {
        get => _errorMessage;
        set => SetProperty(ref _errorMessage, value);
    }

    public string? ResolutionDisplay
    {
        get => _resolutionDisplay;
        set => SetProperty(ref _resolutionDisplay, value);
    }

    public string? VideoCodecDisplay
    {
        get => _videoCodecDisplay;
        set => SetProperty(ref _videoCodecDisplay, value);
    }

    public string? AudioCodecDisplay
    {
        get => _audioCodecDisplay;
        set => SetProperty(ref _audioCodecDisplay, value);
    }

    public string? FormatDisplay
    {
        get => _formatDisplay;
        set => SetProperty(ref _formatDisplay, value);
    }

    public string? FileSizeDisplay
    {
        get => _fileSizeDisplay;
        set => SetProperty(ref _fileSizeDisplay, value);
    }

    public ObservableCollection<PlayerTrackInfo> AudioTracks { get; } = new();
    public ObservableCollection<PlayerTrackInfo> SubtitleTracks { get; } = new();

    public string? ExternalAudioPath
    {
        get => _externalAudioPath;
        private set
        {
            if (SetProperty(ref _externalAudioPath, value))
            {
                OnPropertyChanged(nameof(ExternalAudioFileName));
                OnPropertyChanged(nameof(HasExternalAudio));
            }
        }
    }

    public string ExternalAudioFileName => !string.IsNullOrEmpty(_externalAudioPath) ? Path.GetFileName(_externalAudioPath) : "None";
    public bool HasExternalAudio => !string.IsNullOrEmpty(_externalAudioPath);

    public string? ExternalSubtitlePath
    {
        get => _externalSubtitlePath;
        private set
        {
            if (SetProperty(ref _externalSubtitlePath, value))
            {
                OnPropertyChanged(nameof(ExternalSubtitleFileName));
                OnPropertyChanged(nameof(HasExternalSubtitle));
            }
        }
    }

    public string ExternalSubtitleFileName => !string.IsNullOrEmpty(_externalSubtitlePath) ? Path.GetFileName(_externalSubtitlePath) : "None";
    public bool HasExternalSubtitle => !string.IsNullOrEmpty(_externalSubtitlePath);

    public long AudioDelayMs
    {
        get => _audioDelayMs;
        private set
        {
            if (SetProperty(ref _audioDelayMs, value))
            {
                OnPropertyChanged(nameof(AudioDelayDisplay));
            }
        }
    }

    public string AudioDelayDisplay => _audioDelayMs == 0 ? "0 ms" : _audioDelayMs > 0 ? $"+{_audioDelayMs} ms" : $"{_audioDelayMs} ms";

    public SubtitleSettings SubtitleSettings => _subtitleSettings;
    public SubtitleFontSize SubtitleFontSize => _subtitleSettings.FontSize;
    public SubtitleColor SubtitleColor => _subtitleSettings.Color;
    public SubtitleOutline SubtitleOutline => _subtitleSettings.Outline;
    public SubtitleBackground SubtitleBackground => _subtitleSettings.Background;
    public SubtitlePosition SubtitlePosition => _subtitleSettings.Position;

    public bool IsSubtitleAppearanceOpen
    {
        get => _isSubtitleAppearanceOpen;
        set => SetProperty(ref _isSubtitleAppearanceOpen, value);
    }

    public bool IsKeyboardShortcutsOpen
    {
        get => _isKeyboardShortcutsOpen;
        set => SetProperty(ref _isKeyboardShortcutsOpen, value);
    }

    public ICommand PlayPauseCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand SeekForward5sCommand { get; }
    public ICommand SeekBackward5sCommand { get; }
    public ICommand SeekToPercentageCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public ICommand ToggleAudioMenuCommand { get; }
    public ICommand ToggleSubtitleMenuCommand { get; }
    public ICommand ToggleInfoCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SelectAudioTrackCommand { get; }
    public ICommand SelectSubtitleTrackCommand { get; }
    public ICommand AdjustAudioDelayCommand { get; }
    public ICommand ResetAudioDelayCommand { get; }
    public ICommand LoadExternalAudioCommand { get; }
    public ICommand RemoveExternalAudioCommand { get; }
    public ICommand LoadExternalSubtitleCommand { get; }
    public ICommand RemoveExternalSubtitleCommand { get; }
    public ICommand ToggleSubtitleAppearanceCommand { get; }
    public ICommand ToggleKeyboardShortcutsCommand { get; }
    public ICommand SetSubtitleFontSizeCommand { get; }
    public ICommand SetSubtitleColorCommand { get; }
    public ICommand SetSubtitleOutlineCommand { get; }
    public ICommand SetSubtitleBackgroundCommand { get; }
    public ICommand SetSubtitlePositionCommand { get; }
    public ICommand ResetSubtitleSettingsCommand { get; }

    public PlayerViewModel(
        IVlcPlayerService playerService,
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        IPlaybackProgressRepository progressRepository,
        ILogger<PlayerViewModel>? logger = null,
        IDispatcherService? dispatcher = null,
        IFilePickerService? filePickerService = null,
        IEpisodeRepository? episodeRepository = null,
        ISeriesRepository? seriesRepository = null,
        ISeasonRepository? seasonRepository = null)
    {
        _playerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _progressRepository = progressRepository ?? throw new ArgumentNullException(nameof(progressRepository));
        _episodeRepository = episodeRepository;
        _seriesRepository = seriesRepository;
        _seasonRepository = seasonRepository;
        _logger = logger ?? NullLogger<PlayerViewModel>.Instance;
        _dispatcher = dispatcher;
        _filePickerService = filePickerService;
        _subtitleSettings = _playerService.CurrentSubtitleSettings ?? new SubtitleSettings();

        PlayPauseCommand = new RelayCommand(OnPlayPause);
        SeekForwardCommand = new RelayCommand(() => SeekRelative(10000));
        SeekBackwardCommand = new RelayCommand(() => SeekRelative(-10000));
        SeekForward5sCommand = new RelayCommand(() => SeekRelative(5000));
        SeekBackward5sCommand = new RelayCommand(() => SeekRelative(-5000));
        SeekToPercentageCommand = new RelayCommand<int>(SeekToPercentage);
        ToggleMuteCommand = new RelayCommand(OnToggleMute);
        ToggleFullscreenCommand = new RelayCommand(OnToggleFullscreen);
        ToggleAudioMenuCommand = new RelayCommand(() => IsAudioMenuOpen = !IsAudioMenuOpen);
        ToggleSubtitleMenuCommand = new RelayCommand(() => IsSubtitleMenuOpen = !IsSubtitleMenuOpen);
        ToggleInfoCommand = new RelayCommand(() => IsInfoOpen = !IsInfoOpen);
        BackCommand = new RelayCommand(OnBack);
        SelectAudioTrackCommand = new RelayCommand<PlayerTrackInfo>(SelectAudioTrack);
        SelectSubtitleTrackCommand = new RelayCommand<PlayerTrackInfo>(SelectSubtitleTrack);

        AdjustAudioDelayCommand = new RelayCommand<long>(delta => AdjustAudioDelay(delta));
        ResetAudioDelayCommand = new RelayCommand(() => SetAudioDelay(0));
        LoadExternalAudioCommand = new RelayCommand(async () => await LoadExternalAudioAsync());
        RemoveExternalAudioCommand = new RelayCommand(async () => await RemoveExternalAudioAsync());
        LoadExternalSubtitleCommand = new RelayCommand(async () => await LoadExternalSubtitleAsync());
        RemoveExternalSubtitleCommand = new RelayCommand(async () => await RemoveExternalSubtitleAsync());
        ToggleSubtitleAppearanceCommand = new RelayCommand(() => IsSubtitleAppearanceOpen = !IsSubtitleAppearanceOpen);
        ToggleKeyboardShortcutsCommand = new RelayCommand(() => IsKeyboardShortcutsOpen = !IsKeyboardShortcutsOpen);

        SetSubtitleFontSizeCommand = new RelayCommand<SubtitleFontSize>(async s => await SetSubtitleFontSizeAsync(s));
        SetSubtitleColorCommand = new RelayCommand<SubtitleColor>(async c => await SetSubtitleColorAsync(c));
        SetSubtitleOutlineCommand = new RelayCommand<SubtitleOutline>(async o => await SetSubtitleOutlineAsync(o));
        SetSubtitleBackgroundCommand = new RelayCommand<SubtitleBackground>(async b => await SetSubtitleBackgroundAsync(b));
        SetSubtitlePositionCommand = new RelayCommand<SubtitlePosition>(async p => await SetSubtitlePositionAsync(p));
        ResetSubtitleSettingsCommand = new RelayCommand(async () => await ResetSubtitleSettingsAsync());

        HookPlayerEvents();
    }

    private void HookPlayerEvents()
    {
        _playerService.StateChanged += OnPlayerStateChanged;
        _playerService.PositionChanged += OnPlayerPositionChanged;
        _playerService.DurationChanged += OnPlayerDurationChanged;
        _playerService.TracksChanged += OnPlayerTracksChanged;
        _playerService.PlaybackError += OnPlayerPlaybackError;
        _playerService.MediaEnded += OnPlayerMediaEnded;
        _playerService.AudioDelayChanged += OnPlayerAudioDelayChanged;
        _playerService.SubtitleSettingsChanged += OnPlayerSubtitleSettingsChanged;
    }

    private void UnhookPlayerEvents()
    {
        _playerService.StateChanged -= OnPlayerStateChanged;
        _playerService.PositionChanged -= OnPlayerPositionChanged;
        _playerService.DurationChanged -= OnPlayerDurationChanged;
        _playerService.TracksChanged -= OnPlayerTracksChanged;
        _playerService.PlaybackError -= OnPlayerPlaybackError;
        _playerService.MediaEnded -= OnPlayerMediaEnded;
        _playerService.AudioDelayChanged -= OnPlayerAudioDelayChanged;
        _playerService.SubtitleSettingsChanged -= OnPlayerSubtitleSettingsChanged;
    }

    public async Task InitializeAsync(string filePath, int? movieId = null, int? episodeId = null)
    {
        _filePath = filePath ?? string.Empty;
        _movieId = movieId ?? 0;
        _episodeId = episodeId ?? 0;
        HasError = false;
        ErrorMessage = null;
        IsControlsVisible = true;

        if (string.IsNullOrWhiteSpace(_filePath) || !File.Exists(_filePath))
        {
            HasError = true;
            ErrorMessage = $"Local media file could not be found:\n{_filePath}";
            _logger.LogWarning("Playback failed: file does not exist: {Path}", _filePath);
            return;
        }

        // 1. Resolve Movie, TV Episode, and MediaFile metadata from SQLite
        await ResolveMetadataAsync();

        // 2. Check for saved resume position in SQLite
        long resumePositionMs = 0;
        if (_mediaFileId > 0)
        {
            var saved = await _progressRepository.GetByMediaFileIdAsync(_mediaFileId);
            if (saved != null && !saved.IsFinished && saved.DurationTicks > 0)
            {
                var savedMs = saved.PositionTicks / TimeSpan.TicksPerMillisecond;
                var totalMs = saved.DurationTicks / TimeSpan.TicksPerMillisecond;

                // Resume if watched > 10 seconds and more than 30 seconds before the end
                if (savedMs > 10_000 && (totalMs <= 0 || savedMs < totalMs - 30_000))
                {
                    resumePositionMs = savedMs;
                    _logger.LogInformation("Resuming media from saved progress: {Seconds}s", resumePositionMs / 1000);
                }
            }
        }

        // 3. Open media with native LibVLC
        var opened = await _playerService.OpenMediaAsync(_filePath, resumePositionMs);
        if (!opened)
        {
            HasError = true;
            ErrorMessage = "LibVLC was unable to open or decode this video file.";
        }
    }

    private async Task ResolveMetadataAsync()
    {
        Movie? movie = null;
        Episode? episode = null;

        if (_movieId > 0)
        {
            movie = await _movieRepository.GetByIdAsync(_movieId);
        }

        if (_episodeId > 0 && _episodeRepository != null)
        {
            episode = await _episodeRepository.GetByIdAsync(_episodeId);
        }

        MediaFile? mediaFile = null;
        if (!string.IsNullOrWhiteSpace(_filePath))
        {
            mediaFile = await _mediaFileRepository.GetByPathAsync(_filePath);
            if (mediaFile != null)
            {
                _mediaFileId = mediaFile.Id;
                if (movie == null && mediaFile.MovieId.HasValue)
                {
                    _movieId = mediaFile.MovieId.Value;
                    movie = await _movieRepository.GetByIdAsync(_movieId);
                }
                if (episode == null && mediaFile.EpisodeId.HasValue && _episodeRepository != null)
                {
                    _episodeId = mediaFile.EpisodeId.Value;
                    episode = await _episodeRepository.GetByIdAsync(_episodeId);
                }
            }
        }

        if (episode != null)
        {
            _seriesId = episode.SeriesId;
            Season? season = _seasonRepository != null ? await _seasonRepository.GetByIdAsync(episode.SeasonId) : null;
            Series? series = _seriesRepository != null ? await _seriesRepository.GetByIdAsync(episode.SeriesId) : null;

            MovieTitle = series?.Title ?? "TV Episode";
            var sNum = season?.SeasonNumber ?? 1;
            var parts = new System.Collections.Generic.List<string>
            {
                $"Season {sNum}, Episode {episode.EpisodeNumber} • {episode.Title}"
            };

            if (mediaFile != null)
            {
                if (mediaFile.VideoHeight >= 2160 || mediaFile.VideoWidth >= 3840) parts.Add("4K UHD");
                else if (mediaFile.VideoHeight >= 1080 || mediaFile.VideoWidth >= 1920) parts.Add("1080p HD");
                else if (mediaFile.VideoHeight >= 720 || mediaFile.VideoWidth >= 1280) parts.Add("720p HD");

                if (!string.IsNullOrWhiteSpace(mediaFile.VideoCodec)) parts.Add(mediaFile.VideoCodec.ToUpperInvariant());
            }

            ContextSubtitle = string.Join(" • ", parts);
        }
        else if (movie != null)
        {
            MovieTitle = movie.Title;
            var parts = new System.Collections.Generic.List<string>();
            if (movie.ReleaseYear.HasValue && movie.ReleaseYear.Value > 0) parts.Add(movie.ReleaseYear.Value.ToString());
            if (!string.IsNullOrWhiteSpace(movie.Edition)) parts.Add(movie.Edition);

            if (mediaFile != null)
            {
                if (mediaFile.VideoHeight >= 2160 || mediaFile.VideoWidth >= 3840) parts.Add("4K UHD");
                else if (mediaFile.VideoHeight >= 1080 || mediaFile.VideoWidth >= 1920) parts.Add("1080p HD");
                else if (mediaFile.VideoHeight >= 720 || mediaFile.VideoWidth >= 1280) parts.Add("720p HD");

                if (!string.IsNullOrWhiteSpace(mediaFile.VideoCodec)) parts.Add(mediaFile.VideoCodec.ToUpperInvariant());
            }

            ContextSubtitle = string.Join(" • ", parts);
        }
        else
        {
            MovieTitle = Path.GetFileNameWithoutExtension(_filePath);
            ContextSubtitle = Path.GetExtension(_filePath).TrimStart('.').ToUpperInvariant();
        }

        // Info details
        if (mediaFile != null)
        {
            ResolutionDisplay = mediaFile.VideoWidth.HasValue && mediaFile.VideoHeight.HasValue
                ? $"{mediaFile.VideoWidth} x {mediaFile.VideoHeight}"
                : "Unknown";
            VideoCodecDisplay = mediaFile.VideoCodec ?? "Unknown";
            AudioCodecDisplay = mediaFile.AudioCodec ?? "Unknown";
            FormatDisplay = mediaFile.ContainerFormat?.ToUpperInvariant() ?? Path.GetExtension(_filePath).TrimStart('.').ToUpperInvariant();
            if (mediaFile.SizeBytes > 0)
            {
                double gb = mediaFile.SizeBytes / (1024.0 * 1024.0 * 1024.0);
                FileSizeDisplay = $"{gb:F1} GB";
            }
        }
    }

    public void OnPlayPause()
    {
        _playerService.TogglePlayPause();
    }

    public void BeginSeek()
    {
        _isDraggingSlider = true;
        TriggerInteraction();
    }

    public void SeekPreview(double ratio)
    {
        _isDraggingSlider = true;
        ratio = Math.Clamp(ratio, 0.0, 1.0);
        ProgressRatio = ratio;

        if (_durationMs > 0)
        {
            var previewMs = (long)Math.Round(ratio * _durationMs);
            PositionMs = previewMs;
            CurrentTimeDisplay = FormatTime(previewMs);
            var remaining = Math.Max(0, _durationMs - previewMs);
            RemainingTimeDisplay = $"-{FormatTime(remaining)}";

            // Fluid live video scrubbing via non-blocking coalesced background seek
            _playerService.Seek(previewMs);
        }

        TriggerInteraction();
    }

    public void EndSeek(double ratio)
    {
        _isDraggingSlider = false;
        SeekToRatio(ratio);
    }

    public void SeekToRatio(double ratio)
    {
        ratio = Math.Clamp(ratio, 0.0, 1.0);
        ProgressRatio = ratio;

        if (_durationMs > 0)
        {
            var targetMs = (long)Math.Round(ratio * _durationMs);
            PositionMs = targetMs;
            CurrentTimeDisplay = FormatTime(targetMs);
            var remaining = Math.Max(0, _durationMs - targetMs);
            RemainingTimeDisplay = $"-{FormatTime(remaining)}";
            _playerService.Seek(targetMs);
        }

        TriggerInteraction();
    }

    public void SeekRelative(long deltaMs)
    {
        if (_durationMs > 0)
        {
            var newPos = Math.Clamp(_positionMs + deltaMs, 0, _durationMs);
            PositionMs = newPos;
            CurrentTimeDisplay = FormatTime(newPos);
            var remaining = Math.Max(0, _durationMs - newPos);
            RemainingTimeDisplay = $"-{FormatTime(remaining)}";
            ProgressRatio = (double)newPos / _durationMs;
        }

        _playerService.SeekRelative(deltaMs);
        TriggerInteraction();
    }

    public void OnToggleMute()
    {
        _playerService.ToggleMute();
        IsMuted = _playerService.IsMuted;
        TriggerInteraction();
    }

    public void SetVolume(int volume)
    {
        _playerService.SetVolume(volume);
        Volume = _playerService.Volume;
        IsMuted = _playerService.IsMuted;
        TriggerInteraction();
    }

    public void SeekToPercentage(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        SeekToRatio(percent / 100.0);
    }

    public void SetAudioDelay(long delayMs)
    {
        _playerService.SetAudioDelay(delayMs);
        AudioDelayMs = _playerService.AudioDelayMs;
        TriggerInteraction();
    }

    public void AdjustAudioDelay(long deltaMs)
    {
        _playerService.AdjustAudioDelay(deltaMs);
        AudioDelayMs = _playerService.AudioDelayMs;
        TriggerInteraction();
    }

    public async Task LoadExternalAudioAsync()
    {
        if (_filePickerService == null) return;

        var extensions = new[] { ".mka", ".mkv", ".aac", ".ac3", ".mp3", ".m4a", ".wav", ".flac", ".ogg", ".opus" };
        var pickedPath = await _filePickerService.PickOpenFileAsync("Select External Audio File", extensions);
        if (!string.IsNullOrEmpty(pickedPath))
        {
            var success = await _playerService.AddExternalAudioAsync(pickedPath);
            if (success)
            {
                ExternalAudioPath = pickedPath;
                TriggerInteraction();
            }
        }
    }

    public async Task RemoveExternalAudioAsync()
    {
        var success = await _playerService.RemoveExternalAudioAsync();
        if (success)
        {
            ExternalAudioPath = null;
            TriggerInteraction();
        }
    }

    public async Task LoadExternalSubtitleAsync()
    {
        if (_filePickerService == null) return;

        var extensions = new[] { ".srt", ".ass", ".ssa", ".vtt", ".sub" };
        var pickedPath = await _filePickerService.PickOpenFileAsync("Select Subtitle File", extensions);
        if (!string.IsNullOrEmpty(pickedPath))
        {
            var success = await _playerService.AddExternalSubtitleAsync(pickedPath);
            if (success)
            {
                ExternalSubtitlePath = pickedPath;
                TriggerInteraction();
            }
        }
    }

    public async Task RemoveExternalSubtitleAsync()
    {
        var success = await _playerService.RemoveExternalSubtitleAsync();
        if (success)
        {
            ExternalSubtitlePath = null;
            TriggerInteraction();
        }
    }

    public async Task SetSubtitleFontSizeAsync(SubtitleFontSize size)
    {
        _subtitleSettings.FontSize = size;
        NotifySubtitleSettingsChanged();
        await _playerService.ApplySubtitleSettingsAsync(_subtitleSettings);
        TriggerInteraction();
    }

    public async Task SetSubtitleColorAsync(SubtitleColor color)
    {
        _subtitleSettings.Color = color;
        NotifySubtitleSettingsChanged();
        await _playerService.ApplySubtitleSettingsAsync(_subtitleSettings);
        TriggerInteraction();
    }

    public async Task SetSubtitleOutlineAsync(SubtitleOutline outline)
    {
        _subtitleSettings.Outline = outline;
        NotifySubtitleSettingsChanged();
        await _playerService.ApplySubtitleSettingsAsync(_subtitleSettings);
        TriggerInteraction();
    }

    public async Task SetSubtitleBackgroundAsync(SubtitleBackground background)
    {
        _subtitleSettings.Background = background;
        NotifySubtitleSettingsChanged();
        await _playerService.ApplySubtitleSettingsAsync(_subtitleSettings);
        TriggerInteraction();
    }

    public async Task SetSubtitlePositionAsync(SubtitlePosition position)
    {
        _subtitleSettings.Position = position;
        NotifySubtitleSettingsChanged();
        await _playerService.ApplySubtitleSettingsAsync(_subtitleSettings);
        TriggerInteraction();
    }

    public async Task ResetSubtitleSettingsAsync()
    {
        _subtitleSettings = new SubtitleSettings();
        NotifySubtitleSettingsChanged();
        await _playerService.ApplySubtitleSettingsAsync(_subtitleSettings);
        TriggerInteraction();
    }

    private void OnPlayerAudioDelayChanged(long delayMs)
    {
        RunOnUi(() =>
        {
            AudioDelayMs = delayMs;
        });
    }

    private void OnPlayerSubtitleSettingsChanged()
    {
        RunOnUi(() =>
        {
            _subtitleSettings = _playerService.CurrentSubtitleSettings;
            NotifySubtitleSettingsChanged();
        });
    }

    private void NotifySubtitleSettingsChanged()
    {
        OnPropertyChanged(nameof(SubtitleSettings));
        OnPropertyChanged(nameof(SubtitleFontSize));
        OnPropertyChanged(nameof(SubtitleColor));
        OnPropertyChanged(nameof(SubtitleOutline));
        OnPropertyChanged(nameof(SubtitleBackground));
        OnPropertyChanged(nameof(SubtitlePosition));
    }

    public void OnToggleFullscreen()
    {
        IsFullscreen = !IsFullscreen;
        FullscreenToggled?.Invoke(IsFullscreen);
        TriggerInteraction();
    }

    public void SelectAudioTrack(PlayerTrackInfo? track)
    {
        if (track == null) return;
        _playerService.SetAudioTrack(track.Id);
        IsAudioMenuOpen = false;
        TriggerInteraction();
    }

    public void SelectSubtitleTrack(PlayerTrackInfo? track)
    {
        if (track == null) return;
        _playerService.SetSubtitleTrack(track.Id);
        IsSubtitleMenuOpen = false;
        TriggerInteraction();
    }

    public void TriggerInteraction()
    {
        IsControlsVisible = true;
    }

    public async void OnBack()
    {
        await SaveProgressAsync(isFinished: false);
        _playerService.Stop();

        if (IsFullscreen)
        {
            IsFullscreen = false;
            FullscreenToggled?.Invoke(false);
        }

        if (_seriesId > 0)
        {
            NavigationRequested?.Invoke($"SeriesDetails:{_seriesId}");
        }
        else if (_episodeId > 0)
        {
            NavigationRequested?.Invoke("TVShows");
        }
        else
        {
            NavigationRequested?.Invoke(_movieId > 0 ? $"MovieDetails:{_movieId}" : "Movies");
        }
    }

    private async Task SaveProgressAsync(bool isFinished)
    {
        if (_mediaFileId <= 0 || _durationMs <= 0) return;

        try
        {
            var posTicks = _positionMs * TimeSpan.TicksPerMillisecond;
            var durTicks = _durationMs * TimeSpan.TicksPerMillisecond;

            // If user watched more than 92% of the film, consider it finished
            if (!isFinished && _progressRatio >= 0.92)
            {
                isFinished = true;
            }

            await _progressRepository.SaveProgressAsync(_mediaFileId, _movieId > 0 ? _movieId : null, posTicks, durTicks, isFinished, _episodeId > 0 ? _episodeId : null);
            _lastSavedProgressTime = DateTime.UtcNow;
            _logger.LogDebug("Saved playback progress for media file {FileId}: {Pos} / {Dur}", _mediaFileId, _positionMs, _durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist playback progress to SQLite.");
        }
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher != null)
        {
            _dispatcher.RunOnUi(action);
        }
        else
        {
            action();
        }
    }

    private void OnPlayerStateChanged(PlayerState state)
    {
        RunOnUi(() =>
        {
            IsPlaying = state == PlayerState.Playing;
            if (state == PlayerState.Playing)
            {
                HasError = false;
            }
        });
    }

    private void OnPlayerPositionChanged(long positionMs, float ratio)
    {
        RunOnUi(() =>
        {
            PositionMs = positionMs;

            // When user is actively dragging the slider, do not overwrite the user's drag preview!
            if (!_isDraggingSlider)
            {
                _isUpdatingFromPlayer = true;
                try
                {
                    ProgressRatio = ratio;
                    CurrentTimeDisplay = FormatTime(positionMs);
                    if (_durationMs > 0)
                    {
                        var remaining = Math.Max(0, _durationMs - positionMs);
                        RemainingTimeDisplay = $"-{FormatTime(remaining)}";
                    }
                }
                finally
                {
                    _isUpdatingFromPlayer = false;
                }
            }

            // Periodically save progress to database every 5 seconds (only when playing and not dragging)
            if (!_isDraggingSlider && (DateTime.UtcNow - _lastSavedProgressTime).TotalSeconds >= 5)
            {
                _ = SaveProgressAsync(isFinished: false);
            }
        });
    }

    private void OnPlayerDurationChanged(long durationMs)
    {
        RunOnUi(() =>
        {
            DurationMs = durationMs;
            DurationDisplay = FormatTime(durationMs);
            var remaining = Math.Max(0, _durationMs - _positionMs);
            RemainingTimeDisplay = $"-{FormatTime(remaining)}";
        });
    }

    private void OnPlayerTracksChanged()
    {
        RunOnUi(() =>
        {
            AudioTracks.Clear();
            foreach (var a in _playerService.AudioTracks)
            {
                AudioTracks.Add(a);
            }

            SubtitleTracks.Clear();
            foreach (var s in _playerService.SubtitleTracks)
            {
                SubtitleTracks.Add(s);
            }
        });
    }

    private void OnPlayerPlaybackError(string message)
    {
        RunOnUi(() =>
        {
            HasError = true;
            ErrorMessage = message;
            IsPlaying = false;
            IsControlsVisible = true;
        });
    }

    private void OnPlayerMediaEnded()
    {
        RunOnUi(() =>
        {
            _ = SaveProgressAsync(isFinished: true);
            IsPlaying = false;
            IsControlsVisible = true;
        });
    }

    public static string FormatTime(long milliseconds)
    {
        if (milliseconds < 0) milliseconds = 0;
        var ts = TimeSpan.FromMilliseconds(milliseconds);
        if (ts.TotalHours >= 1)
        {
            return $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
        }
        return $"{ts.Minutes:D2}:{ts.Seconds:D2}";
    }

    public void Dispose()
    {
        UnhookPlayerEvents();
        _playerService.Stop();
    }
}
