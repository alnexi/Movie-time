namespace MovieTime.Core.Artwork;

/// <summary>
/// Progress reporting model for background batch artwork extraction.
/// </summary>
public readonly record struct ArtworkPipelineProgress(
    int TotalItems,
    int ProcessedItems,
    int ExtractedCount,
    int FallbackCount,
    string CurrentMovieTitle)
{
    public double ProgressRatio => TotalItems > 0 ? (double)ProcessedItems / TotalItems : 0.0;
    public double ProgressPercentage => ProgressRatio * 100.0;
}
