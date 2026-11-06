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

    private LibVLC? _libVlc;
    private MediaPlayer? _mediaPlayer;
    private Media? _currentMedia;
    private VideoView? _attachedVideoView;

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
    /// </summary>
    public void AttachVideoView(VideoView videoView)
    {
        lock (_lock)
        {
            _attachedVideoView = videoView ?? throw new ArgumentNullException(nameof(videoView));
            EnsureInitialized();
            _attachedVideoView.MediaPlayer = _mediaPlayer;
            _logger.LogInformation("Attached VideoView to LibVLC MediaPlayer instance.");
        }
    }

    /// <summary>
    /// Ensures native LibVLC core is initialized and engine instances are allocated.
    /// </summary>
    public void EnsureInitialized()
    {
        lock (_lock)
        {
            if (_isDisposed)
            {
                throw new ObjectDisposedException(nameof(VlcPlayerService));
            }

            if (_libVlc != null && _mediaPlayer != null)
            {
                return;
            }

            try
            {
                // 1. Initialize native LibVLC binaries
                LibVLCSharp.Shared.Core.Initialize();

                // 2. Strict local-only parameters: disable OSD title overlay, bypass remote telemetry, optimize for local file playback
                var options = new[]
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

                _libVlc = new LibVLC(options);
                _mediaPlayer = new MediaPlayer(_libVlc);

                // Configure default volume
                _mediaPlayer.Volume = _volume;

                // Subscribe to MediaPlayer events
                HookMediaPlayerEvents(_mediaPlayer);

                if (_attachedVideoView != null)
                {
                    _attachedVideoView.MediaPlayer = _mediaPlayer;
                }

                _logger.LogInformation("LibVLC playback engine initialized successfully (x64 hardware decoding enabled).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize LibVLC native engine.");
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
            EnsureInitialized();
            if (_libVlc == null || _mediaPlayer == null)
            {
                return false;
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
        if (_mediaPlayer != null)
        {
            _mediaPlayer.Stop();
            State = PlayerState.Stopped;
        }
    }

    public void Seek(long positionMs)
    {
        if (_mediaPlayer == null || _durationMs <= 0) return;
        var clampedTime = Math.Clamp(positionMs, 0, _durationMs);
        _mediaPlayer.Time = clampedTime;
        _positionMs = clampedTime;
        _positionRatio = (float)clampedTime / _durationMs;
        PositionChanged?.Invoke(_positionMs, _positionRatio);
    }

    public void SeekRelative(long deltaMs)
    {
        if (_mediaPlayer == null) return;
        var currentTime = _mediaPlayer.Time;
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
