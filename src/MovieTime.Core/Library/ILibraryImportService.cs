using System;
using System.Threading;
using System.Threading.Tasks;
using MovieTime.Core.Library.Models;

namespace MovieTime.Core.Library;

/// <summary>
/// High-level orchestration service coordinating media folder scanning, filename parsing,
/// database relationship management, missing-file reconciliation, and progress reporting.
/// </summary>
public interface ILibraryImportService
{
    /// <summary>
    /// Indicates whether a library import operation is currently executing.
    /// </summary>
    bool IsImporting { get; }

    /// <summary>
    /// Executes a library import and synchronization run across configured scan roots.
    /// </summary>
    Task<LibraryImportResult> ImportAsync(
        LibraryImportOptions? options = null,
        IProgress<LibraryImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
