namespace MovieTime.Core.Parsing.Models;

/// <summary>
/// Indicates the degree of certainty of the parsing engine's classification and extracted fields.
/// </summary>
public enum ParseConfidence
{
    /// <summary>
    /// Parsing relied on conservative fallback rules; key metadata may be incomplete or uncertain.
    /// </summary>
    Low = 0,

    /// <summary>
    /// Metadata was extracted using heuristics, partial tags, or folder context.
    /// </summary>
    Medium = 1,

    /// <summary>
    /// Unambiguous pattern match with distinct standard tags (e.g. S01E02 or Title (Year) [Quality]).
    /// </summary>
    High = 2
}
