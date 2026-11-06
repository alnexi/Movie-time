using System;
using System.Windows.Input;
using MovieTime.App.Common;
using MovieTime.Core.Library.Models;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model representing a registered MLibrary storage folder on a local drive.
/// Tracks drive availability, folder presence on disk, enabled state, and scan actions.
/// </summary>
public sealed class RegisteredMLibraryViewModel : ObservableObject
{
    private int _id;
    private string _path = string.Empty;
    private string _normalizedPath = string.Empty;
    private string _driveLetter = string.Empty;
    private string _volumeLabel = string.Empty;
    private bool _isDriveAvailable;
    private bool _folderExists;
    private bool _isEnabled = true;
    private DateTime _createdAt;
    private DateTime? _lastScannedAt;

    public int Id
    {
        get => _id;
        set => SetProperty(ref _id, value);
    }

    public string Path
    {
        get => _path;
        set => SetProperty(ref _path, value);
    }

    public string NormalizedPath
    {
        get => _normalizedPath;
        set => SetProperty(ref _normalizedPath, value);
    }

    public string DriveLetter
    {
        get => _driveLetter;
        set => SetProperty(ref _driveLetter, value);
    }

    public string VolumeLabel
    {
        get => _volumeLabel;
        set => SetProperty(ref _volumeLabel, value);
    }

    public bool IsDriveAvailable
    {
        get => _isDriveAvailable;
        set
        {
            if (SetProperty(ref _isDriveAvailable, value))
            {
                NotifyStatusProperties();
            }
        }
    }

    public bool FolderExists
    {
        get => _folderExists;
        set
        {
            if (SetProperty(ref _folderExists, value))
            {
                NotifyStatusProperties();
            }
        }
    }

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (SetProperty(ref _isEnabled, value))
            {
                NotifyStatusProperties();
            }
        }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set => SetProperty(ref _createdAt, value);
    }

    public DateTime? LastScannedAt
    {
        get => _lastScannedAt;
        set
        {
            if (SetProperty(ref _lastScannedAt, value))
            {
                OnPropertyChanged(nameof(LastScannedDisplay));
            }
        }
    }

    public string DisplayTitle => !string.IsNullOrWhiteSpace(VolumeLabel)
        ? $"{DriveLetter} ({VolumeLabel})"
        : $"{DriveLetter} Drive";

    public string LastScannedDisplay => LastScannedAt.HasValue
        ? $"Last scanned: {LastScannedAt.Value.ToLocalTime():g}"
        : "Never scanned";

    public string StatusText
    {
        get
        {
            if (!IsDriveAvailable) return "Drive Disconnected";
            if (!FolderExists) return "Folder Missing";
            if (!IsEnabled) return "Disabled";
            return "Available";
        }
    }

    public string StatusBadgeBackground
    {
        get
        {
            if (!IsDriveAvailable) return "#30FF453A";
            if (!FolderExists) return "#30FF9F0A";
            if (!IsEnabled) return "#308E8E93";
            return "#3030D158";
        }
    }

    public string StatusBadgeForeground
    {
        get
        {
            if (!IsDriveAvailable) return "#FF453A";
            if (!FolderExists) return "#FF9F0A";
            if (!IsEnabled) return "#8E8E93";
            return "#30D158";
        }
    }

    public string StatusIconGlyph
    {
        get
        {
            if (!IsDriveAvailable) return "\uEB90";
            if (!FolderExists) return "\uE7BA";
            if (!IsEnabled) return "\uE711";
            return "\uE73E";
        }
    }

    public bool CanScan => IsDriveAvailable && FolderExists && IsEnabled;

    public bool IsAvailableState => IsDriveAvailable && FolderExists && IsEnabled;
    public bool IsDisconnectedState => !IsDriveAvailable;
    public bool IsMissingFolderState => IsDriveAvailable && !FolderExists;
    public bool IsDisabledState => IsDriveAvailable && FolderExists && !IsEnabled;

    public ICommand? ScanCommand { get; set; }
    public ICommand? RemoveCommand { get; set; }
    public ICommand? ToggleEnabledCommand { get; set; }

    private void NotifyStatusProperties()
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(StatusBadgeBackground));
        OnPropertyChanged(nameof(StatusBadgeForeground));
        OnPropertyChanged(nameof(StatusIconGlyph));
        OnPropertyChanged(nameof(CanScan));
        OnPropertyChanged(nameof(IsAvailableState));
        OnPropertyChanged(nameof(IsDisconnectedState));
        OnPropertyChanged(nameof(IsMissingFolderState));
        OnPropertyChanged(nameof(IsDisabledState));
    }

    public static RegisteredMLibraryViewModel FromModel(RegisteredMLibrary model)
    {
        return new RegisteredMLibraryViewModel
        {
            Id = model.Id,
            Path = model.Path,
            NormalizedPath = model.NormalizedPath,
            DriveLetter = model.DriveLetter,
            VolumeLabel = model.VolumeLabel,
            IsDriveAvailable = model.IsDriveAvailable,
            FolderExists = model.FolderExists,
            IsEnabled = model.IsEnabled,
            CreatedAt = model.CreatedAt,
            LastScannedAt = model.LastScannedAt
        };
    }
}
