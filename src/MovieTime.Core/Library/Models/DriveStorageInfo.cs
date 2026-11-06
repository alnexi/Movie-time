using System.IO;

namespace MovieTime.Core.Library.Models;

/// <summary>
/// Information about a local Windows filesystem storage drive available for library hosting.
/// </summary>
public sealed class DriveStorageInfo
{
    public string DriveLetter { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DriveType DriveType { get; set; }
    public bool IsReady { get; set; }
    public long TotalSizeBytes { get; set; }
    public long FreeSizeBytes { get; set; }
    public string DefaultMLibraryPath { get; set; } = string.Empty;
    public bool MLibraryFolderExists { get; set; }

    public string FreeSizeDisplay
    {
        get
        {
            if (FreeSizeBytes <= 0) return "0 GB";
            double gb = FreeSizeBytes / (1024.0 * 1024.0 * 1024.0);
            return gb >= 1024 ? $"{gb / 1024.0:F1} TB" : $"{gb:F1} GB";
        }
    }

    public string TotalSizeDisplay
    {
        get
        {
            if (TotalSizeBytes <= 0) return "0 GB";
            double gb = TotalSizeBytes / (1024.0 * 1024.0 * 1024.0);
            return gb >= 1024 ? $"{gb / 1024.0:F1} TB" : $"{gb:F1} GB";
        }
    }

    public string CapacitySummary => $"{FreeSizeDisplay} free of {TotalSizeDisplay}";
}
