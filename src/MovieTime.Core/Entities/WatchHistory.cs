using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents a historical viewing session log entry.
/// </summary>
[Table("WatchHistory")]
public class WatchHistory
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [NotNull, Indexed]
    public int MediaFileId { get; set; }

    [Indexed]
    public int? MovieId { get; set; }

    [Indexed]
    public int? EpisodeId { get; set; }

    [NotNull, Indexed]
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;

    public DateTime? CompletedAt { get; set; }

    [NotNull]
    public long WatchedDurationTicks { get; set; }

    [NotNull]
    public long StoppedPositionTicks { get; set; }

    [NotNull]
    public bool WasCompleted { get; set; }
}
