using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MovieTime.Core.Scanning.Models;

namespace MovieTime.Core.Scanning;

/// <summary>
/// Production recursive local media scanner. Traverses configured directories, identifies
/// supported video containers using case-insensitive matching, and guards against symlink cycles,
/// hidden folders, and filesystem permission exceptions.
/// </summary>
public sealed class MediaScannerService : IMediaScannerService
{
    private readonly ILogger<MediaScannerService> _logger;

    public MediaScannerService(ILogger<MediaScannerService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public Task<MediaScanResult> ScanAsync(
        MediaScanOptions options,
        IProgress<MediaScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        // Run scanning on a background thread to prevent any blocking of the calling thread.
        return Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var scannedFiles = new List<ScannedMediaFile>();
            var errors = new List<ScanError>();
            var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var directoriesScanned = 0;
            var skippedDirectoriesCount = 0;
            var skippedFilesCount = 0;
            long totalBytes = 0;
            var wasCancelled = false;

            _logger.LogInformation("Starting media scan for {RootCount} root directories.",
                options.RootDirectories.Count);

            try
            {
                foreach (var rootDir in options.RootDirectories)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        if (options.ThrowOnCancellation)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }
                        wasCancelled = true;
                        break;
                    }

                    if (string.IsNullOrWhiteSpace(rootDir))
                    {
                        continue;
                    }

                    string fullRootPath;
                    try
                    {
                        fullRootPath = Path.GetFullPath(rootDir);
                    }
                    catch (Exception ex) when (IsFileSystemException(ex))
                    {
                        skippedDirectoriesCount++;
                        errors.Add(new ScanError(rootDir, ex.Message, ex.GetType().Name));
                        _logger.LogWarning("Invalid root directory path '{Path}'. Reason: {Reason}", rootDir, ex.Message);
                        continue;
                    }

                    if (!Directory.Exists(fullRootPath))
                    {
                        skippedDirectoriesCount++;
                        errors.Add(new ScanError(fullRootPath, "Directory does not exist", nameof(DirectoryNotFoundException)));
                        _logger.LogWarning("Root directory does not exist: {Path}", fullRootPath);
                        continue;
                    }

                    var queue = new Queue<string>();
                    queue.Enqueue(fullRootPath);

                    while (queue.Count > 0)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            if (options.ThrowOnCancellation)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }
                            wasCancelled = true;
                            break;
                        }

                        var currentDir = queue.Dequeue();

                        // Avoid processing duplicate paths or cyclic references
                        if (!visitedDirectories.Add(currentDir))
                        {
                            continue;
                        }

                        directoriesScanned++;

                        // 1. Inspect directory attributes
                        DirectoryInfo currentDirInfo;
                        try
                        {
                            currentDirInfo = new DirectoryInfo(currentDir);
                            if (!currentDirInfo.Exists)
                            {
                                skippedDirectoriesCount++;
                                continue;
                            }

                            // Skip hidden directories unless it is the root itself
                            var isRoot = string.Equals(currentDir, fullRootPath, StringComparison.OrdinalIgnoreCase);
                            if (!isRoot && options.SkipHiddenDirectories && (currentDirInfo.Attributes & FileAttributes.Hidden) != 0)
                            {
                                continue;
                            }
                        }
                        catch (Exception ex) when (IsFileSystemException(ex))
                        {
                            skippedDirectoriesCount++;
                            errors.Add(new ScanError(currentDir, ex.Message, ex.GetType().Name));
                            _logger.LogWarning("Failed to access directory metadata: {Reason}", ex.Message);
                            continue;
                        }

                        // 2. Discover subdirectories safely
                        try
                        {
                            foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                try
                                {
                                    var subDirInfo = new DirectoryInfo(subDir);

                                    // Skip reparse points (symlinks, junctions) if configured to avoid loops
                                    if (options.SkipReparsePoints && (subDirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                                    {
                                        skippedDirectoriesCount++;
                                        _logger.LogDebug("Skipping reparse point directory: {SubDir}", subDir);
                                        continue;
                                    }

                                    // Skip hidden subdirectories if configured
                                    if (options.SkipHiddenDirectories && (subDirInfo.Attributes & FileAttributes.Hidden) != 0)
                                    {
                                        continue;
                                    }

                                    queue.Enqueue(subDir);
                                }
                                catch (Exception ex) when (IsFileSystemException(ex))
                                {
                                    skippedDirectoriesCount++;
                                    errors.Add(new ScanError(subDir, ex.Message, ex.GetType().Name));
                                    _logger.LogWarning("Skipping inaccessible subdirectory: {Reason}", ex.Message);
                                }
                            }
                        }
                        catch (Exception ex) when (IsFileSystemException(ex))
                        {
                            skippedDirectoriesCount++;
                            errors.Add(new ScanError(currentDir, ex.Message, ex.GetType().Name));
                            _logger.LogWarning("Failed to enumerate subdirectories: {Reason}", ex.Message);
                        }

                        // 3. Discover files safely
                        try
                        {
                            foreach (var filePath in Directory.EnumerateFiles(currentDir))
                            {
                                if (cancellationToken.IsCancellationRequested)
                                {
                                    break;
                                }

                                try
                                {
                                    var extension = Path.GetExtension(filePath);
                                    if (string.IsNullOrEmpty(extension))
                                    {
                                        continue;
                                    }

                                    // Check supported extension case-insensitively
                                    if (!options.SupportedExtensions.Contains(extension))
                                    {
                                        continue;
                                    }

                                    var fileInfo = new FileInfo(filePath);
                                    if (!fileInfo.Exists)
                                    {
                                        skippedFilesCount++;
                                        continue;
                                    }

                                    if (options.SkipReparsePoints && (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                                    {
                                        skippedFilesCount++;
                                        continue;
                                    }

                                    var scanned = new ScannedMediaFile(
                                        filePath: fileInfo.FullName,
                                        fileName: fileInfo.Name,
                                        fileExtension: extension.ToLowerInvariant(),
                                        sizeBytes: fileInfo.Length,
                                        lastModifiedUtc: fileInfo.LastWriteTimeUtc,
                                        createdUtc: fileInfo.CreationTimeUtc
                                    );

                                    scannedFiles.Add(scanned);
                                    totalBytes += scanned.SizeBytes;

                                    if (progress != null && scannedFiles.Count % options.ProgressFileInterval == 0)
                                    {
                                        progress.Report(new MediaScanProgress(
                                            directoriesScanned,
                                            scannedFiles.Count,
                                            totalBytes,
                                            currentDir,
                                            isComplete: false
                                        ));
                                    }
                                }
                                catch (Exception ex) when (IsFileSystemException(ex))
                                {
                                    skippedFilesCount++;
                                    errors.Add(new ScanError(filePath, ex.Message, ex.GetType().Name));
                                    _logger.LogWarning("Skipping inaccessible file: {Reason}", ex.Message);
                                }
                            }
                        }
                        catch (Exception ex) when (IsFileSystemException(ex))
                        {
                            skippedDirectoriesCount++;
                            errors.Add(new ScanError(currentDir, ex.Message, ex.GetType().Name));
                            _logger.LogWarning("Failed to enumerate files in directory: {Reason}", ex.Message);
                        }

                        // Periodic progress on directory boundaries if no files found yet
                        progress?.Report(new MediaScanProgress(
                            directoriesScanned,
                            scannedFiles.Count,
                            totalBytes,
                            currentDir,
                            isComplete: false
                        ));
                    }

                    if (wasCancelled)
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                if (options.ThrowOnCancellation)
                {
                    throw;
                }
                wasCancelled = true;
            }

            stopwatch.Stop();

            progress?.Report(new MediaScanProgress(
                directoriesScanned,
                scannedFiles.Count,
                totalBytes,
                currentDirectory: null,
                isComplete: true
            ));

            _logger.LogInformation(
                "Media scan finished. Status: {Status}, Duration: {DurationMs}ms, Files: {FilesCount}, Bytes: {TotalBytes:N0}, Directories: {DirsCount}, Skipped Dirs: {SkippedDirs}, Skipped Files: {SkippedFiles}",
                wasCancelled ? "Cancelled" : "Completed",
                stopwatch.ElapsedMilliseconds,
                scannedFiles.Count,
                totalBytes,
                directoriesScanned,
                skippedDirectoriesCount,
                skippedFilesCount);

            return new MediaScanResult(
                scannedFiles,
                totalBytes,
                directoriesScanned,
                skippedDirectoriesCount,
                skippedFilesCount,
                stopwatch.Elapsed,
                wasCancelled,
                errors
            );
        });
    }

    public async IAsyncEnumerable<ScannedMediaFile> EnumerateMediaFilesAsync(
        MediaScanOptions options,
        IProgress<MediaScanProgress>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var visitedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directoriesScanned = 0;
        var filesFound = 0;
        long totalBytes = 0;

        foreach (var rootDir in options.RootDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(rootDir))
            {
                continue;
            }

            string fullRootPath;
            try
            {
                fullRootPath = Path.GetFullPath(rootDir);
            }
            catch (Exception ex) when (IsFileSystemException(ex))
            {
                _logger.LogWarning("Invalid root directory path '{Path}'. Reason: {Reason}", rootDir, ex.Message);
                continue;
            }

            if (!Directory.Exists(fullRootPath))
            {
                _logger.LogWarning("Root directory does not exist: {Path}", fullRootPath);
                continue;
            }

            var queue = new Queue<string>();
            queue.Enqueue(fullRootPath);

            while (queue.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var currentDir = queue.Dequeue();

                if (!visitedDirectories.Add(currentDir))
                {
                    continue;
                }

                directoriesScanned++;

                DirectoryInfo currentDirInfo;
                try
                {
                    currentDirInfo = new DirectoryInfo(currentDir);
                    if (!currentDirInfo.Exists)
                    {
                        continue;
                    }

                    var isRoot = string.Equals(currentDir, fullRootPath, StringComparison.OrdinalIgnoreCase);
                    if (!isRoot && options.SkipHiddenDirectories && (currentDirInfo.Attributes & FileAttributes.Hidden) != 0)
                    {
                        continue;
                    }
                }
                catch (Exception ex) when (IsFileSystemException(ex))
                {
                    _logger.LogWarning("Failed to access directory metadata: {Reason}", ex.Message);
                    continue;
                }

                // Subdirectories
                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(currentDir))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var subDirInfo = new DirectoryInfo(subDir);
                            if (options.SkipReparsePoints && (subDirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }

                            if (options.SkipHiddenDirectories && (subDirInfo.Attributes & FileAttributes.Hidden) != 0)
                            {
                                continue;
                            }

                            queue.Enqueue(subDir);
                        }
                        catch (Exception ex) when (IsFileSystemException(ex))
                        {
                            _logger.LogWarning("Skipping inaccessible subdirectory: {Reason}", ex.Message);
                        }
                    }
                }
                catch (Exception ex) when (IsFileSystemException(ex))
                {
                    _logger.LogWarning("Failed to enumerate subdirectories: {Reason}", ex.Message);
                }

                // Files
                List<ScannedMediaFile> dirFiles = new();
                try
                {
                    foreach (var filePath in Directory.EnumerateFiles(currentDir))
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            var extension = Path.GetExtension(filePath);
                            if (string.IsNullOrEmpty(extension) || !options.SupportedExtensions.Contains(extension))
                            {
                                continue;
                            }

                            var fileInfo = new FileInfo(filePath);
                            if (!fileInfo.Exists)
                            {
                                continue;
                            }

                            if (options.SkipReparsePoints && (fileInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                            {
                                continue;
                            }

                            var scanned = new ScannedMediaFile(
                                filePath: fileInfo.FullName,
                                fileName: fileInfo.Name,
                                fileExtension: extension.ToLowerInvariant(),
                                sizeBytes: fileInfo.Length,
                                lastModifiedUtc: fileInfo.LastWriteTimeUtc,
                                createdUtc: fileInfo.CreationTimeUtc
                            );

                            dirFiles.Add(scanned);
                        }
                        catch (Exception ex) when (IsFileSystemException(ex))
                        {
                            _logger.LogWarning("Skipping inaccessible file: {Reason}", ex.Message);
                        }
                    }
                }
                catch (Exception ex) when (IsFileSystemException(ex))
                {
                    _logger.LogWarning("Failed to enumerate files in directory: {Reason}", ex.Message);
                }

                // Yield files for this directory
                foreach (var file in dirFiles)
                {
                    filesFound++;
                    totalBytes += file.SizeBytes;
                    yield return file;
                }

                progress?.Report(new MediaScanProgress(
                    directoriesScanned,
                    filesFound,
                    totalBytes,
                    currentDir,
                    isComplete: false
                ));
            }
        }

        progress?.Report(new MediaScanProgress(
            directoriesScanned,
            filesFound,
            totalBytes,
            currentDirectory: null,
            isComplete: true
        ));
    }

    private static bool IsFileSystemException(Exception ex) =>
        ex is UnauthorizedAccessException
        || ex is SecurityException
        || ex is DirectoryNotFoundException
        || ex is FileNotFoundException
        || ex is PathTooLongException
        || ex is IOException;
}
