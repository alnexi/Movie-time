using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Extensions;
using MovieTime.Core.Scanning;
using MovieTime.Core.Scanning.Models;
using Xunit;

namespace MovieTime.Tests;

public sealed class MediaScannerTests : IDisposable
{
    private readonly string _testRoot;
    private readonly MediaScannerService _scanner;

    public MediaScannerTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "MovieTime_ScannerTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testRoot);
        _scanner = new MediaScannerService(NullLogger<MediaScannerService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, recursive: true);
            }
        }
        catch
        {
            // Best effort cleanup in temp directory
        }
    }

    [Fact]
    public async Task ScanAsync_SingleRootDirectory_DiscoversNestedVideoFilesRecursively()
    {
        // Arrange: nested directory structure
        var subDir1 = Path.Combine(_testRoot, "Action");
        var subDir2 = Path.Combine(subDir1, "SciFi");
        Directory.CreateDirectory(subDir2);

        var file1 = Path.Combine(_testRoot, "Matrix.mp4");
        var file2 = Path.Combine(subDir1, "Inception.mkv");
        var file3 = Path.Combine(subDir2, "Interstellar.avi");

        await File.WriteAllTextAsync(file1, "dummy content 1");
        await File.WriteAllTextAsync(file2, "dummy content 2 longer");
        await File.WriteAllTextAsync(file3, "dummy content 3");

        var options = new MediaScanOptions(_testRoot);

        // Act
        var result = await _scanner.ScanAsync(options);

        // Assert
        Assert.False(result.WasCancelled);
        Assert.Equal(3, result.TotalFilesFound);
        Assert.True(result.DirectoriesScanned >= 3);
        Assert.Empty(result.Errors);
        Assert.True(result.TotalBytes > 0);

        var fileNames = result.ScannedFiles.Select(f => f.FileName).ToList();
        Assert.Contains("Matrix.mp4", fileNames);
        Assert.Contains("Inception.mkv", fileNames);
        Assert.Contains("Interstellar.avi", fileNames);

        var extensions = result.ScannedFiles.Select(f => f.FileExtension).ToList();
        Assert.Contains(".mp4", extensions);
        Assert.Contains(".mkv", extensions);
        Assert.Contains(".avi", extensions);
    }

    [Fact]
    public async Task ScanAsync_MultipleRootDirectories_ScansAllConfiguredRoots()
    {
        // Arrange
        var rootA = Path.Combine(_testRoot, "LibraryA");
        var rootB = Path.Combine(_testRoot, "LibraryB");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        var fileA = Path.Combine(rootA, "MovieA.mkv");
        var fileB = Path.Combine(rootB, "MovieB.mp4");
        await File.WriteAllTextAsync(fileA, "content A");
        await File.WriteAllTextAsync(fileB, "content B");

        var options = new MediaScanOptions(rootA, rootB);

        // Act
        var result = await _scanner.ScanAsync(options);

        // Assert
        Assert.Equal(2, result.TotalFilesFound);
        Assert.True(result.DirectoriesScanned >= 2);
        Assert.Contains(result.ScannedFiles, f => f.FileName == "MovieA.mkv");
        Assert.Contains(result.ScannedFiles, f => f.FileName == "MovieB.mp4");
    }

    [Fact]
    public async Task ScanAsync_FiltersSupportedAndUnsupportedExtensions()
    {
        // Arrange
        var validMkv = Path.Combine(_testRoot, "film.mkv");
        var validMp4 = Path.Combine(_testRoot, "film.mp4");
        var subSrt = Path.Combine(_testRoot, "film.srt");
        var infoNfo = Path.Combine(_testRoot, "film.nfo");
        var txtFile = Path.Combine(_testRoot, "readme.txt");
        var imgJpg = Path.Combine(_testRoot, "poster.jpg");
        var audioMp3 = Path.Combine(_testRoot, "ost.mp3");

        await File.WriteAllTextAsync(validMkv, "video");
        await File.WriteAllTextAsync(validMp4, "video");
        await File.WriteAllTextAsync(subSrt, "subtitles");
        await File.WriteAllTextAsync(infoNfo, "nfo");
        await File.WriteAllTextAsync(txtFile, "text");
        await File.WriteAllTextAsync(imgJpg, "image");
        await File.WriteAllTextAsync(audioMp3, "audio");

        var options = new MediaScanOptions(_testRoot);

        // Act
        var result = await _scanner.ScanAsync(options);

        // Assert
        Assert.Equal(2, result.TotalFilesFound);
        var foundNames = result.ScannedFiles.Select(f => f.FileName).ToHashSet();
        Assert.Contains("film.mkv", foundNames);
        Assert.Contains("film.mp4", foundNames);
        Assert.DoesNotContain("film.srt", foundNames);
        Assert.DoesNotContain("film.nfo", foundNames);
        Assert.DoesNotContain("readme.txt", foundNames);
        Assert.DoesNotContain("poster.jpg", foundNames);
        Assert.DoesNotContain("ost.mp3", foundNames);
    }

    [Fact]
    public async Task ScanAsync_DetectsAll13SupportedExtensions()
    {
        // The 13 required extensions:
        // .mkv, .mp4, .avi, .mov, .wmv, .m4v, .mpg, .mpeg, .ts, .m2ts, .webm, .vob, .flv
        var requiredExtensions = new[]
        {
            ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".m4v",
            ".mpg", ".mpeg", ".ts", ".m2ts", ".webm", ".vob", ".flv"
        };

        foreach (var ext in requiredExtensions)
        {
            var filePath = Path.Combine(_testRoot, $"sample_video{ext}");
            await File.WriteAllTextAsync(filePath, $"video content for {ext}");
        }

        var options = new MediaScanOptions(_testRoot);

        // Act
        var result = await _scanner.ScanAsync(options);

        // Assert
        Assert.Equal(13, result.TotalFilesFound);
        var foundExtensions = result.ScannedFiles.Select(f => f.FileExtension).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var ext in requiredExtensions)
        {
            Assert.Contains(ext, foundExtensions);
        }
    }

    [Fact]
    public async Task ScanAsync_MatchesExtensionsCaseInsensitively()
    {
        // Arrange: files with mixed-case and uppercase extensions
        var upperMkv = Path.Combine(_testRoot, "clip1.MKV");
        var mixedMp4 = Path.Combine(_testRoot, "clip2.Mp4");
        var mixedAvi = Path.Combine(_testRoot, "clip3.AvI");
        var upperWebm = Path.Combine(_testRoot, "clip4.WEBM");

        await File.WriteAllTextAsync(upperMkv, "data");
        await File.WriteAllTextAsync(mixedMp4, "data");
        await File.WriteAllTextAsync(mixedAvi, "data");
        await File.WriteAllTextAsync(upperWebm, "data");

        var options = new MediaScanOptions(_testRoot);

        // Act
        var result = await _scanner.ScanAsync(options);

        // Assert
        Assert.Equal(4, result.TotalFilesFound);
        // Ensure FileExtension property is normalized to lowercase
        foreach (var file in result.ScannedFiles)
        {
            Assert.Equal(file.FileExtension.ToLowerInvariant(), file.FileExtension);
        }
    }

    [Fact]
    public async Task ScanAsync_EmptyDirectory_ReturnsZeroFiles()
    {
        var emptyDir = Path.Combine(_testRoot, "EmptyDir");
        Directory.CreateDirectory(emptyDir);

        var options = new MediaScanOptions(emptyDir);

        var result = await _scanner.ScanAsync(options);

        Assert.Equal(0, result.TotalFilesFound);
        Assert.Equal(1, result.DirectoriesScanned);
        Assert.Empty(result.Errors);
        Assert.False(result.WasCancelled);
    }

    [Fact]
    public async Task ScanAsync_MissingDirectory_RecordsErrorAndContinues()
    {
        var nonExistent = Path.Combine(_testRoot, "DoesNotActuallyExist_" + Guid.NewGuid().ToString("N"));
        var options = new MediaScanOptions(nonExistent);

        var result = await _scanner.ScanAsync(options);

        Assert.Equal(0, result.TotalFilesFound);
        Assert.Equal(1, result.SkippedDirectoriesCount);
        Assert.Single(result.Errors);
        Assert.Contains("Directory does not exist", result.Errors[0].Message);
    }

    [Fact]
    public async Task ScanAsync_WithValidAndInvalidRoots_ProcessesValidAndReportsInvalid()
    {
        // Valid directory with file
        var validDir = Path.Combine(_testRoot, "ValidRoot");
        Directory.CreateDirectory(validDir);
        await File.WriteAllTextAsync(Path.Combine(validDir, "valid.mkv"), "content");

        // Invalid directory
        var invalidDir = Path.Combine(_testRoot, "NonExistent_" + Guid.NewGuid().ToString("N"));

        var options = new MediaScanOptions(validDir, invalidDir);

        var result = await _scanner.ScanAsync(options);

        Assert.Equal(1, result.TotalFilesFound);
        Assert.Equal("valid.mkv", result.ScannedFiles[0].FileName);
        Assert.Equal(1, result.SkippedDirectoriesCount);
        Assert.Single(result.Errors);
    }

    [Fact]
    public async Task ScanAsync_CancellationPrompt_CapturesCancellationGracefully()
    {
        // Create 20 nested subdirectories with files
        for (int i = 0; i < 20; i++)
        {
            var dir = Path.Combine(_testRoot, $"Folder_{i}");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, $"movie_{i}.mp4"), "content");
        }

        using var cts = new CancellationTokenSource();
        // Cancel token immediately
        cts.Cancel();

        var options = new MediaScanOptions(_testRoot) { ThrowOnCancellation = false };

        var result = await _scanner.ScanAsync(options, cancellationToken: cts.Token);

        Assert.True(result.WasCancelled);
    }

    [Fact]
    public async Task ScanAsync_CancellationWithThrowOnCancellation_ThrowsOperationCanceledException()
    {
        for (int i = 0; i < 10; i++)
        {
            var dir = Path.Combine(_testRoot, $"Folder_{i}");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, $"movie_{i}.mp4"), "content");
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new MediaScanOptions(_testRoot) { ThrowOnCancellation = true };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _scanner.ScanAsync(options, cancellationToken: cts.Token));
    }

    [Fact]
    public async Task ScanAsync_ProgressReporting_ReportsIntermediateAndCompletionProgress()
    {
        for (int i = 0; i < 5; i++)
        {
            var dir = Path.Combine(_testRoot, $"ProgFolder_{i}");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, $"clip_{i}.mkv"), "video data");
        }

        var progressReports = new List<MediaScanProgress>();
        var progress = new SynchronousProgress<MediaScanProgress>(p =>
        {
            lock (progressReports)
            {
                progressReports.Add(p);
            }
        });

        var options = new MediaScanOptions(_testRoot) { ProgressFileInterval = 1 };

        var result = await _scanner.ScanAsync(options, progress: progress);

        Assert.Equal(5, result.TotalFilesFound);

        Assert.NotEmpty(progressReports);
        var lastReport = progressReports.Last();
        Assert.True(lastReport.IsComplete);
        Assert.Equal(5, lastReport.FilesFound);
    }

    private sealed class SynchronousProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    [Fact]
    public async Task EnumerateMediaFilesAsync_StreamsFilesAsynchronously()
    {
        var dirA = Path.Combine(_testRoot, "StreamA");
        var dirB = Path.Combine(_testRoot, "StreamB");
        Directory.CreateDirectory(dirA);
        Directory.CreateDirectory(dirB);

        await File.WriteAllTextAsync(Path.Combine(dirA, "one.mp4"), "1");
        await File.WriteAllTextAsync(Path.Combine(dirB, "two.mkv"), "2");

        var options = new MediaScanOptions(dirA, dirB);
        var collected = new List<ScannedMediaFile>();

        await foreach (var file in _scanner.EnumerateMediaFilesAsync(options))
        {
            collected.Add(file);
        }

        Assert.Equal(2, collected.Count);
        Assert.Contains(collected, f => f.FileName == "one.mp4");
        Assert.Contains(collected, f => f.FileName == "two.mkv");
    }

    [Fact]
    public async Task ScanAsync_ReparsePointProtection_SkipsJunctionDirectory()
    {
        // 1. Create a real target directory with a file
        var targetDir = Path.Combine(_testRoot, "TargetFolder");
        Directory.CreateDirectory(targetDir);
        await File.WriteAllTextAsync(Path.Combine(targetDir, "target_file.mkv"), "content");

        // 2. Create a junction linking to the target directory
        var junctionPath = Path.Combine(_testRoot, "JunctionFolder");

        var startInfo = new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{junctionPath}\" \"{targetDir}\"")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };

        var process = Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }

        // Only assert if junction was successfully created
        if (Directory.Exists(junctionPath))
        {
            var isReparsePoint = (new DirectoryInfo(junctionPath).Attributes & FileAttributes.ReparsePoint) != 0;
            if (isReparsePoint)
            {
                var options = new MediaScanOptions(_testRoot) { SkipReparsePoints = true };
                var result = await _scanner.ScanAsync(options);

                // File should be found only once from TargetFolder, and JunctionFolder should be skipped
                Assert.Equal(1, result.TotalFilesFound);
                Assert.True(result.SkippedDirectoriesCount >= 1);
            }
        }
    }

    [Fact]
    public async Task ScanAsync_LargeDirectoryTree_EnumeratesEfficiently()
    {
        // Create 50 directories with 2 files each (100 files total)
        for (int i = 0; i < 50; i++)
        {
            var dir = Path.Combine(_testRoot, $"Tree_{i}");
            Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(Path.Combine(dir, $"file_{i}_1.mkv"), "content");
            await File.WriteAllTextAsync(Path.Combine(dir, $"file_{i}_2.mp4"), "content");
        }

        var options = new MediaScanOptions(_testRoot);

        var stopwatch = Stopwatch.StartNew();
        var result = await _scanner.ScanAsync(options);
        stopwatch.Stop();

        Assert.Equal(100, result.TotalFilesFound);
        Assert.True(result.DirectoriesScanned >= 50);
        Assert.True(stopwatch.ElapsedMilliseconds < 5000, "Scanning 100 local files should complete in under 5 seconds");
    }

    [Fact]
    public void DependencyInjection_ResolvesMediaScannerServiceAsSingleton()
    {
        var services = new ServiceCollection();
        services.AddMovieTimeCore();
        var provider = services.BuildServiceProvider();

        var scanner1 = provider.GetService<IMediaScannerService>();
        var scanner2 = provider.GetService<IMediaScannerService>();

        Assert.NotNull(scanner1);
        Assert.IsType<MediaScannerService>(scanner1);
        Assert.Same(scanner1, scanner2);
    }
}
