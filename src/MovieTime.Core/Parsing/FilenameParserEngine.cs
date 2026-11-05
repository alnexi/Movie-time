using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Parsing.Models;
using MovieTime.Core.Services;

namespace MovieTime.Core.Parsing;

/// <summary>
/// Production offline filename and metadata parsing engine.
/// Evaluates video filenames and directory hierarchies to classify media, identify clean titles,
/// extract release years, season/episode coordinates, edition markers, and technical tags.
/// </summary>
public sealed class FilenameParserEngine : IFilenameParserEngine
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly ILogger<FilenameParserEngine> _logger;
    private readonly NamingPatternsConfig _config;

    // Known generic directory names that should never be identified as movie or series titles
    private static readonly HashSet<string> GenericFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "movies", "movie", "films", "film", "cinema",
        "tv", "tv shows", "tv show", "tv series", "series", "shows", "television",
        "downloads", "download", "completed", "complete", "media", "videos", "video",
        "incoming", "temp", "tmp", "torrents", "torrent", "seedbox", "library",
        "specials", "extras", "season", "seasons", "disk", "disc"
    };

    // Standard technical tags recognized in scene and P2P filenames
    private static readonly string[] TechnicalTagPatterns = new[]
    {
        // Resolution
        @"2160p", @"4k", @"uhd", @"1080p", @"1080i", @"720p", @"576p", @"480p", @"sd",
        // Source
        @"remux", @"blu-?ray", @"bdrip", @"brrip", @"web-?dl", @"webrip", @"web",
        @"hdrip", @"dvdrip", @"dvd", @"hdtv", @"vob",
        // Video Codec
        @"x265", @"h\.?265", @"hevc", @"x264", @"h\.?264", @"avc", @"av1", @"xvid", @"divx", @"vc-?1", @"10bit", @"8bit",
        // Audio
        @"dts-?hd\s*ma", @"dts-?hd", @"dts", @"truehd", @"atmos", @"eac3", @"ac3", @"aac", @"flac", @"mp3",
        @"dd5\.1", @"dd\+", @"5\.1", @"7\.1", @"2\.0",
        // Dynamic Range
        @"hdr10\+", @"hdr10", @"hdr", @"dolby\s*vision", @"\bdv\b"
    };

    // Edition / Version keywords
    private static readonly Dictionary<string, string> EditionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        { "remastered", "Remastered" },
        { "remaster", "Remastered" },
        { "directors cut", "Director's Cut" },
        { "director's cut", "Director's Cut" },
        { "directors", "Director's Cut" },
        { "extended cut", "Extended Edition" },
        { "extended edition", "Extended Edition" },
        { "extended", "Extended Edition" },
        { "theatrical cut", "Theatrical" },
        { "theatrical", "Theatrical" },
        { "unrated", "Unrated" },
        { "special edition", "Special Edition" },
        { "criterion", "Criterion Edition" },
        { "final cut", "Final Cut" },
        { "imax edition", "IMAX Edition" },
        { "imax", "IMAX" }
    };

    #region Precompiled Regular Expressions

    // TV SxxExx: e.g. Breaking.Bad.S02E03, Show.S01E01-E02, Show.S01E01E02, Show.S01E01-02
    private static readonly Regex TvStandardRegex = new(
        @"^(?<series>.+?)[._\s-]+[sS](?<season>\d{1,3})[eE](?<episode>\d{1,4})(?:(?:[-_~]|e|E|to)(?:[eE]?(?<endEpisode>\d{1,4})))?(?:[._\s-]+(?<epTitle>[^\[\(\n\r]+?))?(?:[._\s-]+(?:\d{3,4}p|4k|bluray|web-?dl|webrip|x264|x265|hevc|aac|dts|remastered).*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );

    // TV Multi-part 1x02 or 01x02: e.g. Game.of.Thrones.1x02, Show.1x02-03
    private static readonly Regex TvNumberXNumberRegex = new(
        @"^(?<series>.+?)[._\s-]+(?<season>\d{1,2})x(?<episode>\d{1,4})(?:-(?<endEpisode>\d{1,4}))?(?:[._\s-]+(?<epTitle>[^\[\(\n\r]+?))?(?:[._\s-]+(?:\d{3,4}p|4k|bluray|web-?dl|webrip|x264|x265|hevc|aac|dts).*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );

    // TV Verbal: e.g. Show.Name.Season.2.Episode.3, Show Name - Season 2 Episode 3 - Title
    private static readonly Regex TvVerbalRegex = new(
        @"^(?<series>.+?)[._\s-]+[sS]eason[._\s-]+(?<season>\d{1,2})[._\s-]+[eE]pisode[._\s-]+(?<episode>\d{1,4})(?:[._\s-]+(?<epTitle>[^\[\(\n\r]+?))?(?:[._\s-]+(?:\d{3,4}p|4k|bluray|web-?dl|webrip|x264|x265|hevc|aac|dts).*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );

    // TV Season Only: e.g. Show.Name.S02.mkv, Show.Name.Season.2.mkv
    private static readonly Regex TvSeasonOnlyRegex = new(
        @"^(?<series>.+?)[._\s-]+(?:[sS](?<season>\d{1,3})|[sS]eason[._\s-]+(?<season>\d{1,3}))(?:[._\s-]+(?:\d{3,4}p|4k|bluray|web-?dl|webrip|x264|x265|hevc|aac|dts).*)?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );

    // TV Episode File in Season Directory: e.g. "03 - Bit by a Dead Bee", "03.mkv", "Episode 3"
    private static readonly Regex TvEpisodeFileOnlyRegex = new(
        @"^(?:[eE]pisode[._\s-]*)?(?<episode>\d{1,4})(?:[._\s-]+(?<epTitle>.+))?$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );

    // Folder Season Pattern: e.g. "Season 2", "Season 02", "S02"
    private static readonly Regex SeasonFolderRegex = new(
        @"^(?:[sS]eason[._\s-]*(?<season>\d{1,3})|[sS](?<season>\d{1,3}))$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        RegexTimeout
    );

    // Movie with Parenthesized Year: e.g. Interstellar (2014) [2160p]
    private static readonly Regex MovieParenthesizedYearRegex = new(
        @"^(?<title>.+?)\s*\((?<year>19\d\d|20\d\d)\)(?<remainder>.*)$",
        RegexOptions.Compiled,
        RegexTimeout
    );

    // Release Group at end of string: e.g. -SPARKS, [YIFY], -FLUX
    private static readonly Regex TrailingReleaseGroupRegex = new(
        @"(?:-([a-zA-Z0-9]{2,15})|\[([a-zA-Z0-9_\.]{2,15})\])$",
        RegexOptions.Compiled,
        RegexTimeout
    );

    #endregion

    public FilenameParserEngine(
        ILogger<FilenameParserEngine>? logger = null,
        IStoragePathService? storagePathService = null)
    {
        _logger = logger ?? NullLogger<FilenameParserEngine>.Instance;
        _config = LoadConfiguration(storagePathService?.NamingPatternsFilePath);
    }

    public ParsedMediaInfo ParsePath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return new ParsedMediaInfo(string.Empty, MediaClassification.Unknown, ParseConfidence.Low, string.Empty);
        }

        var filename = Path.GetFileName(fullPath);
        var folderPath = Path.GetDirectoryName(fullPath);

        return Parse(filename, folderPath);
    }

    public ParsedMediaInfo Parse(string filename, string? folderPath = null)
    {
        if (string.IsNullOrWhiteSpace(filename))
        {
            return new ParsedMediaInfo(string.Empty, MediaClassification.Unknown, ParseConfidence.Low, string.Empty);
        }

        var originalInput = filename;
        var nameWithoutExt = Path.GetFileNameWithoutExtension(filename);
        if (string.IsNullOrWhiteSpace(nameWithoutExt))
        {
            nameWithoutExt = filename;
        }

        // 1. Extract technical tags and release group upfront
        var technicalTags = ExtractTechnicalTags(nameWithoutExt);
        var releaseGroup = ExtractReleaseGroup(nameWithoutExt);

        // 2. Custom User Patterns (highest priority if configured)
        if (TryMatchCustomPatterns(nameWithoutExt, originalInput, technicalTags, releaseGroup, out var customResult))
        {
            return customResult;
        }

        // 3. Television Standard Patterns (SxxExx, 1x02, Season X Episode Y)
        if (TryMatchStandardTv(nameWithoutExt, originalInput, technicalTags, releaseGroup, out var tvResult))
        {
            return tvResult;
        }

        // 4. Television Season-Level File (e.g. Show.Name.S02.mkv)
        if (TryMatchTvSeasonOnly(nameWithoutExt, originalInput, technicalTags, releaseGroup, out var seasonResult))
        {
            return seasonResult;
        }

        // 5. Folder-Context Guided Television Parsing (e.g. Show/Season 2/03 - Title.mkv)
        if (!string.IsNullOrWhiteSpace(folderPath) &&
            TryMatchTvFolderContext(nameWithoutExt, folderPath, originalInput, technicalTags, releaseGroup, out var folderTvResult))
        {
            return folderTvResult;
        }

        // 6. Movie Parenthesized Year Pattern: "Interstellar (2014) [2160p]"
        if (TryMatchParenthesizedMovie(nameWithoutExt, originalInput, technicalTags, releaseGroup, out var parenMovieResult))
        {
            return parenMovieResult;
        }

        // 7. Standard Delimited Movie Pattern with Year: "The.Dark.Knight.2008.1080p.BluRay" or "1917.2019.1080p"
        if (TryMatchDelimitedMovieWithYear(nameWithoutExt, originalInput, technicalTags, releaseGroup, out var movieWithYearResult))
        {
            return movieWithYearResult;
        }

        // 8. Folder-Context Guided Movie Parsing (e.g. Movies/The Dark Knight (2008)/The.Dark.Knight.1080p.mkv)
        if (!string.IsNullOrWhiteSpace(folderPath) &&
            TryMatchMovieFolderContext(nameWithoutExt, folderPath, originalInput, technicalTags, releaseGroup, out var folderMovieResult))
        {
            return folderMovieResult;
        }

        // 9. Delimited Movie Pattern without Year (e.g. "Gladiator.1080p.BluRay" or "The.Shawshank.Redemption")
        if (TryMatchDelimitedMovieWithoutYear(nameWithoutExt, originalInput, technicalTags, releaseGroup, out var movieWithoutYearResult))
        {
            return movieWithoutYearResult;
        }

        // 10. Conservative Fallback: Clean title without forcing classification
        var fallbackTitle = CleanTitle(nameWithoutExt);
        var fallbackConfidence = !string.IsNullOrWhiteSpace(fallbackTitle) ? ParseConfidence.Low : ParseConfidence.Low;

        return new ParsedMediaInfo(
            originalInput: originalInput,
            classification: MediaClassification.Unknown,
            confidence: fallbackConfidence,
            title: fallbackTitle,
            detectedTechnicalTags: technicalTags,
            releaseGroup: releaseGroup
        );
    }

    #region Pattern Matchers

    private bool TryMatchCustomPatterns(
        string rawName,
        string originalInput,
        IReadOnlyList<string> technicalTags,
        string? releaseGroup,
        out ParsedMediaInfo result)
    {
        result = null!;

        // Check custom TV patterns first
        foreach (var rule in _config.TvPatterns)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            try
            {
                var match = Regex.Match(rawName, rule.Pattern, RegexOptions.IgnoreCase, RegexTimeout);
                if (match.Success)
                {
                    var series = match.Groups["series"].Value;
                    var seasonStr = match.Groups["season"].Value;
                    var epStr = match.Groups["episode"].Value;
                    var endEpStr = match.Groups["endEpisode"].Value;
                    var epTitle = match.Groups["title"].Value;

                    int? season = int.TryParse(seasonStr, out var s) ? s : null;
                    int? ep = int.TryParse(epStr, out var e) ? e : null;
                    int? endEp = int.TryParse(endEpStr, out var ee) ? ee : null;

                    var title = CleanTitle(series);
                    result = new ParsedMediaInfo(
                        originalInput: originalInput,
                        classification: ep.HasValue ? MediaClassification.TvEpisode : MediaClassification.TvSeason,
                        confidence: ParseConfidence.High,
                        title: title,
                        seasonNumber: season,
                        episodeNumber: ep,
                        endEpisodeNumber: endEp,
                        episodeTitle: !string.IsNullOrWhiteSpace(epTitle) ? CleanTitle(epTitle) : null,
                        detectedTechnicalTags: technicalTags,
                        releaseGroup: releaseGroup
                    );
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Custom TV pattern '{RuleName}' evaluation failed: {Message}", rule.Name, ex.Message);
            }
        }

        // Check custom Movie patterns
        foreach (var rule in _config.MoviePatterns)
        {
            if (string.IsNullOrWhiteSpace(rule.Pattern)) continue;
            try
            {
                var match = Regex.Match(rawName, rule.Pattern, RegexOptions.IgnoreCase, RegexTimeout);
                if (match.Success)
                {
                    var titleRaw = match.Groups["title"].Value;
                    var yearStr = match.Groups["year"].Value;
                    var edition = match.Groups["edition"].Value;

                    int? year = int.TryParse(yearStr, out var y) ? y : null;

                    result = new ParsedMediaInfo(
                        originalInput: originalInput,
                        classification: MediaClassification.Movie,
                        confidence: ParseConfidence.High,
                        title: CleanTitle(titleRaw),
                        year: year,
                        edition: !string.IsNullOrWhiteSpace(edition) ? CleanTitle(edition) : null,
                        detectedTechnicalTags: technicalTags,
                        releaseGroup: releaseGroup
                    );
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Custom movie pattern '{RuleName}' evaluation failed: {Message}", rule.Name, ex.Message);
            }
        }

        return false;
    }

    private bool TryMatchStandardTv(
        string rawName,
        string originalInput,
        IReadOnlyList<string> technicalTags,
        string? releaseGroup,
        out ParsedMediaInfo result)
    {
        result = null!;

        Match match = TvStandardRegex.Match(rawName);
        if (!match.Success)
        {
            match = TvNumberXNumberRegex.Match(rawName);
        }
        if (!match.Success)
        {
            match = TvVerbalRegex.Match(rawName);
        }

        if (match.Success)
        {
            var seriesRaw = match.Groups["series"].Value;
            var seasonStr = match.Groups["season"].Value;
            var episodeStr = match.Groups["episode"].Value;
            var endEpisodeStr = match.Groups["endEpisode"].Value;
            var epTitleRaw = match.Groups["epTitle"].Value;

            if (int.TryParse(seasonStr, out var season) && int.TryParse(episodeStr, out var episode))
            {
                int? endEpisode = int.TryParse(endEpisodeStr, out var end) ? end : null;

                // Handle single digit end episode e.g. S01E01-02 where endEpisode is 2 (must be >= episode)
                if (endEpisode.HasValue && endEpisode.Value < episode && endEpisode.Value < 10)
                {
                    var prefix = (episode / 10) * 10;
                    endEpisode = prefix + endEpisode.Value;
                }

                var cleanSeries = CleanTitle(seriesRaw);
                var cleanEpisodeTitle = !string.IsNullOrWhiteSpace(epTitleRaw) ? CleanEpisodeTitle(epTitleRaw) : null;

                result = new ParsedMediaInfo(
                    originalInput: originalInput,
                    classification: MediaClassification.TvEpisode,
                    confidence: ParseConfidence.High,
                    title: cleanSeries,
                    seasonNumber: season,
                    episodeNumber: episode,
                    endEpisodeNumber: endEpisode,
                    episodeTitle: cleanEpisodeTitle,
                    detectedTechnicalTags: technicalTags,
                    releaseGroup: releaseGroup
                );
                return true;
            }
        }

        return false;
    }

    private bool TryMatchTvSeasonOnly(
        string rawName,
        string originalInput,
        IReadOnlyList<string> technicalTags,
        string? releaseGroup,
        out ParsedMediaInfo result)
    {
        result = null!;

        var match = TvSeasonOnlyRegex.Match(rawName);
        if (match.Success)
        {
            var seriesRaw = match.Groups["series"].Value;
            var seasonStr = match.Groups["season"].Value;

            if (int.TryParse(seasonStr, out var season))
            {
                var cleanSeries = CleanTitle(seriesRaw);
                result = new ParsedMediaInfo(
                    originalInput: originalInput,
                    classification: MediaClassification.TvSeason,
                    confidence: ParseConfidence.High,
                    title: cleanSeries,
                    seasonNumber: season,
                    detectedTechnicalTags: technicalTags,
                    releaseGroup: releaseGroup
                );
                return true;
            }
        }

        return false;
    }

    private bool TryMatchTvFolderContext(
        string rawName,
        string folderPath,
        string originalInput,
        IReadOnlyList<string> technicalTags,
        string? releaseGroup,
        out ParsedMediaInfo result)
    {
        result = null!;

        // Check if parent directory is a Season directory (e.g. "Season 2", "Season 02", "S02")
        var parentDirName = GetDirectoryNameFromPath(folderPath, 0);
        if (string.IsNullOrEmpty(parentDirName)) return false;

        var seasonMatch = SeasonFolderRegex.Match(parentDirName.Trim());
        if (!seasonMatch.Success || !int.TryParse(seasonMatch.Groups["season"].Value, out var season))
        {
            return false;
        }

        // Grandparent should be the series name (if not a generic folder name like "TV Shows")
        var grandparentDirName = GetDirectoryNameFromPath(folderPath, 1);
        if (string.IsNullOrEmpty(grandparentDirName) || GenericFolderNames.Contains(grandparentDirName.Trim()))
        {
            return false;
        }

        // Check if filename contains an episode number: e.g. "03 - Bit by a Dead Bee.mkv", "03.mkv", "Episode 3"
        var epMatch = TvEpisodeFileOnlyRegex.Match(rawName.Trim());
        if (epMatch.Success && int.TryParse(epMatch.Groups["episode"].Value, out var episode))
        {
            var epTitleRaw = epMatch.Groups["epTitle"].Value;
            var cleanEpTitle = !string.IsNullOrWhiteSpace(epTitleRaw) ? CleanEpisodeTitle(epTitleRaw) : null;

            result = new ParsedMediaInfo(
                originalInput: originalInput,
                classification: MediaClassification.TvEpisode,
                confidence: ParseConfidence.High,
                title: CleanTitle(grandparentDirName),
                seasonNumber: season,
                episodeNumber: episode,
                episodeTitle: cleanEpTitle,
                detectedTechnicalTags: technicalTags,
                releaseGroup: releaseGroup,
                folderContextUsed: true
            );
            return true;
        }

        return false;
    }

    private bool TryMatchParenthesizedMovie(
        string rawName,
        string originalInput,
        IReadOnlyList<string> technicalTags,
        string? releaseGroup,
        out ParsedMediaInfo result)
    {
        result = null!;

        var match = MovieParenthesizedYearRegex.Match(rawName);
        if (match.Success)
        {
            var titleRaw = match.Groups["title"].Value;
            var yearStr = match.Groups["year"].Value;
            var remainder = match.Groups["remainder"].Value;

            if (int.TryParse(yearStr, out var year) && IsValidMovieYear(year))
            {
                var cleanTitle = CleanTitle(titleRaw);
                var edition = ExtractEdition(remainder);

                result = new ParsedMediaInfo(
                    originalInput: originalInput,
                    classification: MediaClassification.Movie,
                    confidence: ParseConfidence.High,
                    title: cleanTitle,
                    year: year,
                    edition: edition,
                    detectedTechnicalTags: technicalTags,
                    releaseGroup: releaseGroup
                );
                return true;
            }
        }

        return false;
    }

    private bool TryMatchDelimitedMovieWithYear(
        string rawName,
        string originalInput,
        IReadOnlyList<string> technicalTags,
        string? releaseGroup,
        out ParsedMediaInfo result)
    {
        result = null!;

        // Tokenize using standard delimiters (. _ and space)
        var tokens = Regex.Split(rawName, @"[._\s]+", RegexOptions.Compiled, RegexTimeout)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (tokens.Count == 0) return false;

        // Locate candidate release year.
        // We look backwards from the end of tokens, or just before technical tags.
        int yearIndex = -1;
        int detectedYear = 0;

        for (int i = tokens.Count - 1; i >= 0; i--)
        {
            var token = tokens[i].Trim('(', ')', '[', ']');
            if (token.Length == 4 && int.TryParse(token, out var candidateYear) && IsValidMovieYear(candidateYear))
            {
                // Ensure this year doesn't leave the title empty (e.g. "1917.2019" -> 2019 is year, 1917 is title)
                if (i > 0)
                {
                    yearIndex = i;
                    detectedYear = candidateYear;
                    break;
                }
            }
        }

        if (yearIndex > 0)
        {
            var titleTokens = tokens.Take(yearIndex).ToList();
            var remainderTokens = tokens.Skip(yearIndex + 1).ToList();

            var cleanTitle = CleanTitle(string.Join(" ", titleTokens));
            var edition = ExtractEdition(string.Join(" ", remainderTokens));

            result = new ParsedMediaInfo(
                originalInput: originalInput,
                classification: MediaClassification.Movie,
                confidence: ParseConfidence.High,
                title: cleanTitle,
                year: detectedYear,
                edition: edition,
                detectedTechnicalTags: technicalTags,
                releaseGroup: releaseGroup
            );
            return true;
        }

        return false;
    }

    private bool TryMatchDelimitedMovieWithoutYear(
        string rawName,
        string originalInput,
        IReadOnlyList<string> technicalTags,
        string? releaseGroup,
        out ParsedMediaInfo result)
    {
        result = null!;

        // Tokenize using standard delimiters (. _ and space)
        var tokens = Regex.Split(rawName, @"[._\s]+", RegexOptions.Compiled, RegexTimeout)
            .Where(t => !string.IsNullOrWhiteSpace(t))
            .ToList();

        if (tokens.Count == 0) return false;

        // What if no year is present, but clear technical tags or release indicators are present?
        // e.g. "Gladiator.1080p.BluRay"
        int firstTechIndex = -1;
        for (int i = 0; i < tokens.Count; i++)
        {
            var t = tokens[i].Trim('(', ')', '[', ']');
            if (IsTechnicalTag(t))
            {
                firstTechIndex = i;
                break;
            }
        }

        if (firstTechIndex > 0)
        {
            var titleTokens = tokens.Take(firstTechIndex).ToList();
            var cleanTitle = CleanTitle(string.Join(" ", titleTokens));

            if (!string.IsNullOrWhiteSpace(cleanTitle))
            {
                result = new ParsedMediaInfo(
                    originalInput: originalInput,
                    classification: MediaClassification.Movie,
                    confidence: ParseConfidence.Medium,
                    title: cleanTitle,
                    year: null,
                    detectedTechnicalTags: technicalTags,
                    releaseGroup: releaseGroup
                );
                return true;
            }
        }

        return false;
    }

    private bool TryMatchMovieFolderContext(
        string rawName,
        string folderPath,
        string originalInput,
        IReadOnlyList<string> technicalTags,
        string? releaseGroup,
        out ParsedMediaInfo result)
    {
        result = null!;

        // Check if parent directory contains "Title (Year)" e.g. "The Dark Knight (2008)"
        var parentDirName = GetDirectoryNameFromPath(folderPath, 0);
        if (!string.IsNullOrEmpty(parentDirName) && !GenericFolderNames.Contains(parentDirName.Trim()))
        {
            var match = MovieParenthesizedYearRegex.Match(parentDirName.Trim());
            if (match.Success)
            {
                var titleRaw = match.Groups["title"].Value;
                var yearStr = match.Groups["year"].Value;

                if (int.TryParse(yearStr, out var year) && IsValidMovieYear(year))
                {
                    result = new ParsedMediaInfo(
                        originalInput: originalInput,
                        classification: MediaClassification.Movie,
                        confidence: ParseConfidence.High,
                        title: CleanTitle(titleRaw),
                        year: year,
                        detectedTechnicalTags: technicalTags,
                        releaseGroup: releaseGroup,
                        folderContextUsed: true
                    );
                    return true;
                }
            }
        }

        return false;
    }

    #endregion

    #region Helper Methods

    private static bool IsValidMovieYear(int year) => year >= 1888 && year <= 2099;

    public static string CleanTitle(string rawTitle)
    {
        if (string.IsNullOrWhiteSpace(rawTitle))
        {
            return string.Empty;
        }

        var text = rawTitle.Trim();

        // 1. Remove leading/trailing brackets, braces, parentheses if wrapping the entire string
        if ((text.StartsWith('[') && text.EndsWith(']')) ||
            (text.StartsWith('(') && text.EndsWith(')')))
        {
            text = text[1..^1].Trim();
        }

        // 2. Normalize dots and underscores to spaces
        text = text.Replace('.', ' ').Replace('_', ' ');

        // 3. Remove known release group prefixes (e.g. "[YTS.MX]" or "[TGx]")
        text = Regex.Replace(text, @"^\[[^\]]+\]\s*", string.Empty, RegexOptions.Compiled, RegexTimeout);

        // 4. Remove standard technical tags that may be present at the tail of title
        foreach (var tagPattern in TechnicalTagPatterns)
        {
            text = Regex.Replace(text, $@"\b{tagPattern}\b", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
        }

        // 5. Clean standalone hyphens while strictly preserving compound word hyphens (e.g. Spider-Man, Sci-Fi)
        text = Regex.Replace(text, @"\s+-\s+", " ", RegexOptions.Compiled, RegexTimeout);
        text = Regex.Replace(text, @"^\s*-\s*", string.Empty, RegexOptions.Compiled, RegexTimeout);
        text = Regex.Replace(text, @"\s*-\s*$", string.Empty, RegexOptions.Compiled, RegexTimeout);

        // 6. Clean residual bracket artifacts
        text = text.Replace('[', ' ').Replace(']', ' ');
        text = text.Replace('(', ' ').Replace(')', ' ');

        // 7. Collapse multiple spaces and trim
        text = Regex.Replace(text, @"\s+", " ", RegexOptions.Compiled, RegexTimeout).Trim();

        return text;
    }

    private static string CleanEpisodeTitle(string rawEpTitle)
    {
        var text = CleanTitle(rawEpTitle);

        // Remove residual technical tags
        foreach (var tagPattern in TechnicalTagPatterns)
        {
            text = Regex.Replace(text, $@"\b{tagPattern}\b.*$", string.Empty, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
        }

        return text.Trim(' ', '-', '_', '.');
    }

    private static IReadOnlyList<string> ExtractTechnicalTags(string input)
    {
        var foundTags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var normalized = input.Replace('_', ' ');

        foreach (var pattern in TechnicalTagPatterns)
        {
            var matches = Regex.Matches(normalized, $@"\b({pattern})\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
            foreach (Match m in matches)
            {
                if (m.Success)
                {
                    foundTags.Add(m.Value.Trim());
                }
            }
        }

        return foundTags.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string? ExtractEdition(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var normalized = text.Replace('.', ' ').Replace('_', ' ');

        foreach (var kvp in EditionKeywords)
        {
            if (Regex.IsMatch(normalized, $@"\b{Regex.Escape(kvp.Key)}\b", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout))
            {
                return kvp.Value;
            }
        }

        return null;
    }

    private static string? ExtractReleaseGroup(string input)
    {
        var match = TrailingReleaseGroupRegex.Match(input);
        if (match.Success)
        {
            var group = !string.IsNullOrEmpty(match.Groups[1].Value)
                ? match.Groups[1].Value
                : match.Groups[2].Value;

            // Ensure extracted group is not merely a technical tag
            if (!IsTechnicalTag(group) && group.Length >= 2)
            {
                return group;
            }
        }

        return null;
    }

    private static bool IsTechnicalTag(string word)
    {
        if (string.IsNullOrWhiteSpace(word)) return false;

        var normalized = word.Replace('_', ' ');
        foreach (var pattern in TechnicalTagPatterns)
        {
            if (Regex.IsMatch(normalized, $@"^{pattern}$", RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetDirectoryNameFromPath(string? folderPath, int parentLevelsUp)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return null;

        try
        {
            var dir = new DirectoryInfo(folderPath);
            for (int i = 0; i < parentLevelsUp; i++)
            {
                if (dir.Parent == null) return null;
                dir = dir.Parent;
            }
            return dir.Name;
        }
        catch
        {
            return null;
        }
    }

    private NamingPatternsConfig LoadConfiguration(string? configFilePath)
    {
        if (string.IsNullOrWhiteSpace(configFilePath) || !File.Exists(configFilePath))
        {
            return new NamingPatternsConfig();
        }

        try
        {
            var json = File.ReadAllText(configFilePath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            return JsonSerializer.Deserialize<NamingPatternsConfig>(json, options) ?? new NamingPatternsConfig();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load custom naming patterns from {ConfigPath}. Using default rules.", configFilePath);
            return new NamingPatternsConfig();
        }
    }

    #endregion
}
