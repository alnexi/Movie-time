namespace MovieTime.Core.Library.Models;

/// <summary>
/// Diagnostic entry describing a rejected, unclassified, skipped, or failed file during library import.
/// Ensures that missing or anomalous media files are diagnosable rather than silently ignored.
/// </summary>
public sealed class ImportDiagnosticItem
{
    /// <summary>
    /// Filename of the item (without sensitive parent directory paths).
    /// </summary>
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// Classified category or outcome (e.g. "Unknown", "Skipped", "Failed").
    /// </summary>
    public string Classification { get; set; } = string.Empty;

    /// <summary>
    /// Specific human-readable reason for rejection, failure, or classification.
    /// </summary>
    public string Reason { get; set; } = string.Empty;

    public ImportDiagnosticItem() { }

    public ImportDiagnosticItem(string fileName, string classification, string reason)
    {
        FileName = fileName;
        Classification = classification;
        Reason = reason;
    }

    public override string ToString() => $"[{Classification}] {FileName} — {Reason}";
}
