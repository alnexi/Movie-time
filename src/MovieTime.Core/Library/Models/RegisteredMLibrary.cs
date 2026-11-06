using System;

namespace MovieTime.Core.Library.Models;

/// <summary>
/// Status and metadata for a registered MLibrary storage folder.
/// </summary>
public sealed class RegisteredMLibrary
{
    public int Id { get; set; }
    public string Path { get; set; } = string.Empty;
    public string NormalizedPath { get; set; } = string.Empty;
    public string DriveLetter { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public bool IsDriveAvailable { get; set; }
    public bool FolderExists { get; set; }
    public bool IsEnabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastScannedAt { get; set; }

    public string StatusText
    {
        get
        {
            if (!IsDriveAvailable) return "Drive Disconnected";
            if (!FolderExists) return "Folder Missing";
            return IsEnabled ? "Available" : "Disabled";
        }
    }

    public string StatusSeverity
    {
        get
        {
            if (!IsDriveAvailable) return "Error";
            if (!FolderExists) return "Warning";
            return IsEnabled ? "Success" : "Info";
        }
    }

    public string LastScannedDisplay => LastScannedAt.HasValue
        ? LastScannedAt.Value.ToLocalTime().ToString("g")
        : "Never scanned";
}
