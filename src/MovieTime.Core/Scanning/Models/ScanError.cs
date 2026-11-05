namespace MovieTime.Core.Scanning.Models;

/// <summary>
/// Represents an error or access restriction encountered while scanning a filesystem path.
/// </summary>
public sealed class ScanError
{
    /// <summary>
    /// Gets the path that caused the error (directory or file).
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// Gets the error description.
    /// </summary>
    public string Message { get; }

    /// <summary>
    /// Gets the exception type name that occurred.
    /// </summary>
    public string ExceptionType { get; }

    public ScanError(string path, string message, string exceptionType)
    {
        Path = path ?? string.Empty;
        Message = message ?? string.Empty;
        ExceptionType = exceptionType ?? string.Empty;
    }

    public override string ToString() => $"[{ExceptionType}] {Path}: {Message}";
}
