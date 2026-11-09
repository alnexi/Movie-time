using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace MovieTime.Core.Playback;

/// <summary>
/// Abstraction for the underlying LibVLC hardware-accelerated playback engine.
/// Provides decoupled control over playback state, seeking, volume, and track selection.
/// </summary>
public interface IVlcPlayerService : IDisposable
{
    bool IsInitialized { get; }
    PlayerState State { get; }
    bool IsPlaying { get; }
    long PositionMs { get; }
    long DurationMs { get; }
    float PositionRatio { get; }
    int Volume { get; }
    bool IsMuted { get; }
    float PlaybackRate { get; }

    IReadOnlyList<PlayerTrackInfo> AudioTracks { get; }
    IReadOnlyList<PlayerTrackInfo> SubtitleTracks { get; }
    int CurrentAudioTrackId { get; }
    int CurrentSubtitleTrackId { get; }

    string? ExternalAudioPath { get; }
    string? ExternalSubtitlePath { get; }
    long AudioDelayMs { get; }
    SubtitleSettings CurrentSubtitleSettings { get; }

    Task<bool> OpenMediaAsync(string filePath, long startPositionMs = 0);
    void Play();
    void Pause();
    void TogglePlayPause();
    void Stop();
    void Seek(long positionMs);
    void SeekRelative(long deltaMs);
    void SetVolume(int volume);
    void ToggleMute();
    void SetPlaybackRate(float rate);
    bool SetAudioTrack(int trackId);
    bool SetSubtitleTrack(int trackId);

    Task<bool> AddExternalAudioAsync(string audioFilePath);
    Task<bool> RemoveExternalAudioAsync();
    void SetAudioDelay(long delayMs);
    void AdjustAudioDelay(long deltaMs);

    Task<bool> AddExternalSubtitleAsync(string subtitleFilePath);
    Task<bool> RemoveExternalSubtitleAsync();
    Task ApplySubtitleSettingsAsync(SubtitleSettings settings);

    event Action<PlayerState>? StateChanged;
    event Action<long, float>? PositionChanged;
    event Action<long>? DurationChanged;
    event Action? TracksChanged;
    event Action<string>? PlaybackError;
    event Action? MediaEnded;
    event Action<long>? AudioDelayChanged;
    event Action? SubtitleSettingsChanged;
    event Action<float>? PlaybackRateChanged;
    event Action<int, bool>? VolumeChanged;
}
