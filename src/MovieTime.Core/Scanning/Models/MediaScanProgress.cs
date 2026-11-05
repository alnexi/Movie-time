namespace MovieTime.Core.Scanning.Models;

/// <summary>
/// Snapshot of progress during an ongoing media library scan.
/// </summary>
public sealed class MediaScanProgress
{
    /// <summary>
    /// Number of directories scanned so far.
    /// </summary>
    public int DirectoriesScanned { get; }

    /// <summary>
    /// Number of supported media files discovered so far.
    /// </summary>
    public int FilesFound { get; }

    /// <summary>
    /// Total bytes of media files discovered so far.
    /// </summary>
    public long TotalBytesScanned { get; }

    /// <summary>
    /// The current directory being inspected, if available.
    /// </summary>
    public string? CurrentDirectory { get; }

    /// <summary>
    /// Whether the scan has completed.
    /// </summary>
    public bool IsComplete { get; }

    public MediaScanProgress(
        int directoriesScanned,
        int filesFound,
        long totalBytesScanned,
        string? currentDirectory = null,
        bool isComplete = false)
    {
        DirectoriesScanned = directoriesScanned;
        FilesFound = filesFound;
        TotalBytesScanned = totalBytesScanned;
        CurrentDirectory = currentDirectory;
        IsComplete = isComplete;
    }

    public override string ToString() =>
        $"Directories: {DirectoriesScanned}, Files: {FilesFound}, Bytes: {TotalBytesScanned:N0}";
}
