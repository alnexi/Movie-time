using System;
using System.Collections.Generic;

namespace MovieTime.Core.Scanning.Models;

/// <summary>
/// Configuration options controlling recursive media scanning.
/// </summary>
public sealed class MediaScanOptions
{
    /// <summary>
    /// Default set of supported video file extensions.
    /// .mkv, .mp4, .avi, .mov, .wmv, .m4v, .mpg, .mpeg, .ts, .m2ts, .mts, .m2t, .webm, .vob, .flv, .divx, .3gp, .ogv, .asf, .rmvb, .rm
    /// </summary>
    public static readonly IReadOnlySet<string> DefaultSupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv",
        ".mp4",
        ".avi",
        ".mov",
        ".wmv",
        ".m4v",
        ".mpg",
        ".mpeg",
        ".ts",
        ".m2ts",
        ".mts",
        ".m2t",
        ".webm",
        ".vob",
        ".flv",
        ".divx",
        ".3gp",
        ".ogv",
        ".asf",
        ".rmvb",
        ".rm"
    };

    /// <summary>
    /// The list of root directories to scan recursively.
    /// </summary>
    public IReadOnlyList<string> RootDirectories { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Supported file extensions (with leading period, case-insensitive).
    /// Defaults to <see cref="DefaultSupportedExtensions"/>.
    /// </summary>
    public IReadOnlySet<string> SupportedExtensions { get; set; } = DefaultSupportedExtensions;

    /// <summary>
    /// Whether to skip symbolic links and directory reparse points to avoid recursive loops.
    /// Default is true.
    /// </summary>
    public bool SkipReparsePoints { get; set; } = true;

    /// <summary>
    /// Whether to skip hidden directories (e.g. .git, $RECYCLE.BIN).
    /// Default is true.
    /// </summary>
    public bool SkipHiddenDirectories { get; set; } = true;

    /// <summary>
    /// Whether an <see cref="OperationCanceledException"/> should be thrown on cancellation in ScanAsync.
    /// If false (default), the scan terminates promptly and returns a <see cref="MediaScanResult"/> with <see cref="MediaScanResult.WasCancelled"/> set to true.
    /// </summary>
    public bool ThrowOnCancellation { get; set; } = false;

    /// <summary>
    /// Progress notification interval in files discovered. Default is every 25 files.
    /// </summary>
    public int ProgressFileInterval { get; set; } = 25;

    public MediaScanOptions() { }

    public MediaScanOptions(params string[] rootDirectories)
    {
        RootDirectories = rootDirectories;
    }

    public MediaScanOptions(IEnumerable<string> rootDirectories)
    {
        RootDirectories = new List<string>(rootDirectories);
    }
}
