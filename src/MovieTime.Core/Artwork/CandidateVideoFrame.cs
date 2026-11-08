using System;

namespace MovieTime.Core.Artwork;

/// <summary>
/// A candidate frame captured from a video at a specific playback timestamp.
/// </summary>
public sealed class CandidateVideoFrame
{
    public TimeSpan Timestamp { get; init; }
    public byte[] ImageBytes { get; init; } = Array.Empty<byte>();
    public int Width { get; init; }
    public int Height { get; init; }
    public byte[]? BgraPixels { get; init; }
    public ImageQualityScore? QualityScore { get; set; }
}
