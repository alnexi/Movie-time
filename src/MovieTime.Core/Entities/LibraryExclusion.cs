using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents a persistent user exclusion of a file path from the River library.
/// Excluded files are permanently skipped during automatic synchronization and directory scans.
/// </summary>
[Table("library_exclusions")]
public sealed class LibraryExclusion
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    /// <summary>
    /// Exact normalized full file path that is excluded.
    /// </summary>
    [NotNull, Indexed, MaxLength(1000)]
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Optional relative path within the scan root for portability across drive letters.
    /// </summary>
    [MaxLength(260)]
    public string? RelativePath { get; set; }

    /// <summary>
    /// Reason for exclusion (e.g. "UserRemoved", "Excluded").
    /// </summary>
    [MaxLength(100)]
    public string? Reason { get; set; }

    /// <summary>
    /// Timestamp when the exclusion was created.
    /// </summary>
    [NotNull]
    public DateTime ExcludedAt { get; set; } = DateTime.UtcNow;
}
