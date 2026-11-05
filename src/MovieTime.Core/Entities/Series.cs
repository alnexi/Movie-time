using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents a television series or serialized show in the local library.
/// </summary>
[Table("Series")]
public class Series
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, MaxLength(500), Indexed]
    public string Title { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? SortTitle { get; set; }

    public int? StartYear { get; set; }

    public int? EndYear { get; set; }

    [MaxLength(1000)]
    public string? PosterPath { get; set; }

    [MaxLength(1000)]
    public string? BackdropPath { get; set; }

    public bool IsPosterFallback { get; set; }

    public bool IsBackdropFallback { get; set; }

    [NotNull]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotNull]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
