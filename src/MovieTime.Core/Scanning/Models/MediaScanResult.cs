using System;
using System.Collections.Generic;

namespace MovieTime.Core.Scanning.Models;

/// <summary>
/// Aggregated summary result of a completed or cancelled media scan.
/// </summary>
public sealed class MediaScanResult
{
    /// <summary>
    /// All media files discovered during the scan.
    /// </summary>
    public IReadOnlyList<ScannedMediaFile> ScannedFiles { get; }

    /// <summary>
    /// Total count of media files discovered.
    /// </summary>
    public int TotalFilesFound => ScannedFiles.Count;

    /// <summary>
    /// Cumulative size in bytes of all discovered media files.
    /// </summary>
    public long TotalBytes { get; }

    /// <summary>
    /// Total count of directories scanned.
    /// </summary>
    public int DirectoriesScanned { get; }

    /// <summary>
    /// Number of inaccessible or unreadable directories skipped.
    /// </summary>
    public int SkippedDirectoriesCount { get; }

    /// <summary>
    /// Number of unreadable or inaccessible files skipped.
    /// </summary>
    public int SkippedFilesCount { get; }

    /// <summary>
    /// Total time taken for the scan operation.
    /// </summary>
    public TimeSpan Duration { get; }

    /// <summary>
    /// Indicates whether the scan operation was interrupted or cancelled.
    /// </summary>
    public bool WasCancelled { get; }

    /// <summary>
    /// Collection of non-fatal errors or access violations encountered during scanning.
    /// </summary>
    public IReadOnlyList<ScanError> Errors { get; }

    public MediaScanResult(
        IReadOnlyList<ScannedMediaFile> scannedFiles,
        long totalBytes,
        int directoriesScanned,
        int skippedDirectoriesCount,
        int skippedFilesCount,
        TimeSpan duration,
        bool wasCancelled,
        IReadOnlyList<ScanError> errors)
    {
        ScannedFiles = scannedFiles ?? Array.Empty<ScannedMediaFile>();
        TotalBytes = totalBytes;
        DirectoriesScanned = directoriesScanned;
        SkippedDirectoriesCount = skippedDirectoriesCount;
        SkippedFilesCount = skippedFilesCount;
        Duration = duration;
        WasCancelled = wasCancelled;
        Errors = errors ?? Array.Empty<ScanError>();
    }
}
