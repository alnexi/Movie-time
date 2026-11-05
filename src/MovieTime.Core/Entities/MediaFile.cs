using System;
using SQLite;

namespace MovieTime.Core.Entities;

/// <summary>
/// Represents a local physical video file associated with a <see cref="Movie"/> or an <see cref="Episode"/>.
/// Supports multi-version media assets (e.g. 1080p, 4K HDR, director cuts) without duplicating entity data.
/// </summary>
[Table("MediaFiles")]
public class MediaFile
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    [Indexed]
    public int? MovieId { get; set; }

    [Indexed]
    public int? EpisodeId { get; set; }

    [Indexed]
    public int? ScanRootId { get; set; }

    [NotNull, MaxLength(2000), Indexed(Unique = true)]
    public string FilePath { get; set; } = string.Empty;

    [NotNull, MaxLength(500)]
    public string FileName { get; set; } = string.Empty;

    [NotNull, MaxLength(20)]
    public string FileExtension { get; set; } = string.Empty;

    [NotNull]
    public long SizeBytes { get; set; }

    public long DurationTicks { get; set; }

    [MaxLength(100)]
    public string? VideoCodec { get; set; }

    public int? VideoWidth { get; set; }

    public int? VideoHeight { get; set; }

    public double? FrameRate { get; set; }

    [MaxLength(100)]
    public string? AudioCodec { get; set; }

    public int? AudioChannels { get; set; }

    [MaxLength(50)]
    public string? ContainerFormat { get; set; }

    public DateTime? FileCreatedAt { get; set; }

    public DateTime? FileModifiedAt { get; set; }

    [NotNull]
    public DateTime LastScannedAt { get; set; } = DateTime.UtcNow;

    [NotNull, Indexed]
    public bool IsMissing { get; set; }

    public double? QualityScore { get; set; }
}
