using System.Collections.Generic;

namespace MovieTime.Core.Parsing.Models;

/// <summary>
/// Configuration schema stored in %LocalAppData%\MovieTime\Config\naming_patterns.json.
/// Allows power users to declare custom regex patterns for unorthodox file naming schemes.
/// </summary>
public sealed class NamingPatternsConfig
{
    /// <summary>
    /// Custom regular expression patterns for movies.
    /// Named groups supported: 'title', 'year', 'edition', 'group'.
    /// </summary>
    public List<CustomPatternRule> MoviePatterns { get; set; } = new();

    /// <summary>
    /// Custom regular expression patterns for TV series and episodes.
    /// Named groups supported: 'series', 'season', 'episode', 'endEpisode', 'title', 'edition', 'group'.
    /// </summary>
    public List<CustomPatternRule> TvPatterns { get; set; } = new();
}
