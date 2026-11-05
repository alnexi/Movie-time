using System;

namespace MovieTime.Core.Scanning.Models;

/// <summary>
/// Represents a physical media file discovered during filesystem scanning.
/// </summary>
public sealed class ScannedMediaFile
{
    /// <summary>
    /// Gets the absolute canonical path to the media file on disk.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Gets the file name including extension (e.g. "Dune.2021.1080p.mkv").
    /// </summary>
    public string FileName { get; }

    /// <summary>
    /// Gets the normalized lowercase file extension with leading period (e.g. ".mkv").
    /// </summary>
    public string FileExtension { get; }

    /// <summary>
    /// Gets the file size in bytes.
    /// </summary>
    public long SizeBytes { get; }

    /// <summary>
    /// Gets the last modified timestamp in UTC.
    /// </summary>
    public DateTime LastModifiedUtc { get; }

    /// <summary>
    /// Gets the creation timestamp in UTC.
    /// </summary>
    public DateTime CreatedUtc { get; }

    public ScannedMediaFile(
        string filePath,
        string fileName,
        string fileExtension,
        long sizeBytes,
        DateTime lastModifiedUtc,
        DateTime createdUtc)
    {
        FilePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        FileName = fileName ?? throw new ArgumentNullException(nameof(fileName));
        FileExtension = fileExtension ?? throw new ArgumentNullException(nameof(fileExtension));
        SizeBytes = sizeBytes;
        LastModifiedUtc = lastModifiedUtc;
        CreatedUtc = createdUtc;
    }

    public override string ToString() => $"{FileName} ({SizeBytes:N0} bytes)";
}
