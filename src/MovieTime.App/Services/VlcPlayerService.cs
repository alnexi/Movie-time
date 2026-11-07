using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using LibVLCSharp.Platforms.Windows;
using LibVLCSharp.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Playback;

namespace MovieTime.App.Services;

/// <summary>
/// Hardware-accelerated video playback service wrapping LibVLC and LibVLCSharp.WinUI.
/// Renders directly into a native WinUI 3 VideoView using Direct3D11 swap chains.
/// </summary>
public sealed class VlcPlayerService : IVlcPlayerService
{
    private readonly ILogger<VlcPlayerService> _logger;
    private readonly object _lock = new();
    private readonly object _seekLock = new();
    private long _pendingSeekTimeMs = -1;
    private bool _isSeekWorkerRunning;

    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private VideoView? _attachedVideoView;
    private TaskCompletionSource<bool>? _swapChainReadyTcs;
    private string[]? _swapChainOptions;

    private PlayerState _state = PlayerState.Idle;
    private long _positionMs;
    private long _durationMs;
    private float _positionRatio;
    private int _volume = 100;
    private bool _isMuted;
    private bool _isDisposed;

    public bool IsInitialized => _libVlc != null && _mediaPlayer != null;

    public PlayerState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(_state);
            }
        }
    }

    public bool IsPlaying => State == PlayerState.Playing;

    public long PositionMs
    {
        get => _positionMs;
        private set => _positionMs = value;
    }

    public long DurationMs
    {
        get => _durationMs;
        private set => _durationMs = value;
    }

    public float PositionRatio
    {
        get => _positionRatio;
        private set => _positionRatio = value;
    }

    public int Volume
    {
        get => _volume;
        private set => _volume = value;
    }

    public bool IsMuted
    {
        get => _isMuted;
        private set => _isMuted = value;
    }

    public IReadOnlyList<PlayerTrackInfo> AudioTracks { get; private set; } = Array.Empty<PlayerTrackInfo>();
    public IReadOnlyList<PlayerTrackInfo> SubtitleTracks { get; private set; } = Array.Empty<PlayerTrackInfo>();

    public int CurrentAudioTrackId => _mediaPlayer?.AudioTrack ?? -1;
    public int CurrentSubtitleTrackId => _mediaPlayer?.Spu ?? -1;

    public event Action<PlayerState>? StateChanged;
    public event Action<long, float>? PositionChanged;
    public event Action<long>? DurationChanged;
    public event Action? TracksChanged;
    public event Action<string>? PlaybackError;
    public event Action? MediaEnded;

    public VlcPlayerService(ILogger<VlcPlayerService>? logger = null)
    {
        _logger = logger ?? NullLogger<VlcPlayerService>.Instance;
    }

    /// <summary>
    /// Attaches the native WinUI 3 VideoView swap chain surface to the underlying LibVLC player.
    /// Captures the Direct3D11 swap chain options required for single-window in-app composition.
    /// </summary>
    public void AttachVideoView(VideoView videoView)
    {
        lock (_lock)
        {
            if (_attachedVideoView == videoView) return;

            _attachedVideoView = videoView ?? throw new ArgumentNullException(nameof(videoView));

            // Check if VideoView is already loaded and has swap chain options ready
            try
            {
                var options = _attachedVideoView.SwapChainOptions;
                if (options != null && options.Length > 0)
                {
                    _swapChainOptions = options;
                    EnsureInitializedWithSwapChain(options);
                    _swapChainReadyTcs?.TrySetResult(true);
                    _logger.LogInformation("Attached pre-loaded VideoView with active SwapChainOptions.");
                    return;
                }
            }
            catch
            {
                // Not yet loaded into visual tree; Initialized event will fire upon layout
            }

            _swapChainReadyTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _attachedVideoView.Initialized -= OnVideoViewInitialized;
            _attachedVideoView.Initialized += OnVideoViewInitialized;
            _logger.LogInformation("Attached VideoView to VlcPlayerService; subscribed to Initialized event for swapchain options.");
        }
    }

    private void OnVideoViewInitialized(object? sender, InitializedEventArgs e)
    {
        lock (_lock)
        {
            _logger.LogInformation("VideoView.Initialized event received with native WinUI swapchain options.");
            _swapChainOptions = e.SwapChainOptions;
            EnsureInitializedWithSwapChain(e.SwapChainOptions);
            _swapChainReadyTcs?.TrySetResult(true);
        }
    }

    /// <summary>
    /// Ensures native LibVLC core is initialized and engine instances are allocated.
    /// </summary>
    public void EnsureInitialized()
    {
        EnsureInitializedWithSwapChain(_swapChainOptions);
    }

    /// <summary>
    /// Allocates LibVLC and MediaPlayer with native Direct3D11 swapchain options so video
    /// renders directly inside WinUI 3 SwapChainPanel with zero external popup windows.
    /// </summary>
    public void EnsureInitializedWithSwapChain(string[]? swapChainOptions)
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VlcPlayerService));
            }

            if (_libVlc != null && _mediaPlayer != null)
            {
                if (_attachedVideoView != null && _attachedVideoView.MediaPlayer != _mediaPlayer)
                {
                    _attachedVideoView.MediaPlayer = _mediaPlayer;
                }
                return;
            }

            try
            {
                // 1. Initialize native LibVLC binaries
                LibVLCSharp.Shared.Core.Initialize();

                // 2. Strict local-only parameters: disable OSD title overlay, bypass remote telemetry, optimize for local file playback
                var optionsList = new List<string>
                {
                    "--no-osd",
                    "--no-video-title-show",
                    "--file-caching=1500",
                    "--disc-caching=1500",
                    "--network-caching=0",
                    "--no-stats",
                    "--drop-late-frames",
                    "--skip-frames"
                };

                if (swapChainOptions != null && swapChainOptions.Length > 0)
                {
                    optionsList.AddRange(swapChainOptions);
                    _logger.LogInformation("Configured LibVLC with WinUI 3 Direct3D11 swapchain parameters: {Options}", string.Join(" ", swapChainOptions));
                }
                else
                {
                    _logger.LogWarning("Initializing LibVLC without WinUI swapchain options. (VideoView may not yet be loaded).");
                }

                _libVlc = new LibVLC(optionsList.ToArray());
                _mediaPlayer = new MediaPlayer(_libVlc);

                // Configure default volume
                _mediaPlayer.Volume = _volume;

                // Subscribe to MediaPlayer events
                HookMediaPlayerEvents(_mediaPlayer);

                if (_attachedVideoView != null)
                {
                    _attachedVideoView.MediaPlayer = _mediaPlayer;
                }

                _logger.LogInformation("LibVLC playback engine initialized successfully (single-window DirectComposition swapchain active).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize LibVLC native engine with SwapChain.");
                State = PlayerState.Error;
                PlaybackError?.Invoke($"Failed to initialize VLC playback engine: {ex.Message}");
            }
        }
    }

    private void HookMediaPlayerEvents(MediaPlayer mp)
    {
        mp.Playing += OnPlaying;
        mp.Paused += OnPaused;
        mp.Stopped += OnStopped;
        mp.EndReached += OnEndReached;
        mp.EncounteredError += OnEncounteredError;
        mp.TimeChanged += OnTimeChanged;
        mp.LengthChanged += OnLengthChanged;
        mp.PositionChanged += OnPositionChanged;
        mp.ESAdded += OnTracksUpdated;
        mp.ESDeleted += OnTracksUpdated;
        mp.ESSelected += OnTracksUpdated;
        mp.Muted += (s, e) => { _isMuted = true; };
        mp.Unmuted += (s, e) => { _isMuted = false; };
        mp.VolumeChanged += (s, e) => { _volume = (int)(e.Volume * 100); };
    }

    private void UnhookMediaPlayerEvents(MediaPlayer mp)
    {
        mp.Playing -= OnPlaying;
        mp.Paused -= OnPaused;
        mp.Stopped -= OnStopped;
        mp.EndReached -= OnEndReached;
        mp.EncounteredError -= OnEncounteredError;
        mp.TimeChanged -= OnTimeChanged;
        mp.LengthChanged -= OnLengthChanged;
        mp.PositionChanged -= OnPositionChanged;
        mp.ESAdded -= OnTracksUpdated;
        mp.ESDeleted -= OnTracksUpdated;
        mp.ESSelected -= OnTracksUpdated;
    }

    public async Task<bool> OpenMediaAsync(string filePath, long startPositionMs = 0)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            _logger.LogWarning("Requested media file does not exist on disk: {Path}", filePath);
            State = PlayerState.Error;
            PlaybackError?.Invoke($"Media file does not exist: {Path.GetFileName(filePath)}");
            return false;
        }

        try
        {
            if (_libVlc == null || _mediaPlayer == null)
            {
                if (_swapChainReadyTcs != null && !_swapChainReadyTcs.Task.IsCompleted)
                {
                    _logger.LogInformation("OpenMediaAsync waiting for WinUI VideoView SwapChain surface to initialize...");
                    await Task.WhenAny(_swapChainReadyTcs.Task, Task.Delay(2500)).ConfigureAwait(false);
                }

                EnsureInitialized();
            }

            if (_libVlc == null || _mediaPlayer == null)
            {
                _logger.LogError("LibVLC engine failed to initialize prior to opening media.");
                return false;
            }

            lock (_seekLock)
            {
                _pendingSeekTimeMs = -1;
            }

            State = PlayerState.Opening;

            // Stop previous media if any
            if (_mediaPlayer.IsPlaying)
            {
                _mediaPlayer.Stop();
            }

            _currentMedia?.Dispose();

            // Configure media options
            var media = new Media(_libVlc, filePath, FromType.FromPath);
            if (startPositionMs > 1000)
            {
                var startSec = startPositionMs / 1000.0;
                media.AddOption($":start-time={startSec:F1}");
                _logger.LogInformation("Configured start-time offset: {Seconds}s for {File}", startSec, Path.GetFileName(filePath));
            }

            _currentMedia = media;
            _mediaPlayer.Media = _currentMedia;

            // Parse metadata asynchronously
            _ = Task.Run(async () =>
            {
                try
                {
                    await media.Parse(MediaParseOptions.ParseLocal).ConfigureAwait(false);
                    RefreshTracks();
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Background media parsing completed with notice.");
                }
            });

            // Start playback
            var playStarted = _mediaPlayer.Play();
            if (!playStarted)
            {
                _logger.LogError("MediaPlayer.Play() returned false for file: {Path}", filePath);
                State = PlayerState.Error;
                PlaybackError?.Invoke("Failed to start playback for this media file.");
                return false;
            }

            _logger.LogInformation("Playback initiated for file: {Path}", filePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error opening media file: {Path}", filePath);
            State = PlayerState.Error;
            PlaybackError?.Invoke($"Error opening media: {ex.Message}");
            return false;
        }
    }

    public void Play()
    {
        if (_mediaPlayer != null && !_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Play();
        }
    }

    public void Pause()
    {
        if (_mediaPlayer != null && _mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
    }

    public void TogglePlayPause()
    {
        if (_mediaPlayer == null) return;
        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Pause();
        }
        else
        {
            _mediaPlayer.Play();
        }
    }

    public void Stop()
    {
        lock (_seekLock)
        {
            _pendingSeekTimeMs = -1;
        }

        if (_mediaPlayer != null)
        {
            _mediaPlayer.Stop();
            State = PlayerState.Stopped;
        }
    }

    public void Seek(long positionMs)
    {
        if (_isDisposed || _mediaPlayer == null || _durationMs <= 0) return;

        // Clamp safely between [0, duration - 250ms] to avoid EOF hanging
        var maxSeekable = Math.Max(0, _durationMs - 250);
        var clampedTime = Math.Clamp(positionMs, 0, maxSeekable);

        // Optimistically update position cache immediately
        _positionMs = clampedTime;
        _positionRatio = _durationMs > 0 ? (float)clampedTime / _durationMs : 0f;

        lock (_seekLock)
        {
            _pendingSeekTimeMs = clampedTime;
            if (!_isSeekWorkerRunning)
            {
                _isSeekWorkerRunning = true;
                _ = Task.Run(ProcessPendingSeeksAsync);
            }
        }
    }

    private async Task ProcessPendingSeeksAsync()
    {
        while (true)
        {
            long targetMs;
            lock (_seekLock)
            {
                if (_pendingSeekTimeMs < 0 || _mediaPlayer == null || _isDisposed)
                {
                    _isSeekWorkerRunning = false;
                    _pendingSeekTimeMs = -1;
                    return;
                }

                targetMs = _pendingSeekTimeMs;
                _pendingSeekTimeMs = -1;
            }

            try
            {
                if (_mediaPlayer != null && !_isDisposed)
                {
                    _mediaPlayer.Time = targetMs;
                    _positionMs = targetMs;
                    if (_durationMs > 0)
                    {
                        _positionRatio = (float)targetMs / _durationMs;
                    }
                    PositionChanged?.Invoke(_positionMs, _positionRatio);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Background LibVLC seek to {TargetMs}ms failed.", targetMs);
            }

            // Brief throttle window (35ms) to let decoder settle and allow coalescing of rapid slider events
            await Task.Delay(35).ConfigureAwait(false);
        }
    }

    public void SeekRelative(long deltaMs)
    {
        if (_isDisposed || _mediaPlayer == null || _durationMs <= 0) return;
        var currentTime = _positionMs;
        var targetTime = currentTime + deltaMs;
        Seek(targetTime);
    }

    public void SetVolume(int volume)
    {
        _volume = Math.Clamp(volume, 0, 100);
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Volume = _volume;
            if (_isMuted && _volume > 0)
            {
                _mediaPlayer.Mute = false;
                _isMuted = false;
            }
        }
    }

    public void ToggleMute()
    {
        if (_mediaPlayer != null)
        {
            _isMuted = !_isMuted;
            _mediaPlayer.Mute = _isMuted;
        }
    }

    public bool SetAudioTrack(int trackId)
    {
        if (_mediaPlayer == null) return false;
        var result = _mediaPlayer.SetAudioTrack(trackId);
        RefreshTracks();
        return result;
    }

    public bool SetSubtitleTrack(int trackId)
    {
        if (_mediaPlayer == null) return false;
        var result = _mediaPlayer.SetSpu(trackId);
        RefreshTracks();
        return result;
    }

    private void RefreshTracks()
    {
        if (_mediaPlayer == null) return;

        try
        {
            var audioList = new List<PlayerTrackInfo>();
            var rawAudio = _mediaPlayer.AudioTrackDescription;
            if (rawAudio != null)
            {
                var currentAudioId = _mediaPlayer.AudioTrack;
                foreach (var t in rawAudio)
                {
                    if (t.Id != -1) // -1 is disable/none
                    {
                        audioList.Add(new PlayerTrackInfo(t.Id, t.Name ?? $"Audio Track {t.Id}", "Unknown", t.Id == currentAudioId));
                    }
                }
            }
            AudioTracks = audioList;

            var subList = new List<PlayerTrackInfo>();
            var rawSubs = _mediaPlayer.SpuDescription;
            if (rawSubs != null)
            {
                var currentSpuId = _mediaPlayer.Spu;
                // Add explicit "Off" track option
                subList.Add(new PlayerTrackInfo(-1, "Off", "None", currentSpuId == -1));
                foreach (var s in rawSubs)
                {
                    if (s.Id != -1)
                    {
                        subList.Add(new PlayerTrackInfo(s.Id, s.Name ?? $"Subtitle {s.Id}", "Unknown", s.Id == currentSpuId));
                    }
                }
            }
            SubtitleTracks = subList;

            TracksChanged?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to refresh tracks description.");
        }
    }

    private void OnPlaying(object? sender, EventArgs e)
    {
        State = PlayerState.Playing;
        RefreshTracks();
    }

    private void OnPaused(object? sender, EventArgs e)
    {
        State = PlayerState.Paused;
    }

    private void OnStopped(object? sender, EventArgs e)
    {
        State = PlayerState.Stopped;
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        State = PlayerState.Ended;
        MediaEnded?.Invoke();
    }

    private void OnEncounteredError(object? sender, EventArgs e)
    {
        State = PlayerState.Error;
        PlaybackError?.Invoke("An error occurred during video decoding.");
    }

    private void OnTimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
    {
        _positionMs = e.Time;
        if (_durationMs > 0)
        {
            _positionRatio = (float)_positionMs / _durationMs;
        }
        PositionChanged?.Invoke(_positionMs, _positionRatio);
    }

    private void OnLengthChanged(object? sender, MediaPlayerLengthChangedEventArgs e)
    {
        _durationMs = e.Length;
        DurationChanged?.Invoke(_durationMs);
        if (_durationMs > 0)
        {
            _positionRatio = (float)_positionMs / _durationMs;
        }
    }

    private void OnPositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
    {
        _positionRatio = e.Position;
        if (_durationMs > 0 && _positionMs == 0)
        {
            _positionMs = (long)(e.Position * _durationMs);
            PositionChanged?.Invoke(_positionMs, _positionRatio);
        }
    }

    private void OnTracksUpdated(object? sender, EventArgs e)
    {
        RefreshTracks();
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;

            lock (_seekLock)
            {
                _pendingSeekTimeMs = -1;
            }

            try
            {
                if (_mediaPlayer != null)
                {
                    UnhookMediaPlayerEvents(_mediaPlayer);
                    if (_mediaPlayer.IsPlaying)
                    {
                        _mediaPlayer.Stop();
                    }
                    _mediaPlayer.Dispose();
                    _mediaPlayer = null;
                }

                _currentMedia?.Dispose();
                _currentMedia = null;

                _libVlc?.Dispose();
                _libVlc = null;

                _logger.LogInformation("VlcPlayerService disposed cleanly.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during VlcPlayerService disposal.");
            }
        }
    }
}
