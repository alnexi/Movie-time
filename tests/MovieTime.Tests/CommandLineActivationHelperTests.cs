using System;
using System.IO;
using System.Linq;
using MovieTime.Core;
using MovieTime.Core.Shell;
using Xunit;

namespace MovieTime.Tests;

public class CommandLineActivationHelperTests
{
    [Fact]
    public void SplitCommandLine_SingleUnquotedArgument_ReturnsSingleItem()
    {
        var input = "D:\\Videos\\Movie.mkv";
        var result = CommandLineHelper.SplitCommandLine(input);

        Assert.Single(result);
        Assert.Equal("D:\\Videos\\Movie.mkv", result[0]);
    }

    [Fact]
    public void SplitCommandLine_QuotedPathWithSpaces_PreservesSpacesAndRemovesQuotes()
    {
        var input = "\"D:\\My Movies\\The Dark Knight (2008)\\The Dark Knight.mkv\"";
        var result = CommandLineHelper.SplitCommandLine(input);

        Assert.Single(result);
        Assert.Equal("D:\\My Movies\\The Dark Knight (2008)\\The Dark Knight.mkv", result[0]);
    }

    [Fact]
    public void SplitCommandLine_MultipleQuotedPaths_SplitsCorrectly()
    {
        var input = "\"D:\\Videos\\Part 1.mp4\" \"D:\\Videos\\Part 2.mp4\" \"D:\\Videos\\Part 3.mkv\"";
        var result = CommandLineHelper.SplitCommandLine(input);

        Assert.Equal(3, result.Length);
        Assert.Equal("D:\\Videos\\Part 1.mp4", result[0]);
        Assert.Equal("D:\\Videos\\Part 2.mp4", result[1]);
        Assert.Equal("D:\\Videos\\Part 3.mkv", result[2]);
    }

    [Fact]
    public void SplitCommandLine_UnicodeCharacters_PreservesCharactersAccurately()
    {
        var input = "\"D:\\فیلم\\فروشنده (2016).mkv\"";
        var result = CommandLineHelper.SplitCommandLine(input);

        Assert.Single(result);
        Assert.Equal("D:\\فیلم\\فروشنده (2016).mkv", result[0]);
    }

    [Fact]
    public void SplitCommandLine_NullOrEmpty_ReturnsEmptyArray()
    {
        Assert.Empty(CommandLineHelper.SplitCommandLine(null));
        Assert.Empty(CommandLineHelper.SplitCommandLine("   "));
    }

    [Fact]
    public void ManagedSplitCommandLine_MatchesExpectedTokenization()
    {
        var input = "\"C:\\Path With Space\\file.mp4\" normal.mkv \"another path.avi\"";
        var result = CommandLineHelper.ManagedSplitCommandLine(input);

        Assert.Equal(3, result.Length);
        Assert.Equal("C:\\Path With Space\\file.mp4", result[0]);
        Assert.Equal("normal.mkv", result[1]);
        Assert.Equal("another path.avi", result[2]);
    }

    [Fact]
    public void ExtractVideoFiles_FiltersOutUnsupportedFileExtensionsAndNonExistentFiles()
    {
        // Create a temporary valid video file on disk
        var tempDir = Path.Combine(Path.GetTempPath(), "RiverTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var validVideo = Path.Combine(tempDir, "sample_clip.mp4");
            File.WriteAllText(validVideo, "dummy content");

            var invalidDoc = Path.Combine(tempDir, "notes.txt");
            File.WriteAllText(invalidDoc, "text content");

            var nonExistent = Path.Combine(tempDir, "ghost.mkv");

            // Build mock command line containing app exe + the 3 paths
            var cmdLine = $"\"River.exe\" \"{validVideo}\" \"{invalidDoc}\" \"{nonExistent}\"";
            var extracted = CommandLineHelper.ExtractVideoFiles(cmdLine, isFullCommandLineWithExecutable: true);

            Assert.Single(extracted);
            Assert.Equal(Path.GetFullPath(validVideo), extracted[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, true);
            }
        }
    }
}
