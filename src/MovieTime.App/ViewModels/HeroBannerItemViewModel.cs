namespace MovieTime.App.ViewModels;

/// <summary>
/// Data model for an individual featured movie item in the rotating Hero Banner collection.
/// </summary>
public sealed class HeroBannerItemViewModel
{
    public int MovieId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Genre { get; set; } = "Feature Film";
    public string RatingBadge { get; set; } = string.Empty;
    public string QualityBadge { get; set; } = "1080p";
    public string Description { get; set; } = string.Empty;
    public string? BackdropPath { get; set; }
    public string? FilePath { get; set; }
    public string ActionButtonText { get; set; } = "Play Movie";
}
