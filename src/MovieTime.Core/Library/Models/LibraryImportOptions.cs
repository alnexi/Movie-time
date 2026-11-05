using System;
using System.Collections.Generic;

namespace MovieTime.Core.Library.Models;

/// <summary>
/// Options controlling the execution of a library import and synchronization run.
/// </summary>
public sealed class LibraryImportOptions
{
    /// <summary>
    /// Optional specific scan root IDs to import. If null or empty, all enabled roots are imported.
    /// </summary>
    public IReadOnlyList<int>? ScanRootIds { get; set; }

    /// <summary>
    /// Whether to force re-parsing and re-evaluating metadata even if file size and modification timestamps are identical.
    /// Default is false.
    /// </summary>
    public bool ForceReparse { get; set; } = false;

    /// <summary>
    /// Batch size for grouped database processing.
    /// </summary>
    public int BatchSize { get; set; } = 50;
}
