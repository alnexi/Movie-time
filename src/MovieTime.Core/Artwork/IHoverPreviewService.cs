using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Service contract for generating and retrieving cached video preview frames
/// for Continue Watching cards and interactive hover previews.
/// </summary>
public interface IHoverPreviewService
{
    /// <summary>
    /// Gets or extracts a genuine video thumbnail representing the user's saved playback position.
    /// </summary>
    Task<string?> GetOrExtractPositionThumbnailAsync(
        string filePath,
        long sizeBytes,
        DateTime? modifiedUtc,
        long positionSeconds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or extracts a sequence of silent preview frames around the saved playback position
    /// for in-card hover preview playback.
    /// </summary>
    Task<IReadOnlyList<string>> GetOrExtractPreviewFramesAsync(
        string filePath,
        long sizeBytes,
        DateTime? modifiedUtc,
        long positionSeconds,
        CancellationToken cancellationToken = default);
}
