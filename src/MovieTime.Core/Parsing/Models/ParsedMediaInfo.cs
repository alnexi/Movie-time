using System;
using System.Collections.Generic;

namespace MovieTime.Core.Parsing.Models;

/// <summary>
/// Structured result containing extracted metadata and classification for a media item.
/// </summary>
public sealed class ParsedMediaInfo
{
    /// <summary>
    /// The original raw input string (filename or path).
    /// </summary>
    public string OriginalInput { get; }

    /// <summary>
    /// The classified media type.
    /// </summary>
    public MediaClassification Classification { get; }

    /// <summary>
    /// The confidence level of this parsing result.
    /// </summary>
    public ParseConfidence Confidence { get; }

    /// <summary>
    /// Cleaned title. For movies, this is the movie title; for TV shows, this is the series title.
    /// For unclassified files, this is a safely normalized filename.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Release year if confidently identifiable (e.g. 1900-2099); otherwise null.
    /// </summary>
    public int? Year { get; }

    /// <summary>
    /// Season number for television series; otherwise null.
    /// </summary>
    public int? SeasonNumber { get; }

    /// <summary>
    /// Primary episode number for television episodes; otherwise null.
    /// </summary>
    public int? EpisodeNumber { get; }

    /// <summary>
    /// End episode number when representing an episode range (e.g. S01E01-E02 => 2); otherwise null.
    /// </summary>
    public int? EndEpisodeNumber { get; }

    /// <summary>
    /// All episode numbers represented by this media item.
    /// </summary>
    public IReadOnlyList<int> EpisodeNumbers { get; }

    /// <summary>
    /// Specific episode title if present in the filename (e.g. "Bit by a Dead Bee"); otherwise null.
    /// </summary>
    public string? EpisodeTitle { get; }

    /// <summary>
    /// Edition or version tag (e.g. "Remastered", "Director's Cut", "Extended Edition"); otherwise null.
    /// </summary>
    public string? Edition { get; }

    /// <summary>
    /// Recognized technical tags separated from the title (e.g. "1080p", "BluRay", "x265", "HDR", "DTS").
    /// </summary>
    public IReadOnlyList<string> DetectedTechnicalTags { get; }

    /// <summary>
    /// Identified release group tag if cleanly extracted (e.g. "SPARKS", "YIFY"); otherwise null.
    /// </summary>
    public string? ReleaseGroup { get; }

    /// <summary>
    /// Indicates whether parent folder context was utilized to deduce series or movie metadata.
    /// </summary>
    public bool FolderContextUsed { get; }

    /// <summary>
    /// True if multiple episodes are contained in this single file.
    /// </summary>
    public bool IsMultiEpisode => EpisodeNumbers.Count > 1;

    public ParsedMediaInfo(
        string originalInput,
        MediaClassification classification,
        ParseConfidence confidence,
        string title,
        int? year = null,
        int? seasonNumber = null,
        int? episodeNumber = null,
        int? endEpisodeNumber = null,
        IEnumerable<int>? episodeNumbers = null,
        string? episodeTitle = null,
        string? edition = null,
        IEnumerable<string>? detectedTechnicalTags = null,
        string? releaseGroup = null,
        bool folderContextUsed = false)
    {
        OriginalInput = originalInput ?? string.Empty;
        Classification = classification;
        Confidence = confidence;
        Title = title ?? string.Empty;
        Year = year;
        SeasonNumber = seasonNumber;
        EpisodeNumber = episodeNumber;
        EndEpisodeNumber = endEpisodeNumber;
        EpisodeTitle = episodeTitle;
        Edition = edition;
        ReleaseGroup = releaseGroup;
        FolderContextUsed = folderContextUsed;

        if (episodeNumbers != null)
        {
            EpisodeNumbers = new List<int>(episodeNumbers);
        }
        else if (episodeNumber.HasValue)
        {
            if (endEpisodeNumber.HasValue && endEpisodeNumber.Value >= episodeNumber.Value)
            {
                var list = new List<int>();
                for (int i = episodeNumber.Value; i <= endEpisodeNumber.Value; i++)
                {
                    list.Add(i);
                }
                EpisodeNumbers = list;
            }
            else
            {
                EpisodeNumbers = new[] { episodeNumber.Value };
            }
        }
        else
        {
            EpisodeNumbers = Array.Empty<int>();
        }

        DetectedTechnicalTags = detectedTechnicalTags != null
            ? new List<string>(detectedTechnicalTags)
            : Array.Empty<string>();
    }

    public override string ToString()
    {
        return Classification switch
        {
            MediaClassification.Movie => Year.HasValue
                ? $"{Title} ({Year.Value})"
                : Title,
            MediaClassification.TvEpisode => SeasonNumber.HasValue && EpisodeNumber.HasValue
                ? $"{Title} S{SeasonNumber:D2}E{EpisodeNumber:D2}" + (IsMultiEpisode ? $"-E{EndEpisodeNumber:D2}" : "") + (!string.IsNullOrWhiteSpace(EpisodeTitle) ? $" - {EpisodeTitle}" : "")
                : Title,
            MediaClassification.TvSeason => SeasonNumber.HasValue
                ? $"{Title} Season {SeasonNumber}"
                : Title,
            _ => Title
        };
    }
}
