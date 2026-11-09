using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Extensions;
using MovieTime.Core.Parsing;
using MovieTime.Core.Parsing.Models;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class FilenameParserTests : IDisposable
{
    private readonly string _tempConfigDir;
    private readonly StoragePathService _storagePathService;
    private readonly FilenameParserEngine _parser;

    public FilenameParserTests()
    {
        _tempConfigDir = Path.Combine(Path.GetTempPath(), "MovieTime_ParserTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempConfigDir);
        _storagePathService = new StoragePathService(_tempConfigDir);
        _parser = new FilenameParserEngine(NullLogger<FilenameParserEngine>.Instance, _storagePathService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempConfigDir))
            {
                Directory.Delete(_tempConfigDir, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in temp directory
        }
    }

    #region Category 1: Common Movie Filenames

    [Fact]
    public void Parse_Movie_TheDarkKnight_ExtractsTitleYearAndTags()
    {
        var result = _parser.Parse("The.Dark.Knight.2008.1080p.BluRay.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("The Dark Knight", result.Title);
        Assert.Equal(2008, result.Year);
        Assert.Contains("1080p", result.DetectedTechnicalTags);
        Assert.Contains("BluRay", result.DetectedTechnicalTags);
        Assert.Equal(ParseConfidence.High, result.Confidence);
    }

    [Fact]
    public void Parse_Movie_InterstellarParenthesizedYear_ExtractsTitleAndYear()
    {
        var result = _parser.Parse("Interstellar (2014) [2160p].mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Interstellar", result.Title);
        Assert.Equal(2014, result.Year);
        Assert.Contains("2160p", result.DetectedTechnicalTags);
        Assert.Equal(ParseConfidence.High, result.Confidence);
    }

    [Fact]
    public void Parse_Movie_BladeRunner2049_DistinguishesTitleNumberFromYear()
    {
        var result = _parser.Parse("Blade.Runner.2049.2017.WEB-DL.x265.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Blade Runner 2049", result.Title);
        Assert.Equal(2017, result.Year);
        Assert.Contains("x265", result.DetectedTechnicalTags);
        Assert.Equal(ParseConfidence.High, result.Confidence);
    }

    [Fact]
    public void Parse_Movie_TheMatrixRemastered_ExtractsEdition()
    {
        var result = _parser.Parse("The.Matrix.1999.REMASTERED.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("The Matrix", result.Title);
        Assert.Equal(1999, result.Year);
        Assert.Equal("Remastered", result.Edition);
        Assert.Contains("1080p", result.DetectedTechnicalTags);
    }

    [Fact]
    public void Parse_Movie_DunePartTwo_ExtractsMultiWordTitleAndYear()
    {
        var result = _parser.Parse("Dune.Part.Two.2024.2160p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Dune Part Two", result.Title);
        Assert.Equal(2024, result.Year);
        Assert.Contains("2160p", result.DetectedTechnicalTags);
    }

    [Fact]
    public void Parse_Movie_1917_CorrectlyDifferentiatesTitleFromYear()
    {
        var result = _parser.Parse("1917.2019.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("1917", result.Title);
        Assert.Equal(2019, result.Year);
        Assert.Contains("1080p", result.DetectedTechnicalTags);
    }

    [Fact]
    public void Parse_Movie_SpiderManNoWayHome_PreservesHyphenatedCompound()
    {
        var result = _parser.Parse("Spider-Man.No.Way.Home.2021.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Spider-Man No Way Home", result.Title);
        Assert.Equal(2021, result.Year);
    }

    [Fact]
    public void Parse_Movie_OppenheimerImax_ExtractsImaxEditionAndTags()
    {
        var result = _parser.Parse("Oppenheimer.2023.IMAX.2160p.UHD.HDR.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Oppenheimer", result.Title);
        Assert.Equal(2023, result.Year);
        Assert.Equal("IMAX", result.Edition);
        Assert.Contains("2160p", result.DetectedTechnicalTags);
        Assert.Contains("HDR", result.DetectedTechnicalTags);
    }

    [Fact]
    public void Parse_Movie_AvatarTheWayOfWater_ExtractsCleanTitle()
    {
        var result = _parser.Parse("Avatar.The.Way.of.Water.2022.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Avatar The Way of Water", result.Title);
        Assert.Equal(2022, result.Year);
    }

    [Fact]
    public void Parse_Movie_TopGunMaverick_ExtractsAudioTags()
    {
        var result = _parser.Parse("Top.Gun.Maverick.2022.2160p.WEB-DL.Atmos.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Top Gun Maverick", result.Title);
        Assert.Equal(2022, result.Year);
        Assert.Contains("2160p", result.DetectedTechnicalTags);
        Assert.Contains("Atmos", result.DetectedTechnicalTags);
    }

    #endregion

    #region Category 2: Movie Titles with Numbers & Punctuation

    [Fact]
    public void Parse_Movie_BladeRunner2049_WithParenthesizedYear()
    {
        var result = _parser.Parse("Blade Runner 2049 (2017).mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Blade Runner 2049", result.Title);
        Assert.Equal(2017, result.Year);
    }

    [Fact]
    public void Parse_Movie_2001ASpaceOdyssey_PreservesLeadingNumber()
    {
        var result = _parser.Parse("2001.A.Space.Odyssey.1968.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("2001 A Space Odyssey", result.Title);
        Assert.Equal(1968, result.Year);
    }

    [Fact]
    public void Parse_Movie_Apollo13_PreservesTitleNumber()
    {
        var result = _parser.Parse("Apollo.13.1995.BluRay.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Apollo 13", result.Title);
        Assert.Equal(1995, result.Year);
    }

    [Fact]
    public void Parse_Movie_Seven1995_ExtractsCleanTitle()
    {
        var result = _parser.Parse("Seven.1995.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Seven", result.Title);
        Assert.Equal(1995, result.Year);
    }

    [Fact]
    public void Parse_Movie_MissionImpossiblePartOne_ExtractsLongTitle()
    {
        var result = _parser.Parse("Mission.Impossible.Dead.Reckoning.Part.One.2023.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Mission Impossible Dead Reckoning Part One", result.Title);
        Assert.Equal(2023, result.Year);
    }

    [Fact]
    public void Parse_Movie_IronMan2_PreservesSequelNumber()
    {
        var result = _parser.Parse("Iron.Man.2.2010.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Iron Man 2", result.Title);
        Assert.Equal(2010, result.Year);
    }

    [Fact]
    public void Parse_Movie_FastAndFurious6_PreservesAmpersand()
    {
        var result = _parser.Parse("Fast.&.Furious.6.2013.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Fast & Furious 6", result.Title);
        Assert.Equal(2013, result.Year);
    }

    [Fact]
    public void Parse_Movie_SpiderMan2_PreservesHyphenAndNumber()
    {
        var result = _parser.Parse("Spider-Man.2.2004.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Spider-Man 2", result.Title);
        Assert.Equal(2004, result.Year);
    }

    [Fact]
    public void Parse_Movie_XMenFirstClass_PreservesHyphen()
    {
        var result = _parser.Parse("X-Men.First.Class.2011.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("X-Men First Class", result.Title);
        Assert.Equal(2011, result.Year);
    }

    #endregion

    #region Category 3: Movies Without Release Years

    [Fact]
    public void Parse_Movie_TheShawshankRedemption_NoYear_ParsesCleanTitle()
    {
        var result = _parser.Parse("The.Shawshank.Redemption.mkv");

        Assert.Equal("The Shawshank Redemption", result.Title);
        Assert.Null(result.Year);
    }

    [Fact]
    public void Parse_Movie_GladiatorWithTags_NoYear_ExtractsTitleAndTags()
    {
        var result = _parser.Parse("Gladiator.1080p.BluRay.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Gladiator", result.Title);
        Assert.Null(result.Year);
        Assert.Contains("1080p", result.DetectedTechnicalTags);
        Assert.Contains("BluRay", result.DetectedTechnicalTags);
    }

    [Fact]
    public void Parse_Movie_PulpFiction_NoTagsNoYear_CleansTitle()
    {
        var result = _parser.Parse("Pulp.Fiction.mkv");

        Assert.Equal("Pulp Fiction", result.Title);
        Assert.Null(result.Year);
    }

    [Fact]
    public void Parse_Movie_FightClub_Spaces_NoYear()
    {
        var result = _parser.Parse("Fight Club.mkv");

        Assert.Equal("Fight Club", result.Title);
        Assert.Null(result.Year);
    }

    [Fact]
    public void Parse_Movie_InceptionBracketedTag_NoYear()
    {
        var result = _parser.Parse("Inception [1080p].mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Inception", result.Title);
        Assert.Null(result.Year);
        Assert.Contains("1080p", result.DetectedTechnicalTags);
    }

    #endregion

    #region Category 4: Movie Years Disambiguation (Year vs Title)

    [Fact]
    public void Parse_Movie_1917_WithoutYearTag_DoesNotConfuseTitleWithYear()
    {
        var result = _parser.Parse("1917.mkv");

        Assert.Equal("1917", result.Title);
        Assert.Null(result.Year);
    }

    [Fact]
    public void Parse_Movie_1917_With1080pTag_DoesNotDropTitle()
    {
        var result = _parser.Parse("1917.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("1917", result.Title);
        Assert.Null(result.Year);
        Assert.Contains("1080p", result.DetectedTechnicalTags);
    }

    [Fact]
    public void Parse_Movie_2012_WithYear2009()
    {
        var result = _parser.Parse("2012.2009.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("2012", result.Title);
        Assert.Equal(2009, result.Year);
    }

    [Fact]
    public void Parse_Movie_300_WithYear2006()
    {
        var result = _parser.Parse("300.2006.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("300", result.Title);
        Assert.Equal(2006, result.Year);
    }

    #endregion

    #region Category 5: Release Groups and Trailing Tags

    [Fact]
    public void Parse_Movie_InceptionSparks_ExtractsReleaseGroup()
    {
        var result = _parser.Parse("Inception.2010.1080p.BluRay-SPARKS.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Inception", result.Title);
        Assert.Equal(2010, result.Year);
        Assert.Equal("SPARKS", result.ReleaseGroup);
    }

    [Fact]
    public void Parse_Movie_InterstellarYts_ExtractsBracketedReleaseGroup()
    {
        var result = _parser.Parse("Interstellar.2014.720p.BluRay.x264-[YTS.AM].mp4");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Interstellar", result.Title);
        Assert.Equal(2014, result.Year);
        Assert.Equal("YTS.AM", result.ReleaseGroup);
    }

    [Fact]
    public void Parse_Movie_DuneFlux_ExtractsTrailingGroup()
    {
        var result = _parser.Parse("Dune.2021.2160p.WEB-DL-FLUX.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Dune", result.Title);
        Assert.Equal(2021, result.Year);
        Assert.Equal("FLUX", result.ReleaseGroup);
    }

    [Fact]
    public void Parse_Movie_TheBatmanRarbg_ExtractsGroup()
    {
        var result = _parser.Parse("The.Batman.2022.1080p-RARBG.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("The Batman", result.Title);
        Assert.Equal(2022, result.Year);
        Assert.Equal("RARBG", result.ReleaseGroup);
    }

    #endregion

    #region Category 6: Common TV Episode Formats

    [Fact]
    public void Parse_TV_BreakingBadStandard_ExtractsSeasonAndEpisode()
    {
        var result = _parser.Parse("Breaking.Bad.S02E03.720p.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Breaking Bad", result.Title);
        Assert.Equal(2, result.SeasonNumber);
        Assert.Equal(3, result.EpisodeNumber);
        Assert.Contains("720p", result.DetectedTechnicalTags);
        Assert.Equal(ParseConfidence.High, result.Confidence);
    }

    [Fact]
    public void Parse_TV_BreakingBadWithEpisodeTitle_ExtractsEpisodeTitle()
    {
        var result = _parser.Parse("Breaking Bad - S02E03 - Bit by a Dead Bee.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Breaking Bad", result.Title);
        Assert.Equal(2, result.SeasonNumber);
        Assert.Equal(3, result.EpisodeNumber);
        Assert.Equal("Bit by a Dead Bee", result.EpisodeTitle);
        Assert.Equal(ParseConfidence.High, result.Confidence);
    }

    [Fact]
    public void Parse_TV_GameOfThrones_1x02_ExtractsSeasonAndEpisode()
    {
        var result = _parser.Parse("Game.of.Thrones.1x02.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Game of Thrones", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(2, result.EpisodeNumber);
    }

    [Fact]
    public void Parse_TV_TheOfficeUS_PreservesRegionalQualifier()
    {
        var result = _parser.Parse("The.Office.US.S03E14.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Office US", result.Title);
        Assert.Equal(3, result.SeasonNumber);
        Assert.Equal(14, result.EpisodeNumber);
    }

    [Fact]
    public void Parse_TV_VerbalSeasonEpisode_ExtractsCorrectly()
    {
        var result = _parser.Parse("Show.Name.Season.2.Episode.3.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Show Name", result.Title);
        Assert.Equal(2, result.SeasonNumber);
        Assert.Equal(3, result.EpisodeNumber);
    }

    [Fact]
    public void Parse_TV_Succession_WithEpisodeTitleAndTags()
    {
        var result = _parser.Parse("Succession.S04E10.With.Open.Eyes.1080p.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Succession", result.Title);
        Assert.Equal(4, result.SeasonNumber);
        Assert.Equal(10, result.EpisodeNumber);
        Assert.Equal("With Open Eyes", result.EpisodeTitle);
        Assert.Contains("1080p", result.DetectedTechnicalTags);
    }

    [Fact]
    public void Parse_TV_StrangerThings_LongEpisodeTitle()
    {
        var result = _parser.Parse("Stranger.Things.S01E01.Chapter.One.The.Vanishing.of.Will.Byers.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Stranger Things", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1, result.EpisodeNumber);
        Assert.Equal("Chapter One The Vanishing of Will Byers", result.EpisodeTitle);
    }

    [Fact]
    public void Parse_TV_Chernobyl_CleanParse()
    {
        var result = _parser.Parse("Chernobyl.S01E01.1080p.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Chernobyl", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1, result.EpisodeNumber);
    }

    #endregion

    #region Category 7: TV Season-Level Files

    [Fact]
    public void Parse_TV_SeasonOnly_S02_ClassifiesAsTvSeason()
    {
        var result = _parser.Parse("Show.Name.S02.mkv");

        Assert.Equal(MediaClassification.TvSeason, result.Classification);
        Assert.Equal("Show Name", result.Title);
        Assert.Equal(2, result.SeasonNumber);
        Assert.Null(result.EpisodeNumber);
    }

    [Fact]
    public void Parse_TV_SeasonOnly_VerbalSeason_ClassifiesAsTvSeason()
    {
        var result = _parser.Parse("Breaking.Bad.Season.1.mkv");

        Assert.Equal(MediaClassification.TvSeason, result.Classification);
        Assert.Equal("Breaking Bad", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Null(result.EpisodeNumber);
    }

    [Fact]
    public void Parse_TV_SeasonOnly_WestworldWithTags()
    {
        var result = _parser.Parse("Westworld.S03.1080p.mkv");

        Assert.Equal(MediaClassification.TvSeason, result.Classification);
        Assert.Equal("Westworld", result.Title);
        Assert.Equal(3, result.SeasonNumber);
        Assert.Contains("1080p", result.DetectedTechnicalTags);
    }

    #endregion

    #region Category 8: Multi-Episode Ranges

    [Fact]
    public void Parse_TV_MultiEpisode_HyphenatedE()
    {
        var result = _parser.Parse("Show.Name.S01E01-E02.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Show Name", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1, result.EpisodeNumber);
        Assert.Equal(2, result.EndEpisodeNumber);
        Assert.True(result.IsMultiEpisode);
        Assert.Equal(new[] { 1, 2 }, result.EpisodeNumbers);
    }

    [Fact]
    public void Parse_TV_MultiEpisode_DoubleE()
    {
        var result = _parser.Parse("Show.Name.S01E01E02.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Show Name", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1, result.EpisodeNumber);
        Assert.Equal(2, result.EndEpisodeNumber);
        Assert.True(result.IsMultiEpisode);
        Assert.Equal(new[] { 1, 2 }, result.EpisodeNumbers);
    }

    [Fact]
    public void Parse_TV_MultiEpisode_HyphenatedTwoDigits()
    {
        var result = _parser.Parse("Show.Name.S01E01-02.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Show Name", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1, result.EpisodeNumber);
        Assert.Equal(2, result.EndEpisodeNumber);
        Assert.True(result.IsMultiEpisode);
    }

    [Fact]
    public void Parse_TV_MultiEpisode_NumberXRange()
    {
        var result = _parser.Parse("Game.of.Thrones.1x01-02.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Game of Thrones", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(1, result.EpisodeNumber);
        Assert.Equal(2, result.EndEpisodeNumber);
        Assert.True(result.IsMultiEpisode);
    }

    #endregion

    #region Category 9: Folder Context Parsing

    [Fact]
    public void Parse_FolderContext_EpisodeInSeasonFolder_ExtractsSeriesAndEpisode()
    {
        var folder = @"D:\Media\TV Shows\Breaking Bad\Season 02";
        var result = _parser.Parse("03 - Bit by a Dead Bee.mkv", folder);

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Breaking Bad", result.Title);
        Assert.Equal(2, result.SeasonNumber);
        Assert.Equal(3, result.EpisodeNumber);
        Assert.Equal("Bit by a Dead Bee", result.EpisodeTitle);
        Assert.True(result.FolderContextUsed);
    }

    [Fact]
    public void Parse_FolderContext_MovieInParenthesizedYearFolder_ExtractsYearFromFolder()
    {
        var folder = @"D:\Media\Movies\The Dark Knight (2008)";
        var result = _parser.Parse("The.Dark.Knight.1080p.mkv", folder);

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("The Dark Knight", result.Title);
        Assert.Equal(2008, result.Year);
        Assert.True(result.FolderContextUsed);
    }

    [Fact]
    public void Parse_FolderContext_EpisodeOnlyFileName_ExtractsFromGrandparent()
    {
        var folder = @"C:\Videos\Severance\Season 1";
        var result = _parser.Parse("Episode 5.mkv", folder);

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Severance", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(5, result.EpisodeNumber);
        Assert.True(result.FolderContextUsed);
    }

    [Fact]
    public void Parse_FolderContext_GenericFolders_AreNotUsedAsTitles()
    {
        var folder = @"C:\Downloads\1080p";
        var result = _parser.Parse("film.mkv", folder);

        Assert.NotEqual("1080p", result.Title);
        Assert.NotEqual("Downloads", result.Title);
        Assert.False(result.FolderContextUsed);
    }

    [Fact]
    public void ParsePath_DecomposesFullPathAutomatically()
    {
        var fullPath = @"C:\Media\TV Shows\Breaking Bad\Season 02\Breaking.Bad.S02E03.mkv";
        var result = _parser.ParsePath(fullPath);

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("Breaking Bad", result.Title);
        Assert.Equal(2, result.SeasonNumber);
        Assert.Equal(3, result.EpisodeNumber);
    }

    #endregion

    #region Category 10: Unicode & Non-English Filenames

    [Fact]
    public void Parse_Unicode_FrenchTitleAmelie_PreservesAccents()
    {
        var result = _parser.Parse("Amélie.2001.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Amélie", result.Title);
        Assert.Equal(2001, result.Year);
    }

    [Fact]
    public void Parse_Unicode_PersianMovieASeparation()
    {
        var result = _parser.Parse("جدایی نادر از سیمین (2011).mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("جدایی نادر از سیمین", result.Title);
        Assert.Equal(2011, result.Year);
    }

    [Fact]
    public void Parse_Unicode_JapaneseCharactersPreserved()
    {
        var result = _parser.Parse("Spirited.Away.千と千尋の神隠し.2001.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Contains("千と千尋の神隠し", result.Title);
        Assert.Equal(2001, result.Year);
    }

    [Fact]
    public void Parse_Unicode_PersianTheSalesman()
    {
        var result = _parser.Parse("فروشنده.2016.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("فروشنده", result.Title);
        Assert.Equal(2016, result.Year);
    }

    #endregion

    #region Category 11: Ambiguous & Invalid / Empty Inputs

    [Fact]
    public void Parse_EmptyString_ReturnsUnknownLowConfidence()
    {
        var result = _parser.Parse(string.Empty);

        Assert.Equal(MediaClassification.Unknown, result.Classification);
        Assert.Equal(ParseConfidence.Low, result.Confidence);
        Assert.Empty(result.Title);
    }

    [Fact]
    public void Parse_Whitespace_ReturnsUnknownLowConfidence()
    {
        var result = _parser.Parse("     ");

        Assert.Equal(MediaClassification.Unknown, result.Classification);
        Assert.Equal(ParseConfidence.Low, result.Confidence);
        Assert.Empty(result.Title);
    }

    [Fact]
    public void Parse_SampleFilename_ReturnsCleanTitleWithoutForcingClassification()
    {
        var result = _parser.Parse("sample.mkv");

        Assert.Equal(MediaClassification.Unknown, result.Classification);
        Assert.Equal("sample", result.Title);
        Assert.Null(result.Year);
    }

    [Fact]
    public void Parse_TrailerWithTag_ReturnsCleanFallback()
    {
        var result = _parser.Parse("trailer_1080p.mp4");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("trailer", result.Title);
        Assert.Null(result.Year);
        Assert.Contains("1080p", result.DetectedTechnicalTags);
    }

    [Fact]
    public void Parse_RandomClip_ReturnsCleanTitle()
    {
        var result = _parser.Parse("random_clip_001.mkv");

        Assert.Equal(MediaClassification.Unknown, result.Classification);
        Assert.Equal("random clip 001", result.Title);
    }

    #endregion

    #region Category 12: Title Cleanup & Edition Regressions

    [Fact]
    public void Parse_Movie_DirectorsCut_ExtractsEdition()
    {
        var result = _parser.Parse("The.Matrix.1999.DIRECTORS.CUT.1080p.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("The Matrix", result.Title);
        Assert.Equal(1999, result.Year);
        Assert.Equal("Director's Cut", result.Edition);
    }

    [Fact]
    public void Parse_Movie_ExtendedEdition_ExtractsEdition()
    {
        var result = _parser.Parse("Lord.of.the.Rings.The.Fellowship.of.the.Ring.2001.EXTENDED.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Lord of the Rings The Fellowship of the Ring", result.Title);
        Assert.Equal(2001, result.Year);
        Assert.Equal("Extended Edition", result.Edition);
    }

    [Fact]
    public void Parse_Movie_FinalCut_ExtractsEdition()
    {
        var result = _parser.Parse("Apocalypse.Now.1979.FINAL.CUT.mkv");

        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Apocalypse Now", result.Title);
        Assert.Equal(1979, result.Year);
        Assert.Equal("Final Cut", result.Edition);
    }

    [Fact]
    public void CleanTitle_PreservesCompoundWordsWithHyphen()
    {
        var cleaned = FilenameParserEngine.CleanTitle("Spider-Man.No.Way.Home.1080p");
        Assert.Equal("Spider-Man No Way Home", cleaned);
    }

    [Fact]
    public void Parse_Movie_CompoundTitleWithYear_ExtractsTitleAndYear()
    {
        var result = _parser.Parse("Spider-Man.No.Way.Home.2021.1080p.mkv");
        Assert.Equal(MediaClassification.Movie, result.Classification);
        Assert.Equal("Spider-Man No Way Home", result.Title);
        Assert.Equal(2021, result.Year);
    }

    [Fact]
    public void CleanTitle_RemovesSeparatorsAndCollapsesSpaces()
    {
        var cleaned = FilenameParserEngine.CleanTitle("A...B___C - D  E");
        Assert.Equal("A B C D E", cleaned);
    }

    [Fact]
    public void CustomPatterns_FromConfigurationFile_ArePrioritized()
    {
        // Create custom naming_patterns.json in temp config directory
        var configPath = _storagePathService.NamingPatternsFilePath;
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);

        var configJson = @"
        {
            ""moviePatterns"": [
                {
                    ""name"": ""Custom Anime Movie"",
                    ""pattern"": ""^\\[(?<group>[^\\]]+)\\]\\s*(?<title>.+?)\\s*-(?<edition>.*?)\\s*\\[(?<year>\\d{4})\\]""
                }
            ],
            ""tvPatterns"": [
                {
                    ""name"": ""Custom Anime TV"",
                    ""pattern"": ""^\\[(?<group>[^\\]]+)\\]\\s*(?<series>.+?)\\s*-\\s*(?<episode>\\d{1,3})\\s*\\[""
                }
            ]
        }";
        File.WriteAllText(configPath, configJson);

        var configuredParser = new FilenameParserEngine(NullLogger<FilenameParserEngine>.Instance, _storagePathService);

        // Act 1: Custom Anime TV
        var tvResult = configuredParser.Parse("[Erai-raws] Solo Leveling - 05 [1080p].mkv");
        Assert.Equal(MediaClassification.TvEpisode, tvResult.Classification);
        Assert.Equal("Solo Leveling", tvResult.Title);
        Assert.Equal(5, tvResult.EpisodeNumber);
        Assert.Equal(ParseConfidence.High, tvResult.Confidence);

        // Act 2: Custom Anime Movie
        var movieResult = configuredParser.Parse("[Fansub] Akira - Special [1988] [1080p].mkv");
        Assert.Equal(MediaClassification.Movie, movieResult.Classification);
        Assert.Equal("Akira", movieResult.Title);
        Assert.Equal(1988, movieResult.Year);
        Assert.Equal("Special", movieResult.Edition);
    }

    #endregion

    #region Category 14: Phase 6 TV Episode Recognition Tests

    [Fact]
    public void Parse_TV_TheWire_PrimaryExample_ExtractsAllRequiredMetadata()
    {
        // Primary Task 2 specification example
        var result = _parser.Parse("The.Wire.S01E08.Lessons.REMASTERED.720p.WEB-DL.2CH.x265.HEVC.PSA.AvaMovie.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Equal("Lessons", result.EpisodeTitle);
        Assert.Contains("720p", result.DetectedTechnicalTags);
        Assert.Contains("web-dl", result.DetectedTechnicalTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("x265", result.DetectedTechnicalTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("hevc", result.DetectedTechnicalTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("2ch", result.DetectedTechnicalTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("remastered", result.DetectedTechnicalTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("psa", result.DetectedTechnicalTags, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("avamovie", result.DetectedTechnicalTags, StringComparer.OrdinalIgnoreCase);
        Assert.Equal("Remastered", result.Edition);
        Assert.Equal(ParseConfidence.High, result.Confidence);
    }

    [Fact]
    public void Parse_TV_TheWire_Lessons720p_ExtractsEpisodeTitle()
    {
        var result = _parser.Parse("The.Wire.S01E08.Lessons.720p.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Equal("Lessons", result.EpisodeTitle);
        Assert.Contains("720p", result.DetectedTechnicalTags);
    }

    [Fact]
    public void Parse_TV_TheWire_S1E8_ShortNotation_ExtractsSeasonAndEpisode()
    {
        var result = _parser.Parse("The.Wire.S1E8.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Null(result.EpisodeTitle);
    }

    [Fact]
    public void Parse_TV_TheWire_HyphenDelimitedWithTitle_ExtractsCleanly()
    {
        var result = _parser.Parse("The Wire - S01E08 - Lessons.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Equal("Lessons", result.EpisodeTitle);
    }

    [Fact]
    public void Parse_TV_TheWire_NumberXNotation_ExtractsTitleAndEpisode()
    {
        var result = _parser.Parse("The.Wire.1x08.Lessons.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Equal("Lessons", result.EpisodeTitle);
    }

    [Fact]
    public void Parse_TV_TheWire_VerbalSeasonEpisode_ExtractsCorrectly()
    {
        var result = _parser.Parse("The Wire Season 1 Episode 8.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
    }

    [Fact]
    public void Parse_TV_TheWire_MultiEpisode_E08E09_ExtractsBothEpisodes()
    {
        var result = _parser.Parse("The.Wire.S01E08E09.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Equal(9, result.EndEpisodeNumber);
        Assert.True(result.IsMultiEpisode);
        Assert.Equal(new[] { 8, 9 }, result.EpisodeNumbers);
    }

    [Fact]
    public void Parse_TV_TheWire_MultiEpisode_E08_Hyphen_E09()
    {
        var result = _parser.Parse("The.Wire.S01E08-E09.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Equal(9, result.EndEpisodeNumber);
        Assert.True(result.IsMultiEpisode);
        Assert.Equal(new[] { 8, 9 }, result.EpisodeNumbers);
    }

    [Fact]
    public void Parse_TV_TheWire_MultiEpisode_E08_Hyphen_09()
    {
        var result = _parser.Parse("The.Wire.S01E08-09.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Equal(9, result.EndEpisodeNumber);
        Assert.True(result.IsMultiEpisode);
        Assert.Equal(new[] { 8, 9 }, result.EpisodeNumbers);
    }

    [Fact]
    public void Parse_TV_TheWire_MultiEpisode_E08_Hyphen_E10_ThreeEpisodes()
    {
        var result = _parser.Parse("The.Wire.S01E08-E10.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Equal(10, result.EndEpisodeNumber);
        Assert.True(result.IsMultiEpisode);
        Assert.Equal(new[] { 8, 9, 10 }, result.EpisodeNumbers);
    }

    [Fact]
    public void Parse_TV_TheWire_Specials_S00E01()
    {
        var result = _parser.Parse("The.Wire.S00E01.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(0, result.SeasonNumber);
        Assert.Equal(1, result.EpisodeNumber);
        Assert.False(result.IsMultiEpisode);
    }

    [Fact]
    public void Parse_TV_TheWire_SeasonPack_S01()
    {
        var result = _parser.Parse("The.Wire.S01.mkv");

        Assert.Equal(MediaClassification.TvSeason, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Null(result.EpisodeNumber);
    }

    [Fact]
    public void Parse_TV_NoEpisodeTitle_DoesNotFabricateTitleFromTechnicalTags()
    {
        var result = _parser.Parse("The.Wire.S01E08.720p.WEB-DL.HEVC.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Null(result.EpisodeTitle); // Crucial: must NOT be "720p" or "WEB-DL"
    }

    [Fact]
    public void Parse_TV_ProperRepack_DoesNotFabricateTitleFromSceneTags()
    {
        var result = _parser.Parse("The.Wire.S01E08.PROPER.720p.HDTV.x264.mkv");

        Assert.Equal(MediaClassification.TvEpisode, result.Classification);
        Assert.Equal("The Wire", result.Title);
        Assert.Equal(1, result.SeasonNumber);
        Assert.Equal(8, result.EpisodeNumber);
        Assert.Null(result.EpisodeTitle); // Crucial: must NOT be "PROPER"
    }

    #endregion
}
