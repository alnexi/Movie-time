using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MovieTime.Core.Scanning.Models;

namespace MovieTime.Core.Scanning;

/// <summary>
/// Scans local filesystem directories recursively for supported media files without internet access.
/// </summary>
public interface IMediaScannerService
{
    /// <summary>
    /// Scans the configured root directories recursively and returns an aggregated <see cref="MediaScanResult"/>.
    /// </summary>
    /// <param name="options">The scan configuration specifying root paths and matching options.</param>
    /// <param name="progress">Optional progress reporter for UI/background updates.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A structured scan summary containing all discovered media files and metrics.</returns>
    Task<MediaScanResult> ScanAsync(
        MediaScanOptions options,
        IProgress<MediaScanProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously streams discovered media files one-by-one as they are discovered, minimizing memory overhead.
    /// </summary>
    /// <param name="options">The scan configuration specifying root paths and matching options.</param>
    /// <param name="progress">Optional progress reporter.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>An asynchronous stream of discovered <see cref="ScannedMediaFile"/> items.</returns>
    IAsyncEnumerable<ScannedMediaFile> EnumerateMediaFilesAsync(
        MediaScanOptions options,
        IProgress<MediaScanProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
