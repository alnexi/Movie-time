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

    IReadOnlyList<PlayerTrackInfo> AudioTracks { get; }
    IReadOnlyList<PlayerTrackInfo> SubtitleTracks { get; }
    int CurrentAudioTrackId { get; }
    int CurrentSubtitleTrackId { get; }

    Task<bool> OpenMediaAsync(string filePath, long startPositionMs = 0);
    void Play();
    void Pause();
    void TogglePlayPause();
    void Stop();
    void Seek(long positionMs);
    void SeekRelative(long deltaMs);
    void SetVolume(int volume);
    void ToggleMute();
    bool SetAudioTrack(int trackId);
    bool SetSubtitleTrack(int trackId);

    event Action<PlayerState>? StateChanged;
    event Action<long, float>? PositionChanged;
    event Action<long>? DurationChanged;
    event Action? TracksChanged;
    event Action<string>? PlaybackError;
    event Action? MediaEnded;
}
