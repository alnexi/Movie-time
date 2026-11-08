using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Abstraction for capturing candidate video frames from local physical video files.
/// </summary>
public interface IVideoFrameExtractor
{
    Task<IReadOnlyList<CandidateVideoFrame>> ExtractCandidateFramesAsync(
        string filePath,
        long durationMs,
        CancellationToken cancellationToken = default);

    Task<CandidateVideoFrame?> ExtractFrameAtTimeAsync(
        string filePath,
        TimeSpan timestamp,
        int targetWidth = 520,
        int targetHeight = 292,
        CancellationToken cancellationToken = default) => Task.FromResult<CandidateVideoFrame?>(null);

    Task<IReadOnlyList<CandidateVideoFrame>> ExtractFramesAroundTimeAsync(
        string filePath,
        TimeSpan startTime,
        int frameCount,
        TimeSpan interval,
        int targetWidth = 520,
        int targetHeight = 292,
        CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<CandidateVideoFrame>>(Array.Empty<CandidateVideoFrame>());
}
