namespace MovieTime.Core.Parsing.Models;

/// <summary>
/// A user-configurable regular expression rule for custom filename parsing.
/// Named capture groups: 'title', 'year', 'series', 'season', 'episode', 'endEpisode', 'edition', 'group'.
/// </summary>
public sealed class CustomPatternRule
{
    /// <summary>
    /// Friendly name or identifier for the rule (e.g. "Anime Fansub Format").
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// The regular expression string.
    /// </summary>
    public string Pattern { get; set; } = string.Empty;
}
