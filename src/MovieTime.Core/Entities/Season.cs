using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents a specific season belonging to a <see cref="Series"/>.
/// </summary>
[Table("Seasons")]
public class Season
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, Indexed]
    public int SeriesId { get; set; }

    [NotNull, Indexed]
    public int SeasonNumber { get; set; }

    [MaxLength(500)]
    public string? Title { get; set; }

    [MaxLength(1000)]
    public string? PosterPath { get; set; }

    public bool IsPosterFallback { get; set; }

    [NotNull]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotNull]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
