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
}
