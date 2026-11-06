namespace MovieTime.Core.Playback;

/// <summary>
/// Represents the current playback lifecycle state of the media player.
/// </summary>
public enum PlayerState
{
    Idle,
    Opening,
    Buffering,
    Playing,
    Paused,
    Stopped,
    Ended,
    Error
}
