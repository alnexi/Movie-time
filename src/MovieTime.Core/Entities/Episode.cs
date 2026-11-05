using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents a single episode belonging to a <see cref="Season"/> and <see cref="Series"/>.
/// </summary>
[Table("Episodes")]
public class Episode
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, Indexed]
    public int SeriesId { get; set; }

    [NotNull, Indexed]
    public int SeasonId { get; set; }

    [NotNull, Indexed]
    public int EpisodeNumber { get; set; }

    [NotNull, MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? ThumbnailPath { get; set; }

    public bool IsThumbnailFallback { get; set; }

    [NotNull]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [NotNull]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
