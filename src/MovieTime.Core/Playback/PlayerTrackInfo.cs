namespace MovieTime.Core.Playback;

/// <summary>
/// Represents metadata for an audio or subtitle track discovered in a media file.
/// </summary>
public record PlayerTrackInfo(int Id, string Name, string Language, bool IsSelected);
