using System;
using System.Collections.Generic;
using MovieTime.Core.Scanning.Models;

namespace MovieTime.Core.Library.Models;

/// <summary>
/// Structured summary result produced at the completion or cancellation of a library import.
/// </summary>
public sealed class LibraryImportResult
{
    public int FilesDiscovered { get; set; }
    public int FilesImported { get; set; }
    public int FilesUpdated { get; set; }
    public int FilesUnchanged { get; set; }
    public int FilesMissing { get; set; }
    public int MoviesCreated { get; set; }
    public int SeriesCreated { get; set; }
    public int EpisodesCreated { get; set; }
    public int UnknownFiles { get; set; }
    public List<ScanError> Errors { get; } = new();
    public bool IsCancelled { get; set; }
    public TimeSpan Duration { get; set; }
}
