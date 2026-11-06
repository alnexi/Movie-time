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
    private readonly ILogger<PlayerViewModel> _logger;

    private int _movieId;
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

    public ICommand PlayPauseCommand { get; }
    public ICommand SeekForwardCommand { get; }
    public ICommand SeekBackwardCommand { get; }
    public ICommand ToggleMuteCommand { get; }
    public ICommand ToggleFullscreenCommand { get; }
    public ICommand ToggleAudioMenuCommand { get; }
    public ICommand ToggleSubtitleMenuCommand { get; }
    public ICommand ToggleInfoCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SelectAudioTrackCommand { get; }
    public ICommand SelectSubtitleTrackCommand { get; }

    public PlayerViewModel(
        IVlcPlayerService playerService,
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        IPlaybackProgressRepository progressRepository,
        ILogger<PlayerViewModel>? logger = null)
    {
        _playerService = playerService ?? throw new ArgumentNullException(nameof(playerService));
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _progressRepository = progressRepository ?? throw new ArgumentNullException(nameof(progressRepository));
        _logger = logger ?? NullLogger<PlayerViewModel>.Instance;

        PlayPauseCommand = new RelayCommand(OnPlayPause);
        SeekForwardCommand = new RelayCommand(() => SeekRelative(10000));
        SeekBackwardCommand = new RelayCommand(() => SeekRelative(-10000));
        ToggleMuteCommand = new RelayCommand(OnToggleMute);
        ToggleFullscreenCommand = new RelayCommand(OnToggleFullscreen);
        ToggleAudioMenuCommand = new RelayCommand(() => IsAudioMenuOpen = !IsAudioMenuOpen);
        ToggleSubtitleMenuCommand = new RelayCommand(() => IsSubtitleMenuOpen = !IsSubtitleMenuOpen);
        ToggleInfoCommand = new RelayCommand(() => IsInfoOpen = !IsInfoOpen);
        BackCommand = new RelayCommand(OnBack);
        SelectAudioTrackCommand = new RelayCommand<PlayerTrackInfo>(SelectAudioTrack);
        SelectSubtitleTrackCommand = new RelayCommand<PlayerTrackInfo>(SelectSubtitleTrack);

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
    }

    private void UnhookPlayerEvents()
    {
        _playerService.StateChanged -= OnPlayerStateChanged;
        _playerService.PositionChanged -= OnPlayerPositionChanged;
        _playerService.DurationChanged -= OnPlayerDurationChanged;
        _playerService.TracksChanged -= OnPlayerTracksChanged;
        _playerService.PlaybackError -= OnPlayerPlaybackError;
        _playerService.MediaEnded -= OnPlayerMediaEnded;
    }

    public async Task InitializeAsync(string filePath, int? movieId = null)
    {
        _filePath = filePath ?? string.Empty;
        _movieId = movieId ?? 0;
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

        // 1. Resolve Movie and MediaFile metadata from SQLite
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
                    _logger.LogInformation("Resuming movie from saved progress: {Seconds}s", resumePositionMs / 1000);
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
        if (_movieId > 0)
        {
            movie = await _movieRepository.GetByIdAsync(_movieId);
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
            }
        }

        if (movie != null)
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

    public void SeekRelative(long deltaMs)
    {
        _playerService.SeekRelative(deltaMs);
        TriggerInteraction();
    }

    public void SeekToRatio(double ratio)
    {
        if (_durationMs > 0)
        {
            var targetMs = (long)(Math.Clamp(ratio, 0.0, 1.0) * _durationMs);
            _playerService.Seek(targetMs);
        }
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

        NavigationRequested?.Invoke(_movieId > 0 ? $"MovieDetails:{_movieId}" : "Movies");
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

            await _progressRepository.SaveProgressAsync(_mediaFileId, _movieId > 0 ? _movieId : null, posTicks, durTicks, isFinished);
            _lastSavedProgressTime = DateTime.UtcNow;
            _logger.LogDebug("Saved playback progress for media file {FileId}: {Pos} / {Dur}", _mediaFileId, _positionMs, _durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist playback progress to SQLite.");
        }
    }

    private void OnPlayerStateChanged(PlayerState state)
    {
        IsPlaying = state == PlayerState.Playing;
        if (state == PlayerState.Playing)
        {
            HasError = false;
        }
    }

    private void OnPlayerPositionChanged(long positionMs, float ratio)
    {
        PositionMs = positionMs;
        ProgressRatio = ratio;

        CurrentTimeDisplay = FormatTime(positionMs);
        if (_durationMs > 0)
        {
            var remaining = Math.Max(0, _durationMs - positionMs);
            RemainingTimeDisplay = $"-{FormatTime(remaining)}";
        }

        // Periodically save progress to database every 5 seconds
        if ((DateTime.UtcNow - _lastSavedProgressTime).TotalSeconds >= 5)
        {
            _ = SaveProgressAsync(isFinished: false);
        }
    }

    private void OnPlayerDurationChanged(long durationMs)
    {
        DurationMs = durationMs;
        DurationDisplay = FormatTime(durationMs);
        var remaining = Math.Max(0, _durationMs - _positionMs);
        RemainingTimeDisplay = $"-{FormatTime(remaining)}";
    }

    private void OnPlayerTracksChanged()
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
    }

    private void OnPlayerPlaybackError(string message)
    {
        HasError = true;
        ErrorMessage = message;
        IsPlaying = false;
        IsControlsVisible = true;
    }

    private void OnPlayerMediaEnded()
    {
        _ = SaveProgressAsync(isFinished: true);
        IsPlaying = false;
        IsControlsVisible = true;
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
