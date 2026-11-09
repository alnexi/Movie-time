using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents an ordered entry in a <see cref="Playlist"/>.
/// </summary>
[Table("PlaylistItems")]
public class PlaylistItem
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed, NotNull]
    public int PlaylistId { get; set; }

    [Indexed, NotNull]
    public int MovieId { get; set; }

    public int SortOrder { get; set; }

    public DateTime AddedAtUtc { get; set; } = DateTime.UtcNow;
}
