namespace MovieTime.Core.Library.Models;

/// <summary>
/// Result of previewing a candidate drive or path for MLibrary registration.
/// </summary>
public sealed class MLibraryRegistrationPreview
{
    public string TargetPath { get; set; } = string.Empty;
    public string DriveLetter { get; set; } = string.Empty;
    public string VolumeLabel { get; set; } = string.Empty;
    public bool IsDriveReady { get; set; }
    public bool FolderAlreadyExists { get; set; }
    public bool IsAlreadyRegistered { get; set; }
    public string? ErrorMessage { get; set; }

    public bool CanRegister => IsDriveReady && !IsAlreadyRegistered && string.IsNullOrEmpty(ErrorMessage);
    public bool NeedsCreationConfirmation => CanRegister && !FolderAlreadyExists;
}
