using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents a user-configured local filesystem directory root that is scanned for media.
/// </summary>
[Table("ScanRoots")]
public class ScanRoot
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(2000), Indexed(Unique = true)]
    public string Path { get; set; } = string.Empty;

    [NotNull, MaxLength(2000), Indexed]
    public string NormalizedPath { get; set; } = string.Empty;

    [NotNull, Indexed]
    public bool IsEnabled { get; set; } = true;

    [NotNull]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastScannedAt { get; set; }
}
