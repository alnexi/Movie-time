namespace MovieTime.Core.Library.Models;

/// <summary>
/// Reports progress updates during library scanning, parsing, and database synchronization.
/// </summary>
public sealed class LibraryImportProgress
{
    public string CurrentRoot { get; set; } = string.Empty;
    public string? CurrentFile { get; set; }
    public int FilesDiscovered { get; set; }
    public int FilesProcessed { get; set; }
    public int TotalFiles { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
}
