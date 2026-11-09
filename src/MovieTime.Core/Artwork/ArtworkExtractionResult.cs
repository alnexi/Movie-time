namespace MovieTime.Core.Artwork;

/// <summary>
/// Result of an artwork extraction operation for a movie entity.
/// </summary>
public sealed class ArtworkExtractionResult
{
    public int MovieId { get; init; }
    public int? SeriesId { get; init; }
    public int? EpisodeId { get; init; }
    public string? PosterPath { get; init; }
    public string? BackdropPath { get; init; }
    public bool HasEmbeddedArtwork { get; init; }
    public bool IsPosterFallback { get; init; }
    public bool IsBackdropFallback { get; init; }
    public bool WasCached { get; init; }
    public string? StatusMessage { get; init; }

    public bool HasAnyArtwork => (!string.IsNullOrEmpty(PosterPath) && !IsPosterFallback) ||
                                 (!string.IsNullOrEmpty(BackdropPath) && !IsBackdropFallback);
}
