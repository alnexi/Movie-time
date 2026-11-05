using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents the user's current playback position and completion status for a media item.
/// </summary>
[Table("PlaybackProgress")]
public class PlaybackProgress
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, Indexed(Unique = true)]
    public int MediaFileId { get; set; }

    [Indexed]
    public int? MovieId { get; set; }

    [Indexed]
    public int? EpisodeId { get; set; }

    [NotNull]
    public long PositionTicks { get; set; }

    [NotNull]
    public long DurationTicks { get; set; }

    [NotNull]
    public double PositionPercentage { get; set; }

    [NotNull, Indexed]
    public bool IsFinished { get; set; }

    [NotNull, Indexed]
    public DateTime LastWatchedAt { get; set; } = DateTime.UtcNow;

    [NotNull]
    public int PlayCount { get; set; } = 1;
}
