using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents a local user playlist for organizing movies.
/// </summary>
[Table("Playlists")]
public class Playlist
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed, NotNull]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
