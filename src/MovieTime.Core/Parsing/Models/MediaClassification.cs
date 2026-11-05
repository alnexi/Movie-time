namespace MovieTime.Core.Parsing.Models;

/// <summary>
/// Categorization of parsed media items.
/// </summary>
public enum MediaClassification
{
    /// <summary>
    /// The media could not be reliably classified or is insufficiently identifiable.
    /// </summary>
    Unknown = 0,

    /// <summary>
    /// A standalone feature film or documentary.
    /// </summary>
    Movie = 1,

    /// <summary>
    /// An individual episode of a television series.
    /// </summary>
    TvEpisode = 2,

    /// <summary>
    /// A season-level container or multi-episode season archive (e.g. S02 full season file).
    /// </summary>
    TvSeason = 3
}
