using System;
using System.Threading;
using System.Threading.Tasks;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Orchestrates the entire embedded artwork and video thumbnail extraction pipeline,
/// updates SQLite entity records, coordinates background batch processing, and dispatches UI update events.
/// </summary>
public interface IArtworkPipelineService
{
    event Action<int, string?, string?>? ArtworkUpdated;
    event Action<int, string?, string?>? SeriesArtworkUpdated;

    Task<ArtworkExtractionResult> ProcessMovieArtworkAsync(
        int movieId,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default);

    Task<ArtworkExtractionResult> ProcessSeriesArtworkAsync(
        int seriesId,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default);

    Task<ArtworkExtractionResult> ProcessEpisodeArtworkAsync(
        int episodeId,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default);

    Task ProcessPendingLibraryArtworkAsync(
        IProgress<ArtworkPipelineProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
