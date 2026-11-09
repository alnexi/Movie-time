using System;
using System.Collections.Generic;
using MovieTime.Core.Scanning.Models;

namespace MovieTime.Core.Library.Models;

/// <summary>
/// Structured summary result produced at the completion or cancellation of a library import.
/// Contains comprehensive diagnostics detailing discovered, classified, imported, updated,
/// duplicate, skipped, and failed media files.
/// </summary>
public sealed class LibraryImportResult
{
    public int FilesDiscovered { get; set; }
    public int MoviesClassified { get; set; }
    public int TvEpisodesClassified { get; set; }
    public int UnknownFiles { get; set; }
    public int FilesImported { get; set; }
    public int FilesUpdated { get; set; }
    public int FilesUnchanged { get; set; }
    public int FilesMissing { get; set; }
    public int MoviesCreated { get; set; }
    public int MoviesUpdated { get; set; }
    public int SeriesCreated { get; set; }
    public int EpisodesCreated { get; set; }
    public int DuplicateFiles { get; set; }
    public int FilesSkipped { get; set; }
    public int FailedImports => Errors.Count;
    public List<ImportDiagnosticItem> Diagnostics { get; } = new();
    public List<ScanError> Errors { get; } = new();
    public bool IsCancelled { get; set; }
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Generates a human-readable diagnostic summary string for logging and UI reporting.
    /// </summary>
    public string FormatDiagnosticSummary()
    {
        return $"[Import Summary] Discovered: {FilesDiscovered} | Movies Classified: {MoviesClassified} | TV Episodes Classified: {TvEpisodesClassified} | Unknown: {UnknownFiles} | " +
               $"Imported: {FilesImported} (Movies Created: {MoviesCreated}, Series: {SeriesCreated}, Episodes: {EpisodesCreated}) | " +
               $"Updated: {FilesUpdated} (Movies Updated: {MoviesUpdated}) | Duplicates: {DuplicateFiles} | Skipped: {FilesSkipped} | " +
               $"Unchanged: {FilesUnchanged} | Missing: {FilesMissing} | Failed: {FailedImports} | Duration: {Duration.TotalSeconds:F2}s";
    }
}
