using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Library.Models;

namespace MovieTime.Core.Library;

/// <summary>
/// Production implementation of multi-drive MLibrary management.
/// Guarantees that only designated MLibrary folders are registered and scanned.
/// </summary>
public sealed class MLibraryManagerService : IMLibraryManagerService
{
    public const string DefaultLibraryFolderName = "MLibrary";

    private readonly IScanRootService _scanRootService;
    private readonly ILibraryImportService _importService;
    private readonly ILogger<MLibraryManagerService> _logger;

    public MLibraryManagerService(
        IScanRootService scanRootService,
        ILibraryImportService importService,
        ILogger<MLibraryManagerService>? logger = null)
    {
        _scanRootService = scanRootService ?? throw new ArgumentNullException(nameof(scanRootService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _logger = logger ?? NullLogger<MLibraryManagerService>.Instance;
    }

    public IReadOnlyList<DriveStorageInfo> GetAvailableDrives()
    {
        var result = new List<DriveStorageInfo>();

        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                try
                {
                    // Exclude network shares, CD-ROMs, and RAM disks
                    if (drive.DriveType != DriveType.Fixed && drive.DriveType != DriveType.Removable)
                    {
                        continue;
                    }

                    bool isReady = drive.IsReady;
                    string volumeLabel = string.Empty;
                    long totalSize = 0;
                    long freeSize = 0;

                    if (isReady)
                    {
                        try
                        {
                            volumeLabel = drive.VolumeLabel;
                            totalSize = drive.TotalSize;
                            freeSize = drive.AvailableFreeSpace;
                        }
                        catch
                        {
                            // Some mounted drives may restrict volume property queries
                        }
                    }

                    string driveName = drive.Name.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    string defaultMLibraryPath = Path.Combine(drive.Name, DefaultLibraryFolderName);
                    bool folderExists = isReady && Directory.Exists(defaultMLibraryPath);

                    string displayName = !string.IsNullOrWhiteSpace(volumeLabel)
                        ? $"{driveName} ({volumeLabel})"
                        : $"{driveName} [Local Drive]";

                    result.Add(new DriveStorageInfo
                    {
                        DriveLetter = driveName,
                        VolumeLabel = volumeLabel,
                        DisplayName = displayName,
                        DriveType = drive.DriveType,
                        IsReady = isReady,
                        TotalSizeBytes = totalSize,
                        FreeSizeBytes = freeSize,
                        DefaultMLibraryPath = defaultMLibraryPath,
                        MLibraryFolderExists = folderExists
                    });
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to inspect drive '{DriveName}'.", drive.Name);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enumerate local drives via DriveInfo.");
        }

        return result;
    }

    public async Task<IReadOnlyList<RegisteredMLibrary>> GetRegisteredLibrariesAsync(CancellationToken cancellationToken = default)
    {
        var roots = await _scanRootService.GetRootsAsync(cancellationToken).ConfigureAwait(false);
        var list = new List<RegisteredMLibrary>();

        foreach (var root in roots)
        {
            string driveLetter = string.Empty;
            string volumeLabel = string.Empty;
            bool isDriveAvailable = false;
            bool folderExists = false;

            try
            {
                var rootPath = Path.GetPathRoot(root.Path);
                if (!string.IsNullOrWhiteSpace(rootPath))
                {
                    driveLetter = rootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (Directory.Exists(rootPath))
                    {
                        isDriveAvailable = true;
                        try
                        {
                            var dInfo = new DriveInfo(rootPath);
                            if (dInfo.IsReady)
                            {
                                volumeLabel = dInfo.VolumeLabel;
                            }
                        }
                        catch
                        {
                            // Best-effort drive info query
                        }
                    }
                }

                folderExists = isDriveAvailable && Directory.Exists(root.Path);
            }
            catch
            {
                // Unmounted or malformed path
            }

            list.Add(new RegisteredMLibrary
            {
                Id = root.Id,
                Path = root.Path,
                NormalizedPath = root.NormalizedPath,
                DriveLetter = driveLetter,
                VolumeLabel = volumeLabel,
                IsDriveAvailable = isDriveAvailable,
                FolderExists = folderExists,
                IsEnabled = root.IsEnabled,
                CreatedAt = root.CreatedAt,
                LastScannedAt = root.LastScannedAt
            });
        }

        return list;
    }

    public async Task<MLibraryRegistrationPreview> PreviewDriveRegistrationAsync(string driveName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(driveName))
        {
            return new MLibraryRegistrationPreview
            {
                ErrorMessage = "Drive letter cannot be empty."
            };
        }

        var trimmed = driveName.Trim();
        string driveRoot = trimmed;
        if (!driveRoot.EndsWith(Path.DirectorySeparatorChar) && !driveRoot.EndsWith(Path.AltDirectorySeparatorChar))
        {
            driveRoot += Path.DirectorySeparatorChar;
        }

        bool isReady = false;
        string volumeLabel = string.Empty;

        try
        {
            var drive = new DriveInfo(driveRoot);
            isReady = drive.IsReady;
            if (isReady)
            {
                volumeLabel = drive.VolumeLabel;
            }
        }
        catch (Exception ex)
        {
            return new MLibraryRegistrationPreview
            {
                DriveLetter = trimmed,
                ErrorMessage = $"Drive '{trimmed}' is not accessible: {ex.Message}"
            };
        }

        if (!isReady)
        {
            return new MLibraryRegistrationPreview
            {
                DriveLetter = trimmed,
                ErrorMessage = $"Drive '{trimmed}' is not ready."
            };
        }

        string targetPath = Path.Combine(driveRoot, DefaultLibraryFolderName);
        bool folderExists = Directory.Exists(targetPath);

        // Check if already registered
        var existingRoots = await _scanRootService.GetRootsAsync(cancellationToken).ConfigureAwait(false);
        bool isAlreadyRegistered = existingRoots.Any(r =>
            string.Equals(r.NormalizedPath, Path.GetFullPath(targetPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) ||
            string.Equals(r.Path, targetPath, StringComparison.OrdinalIgnoreCase));

        return new MLibraryRegistrationPreview
        {
            TargetPath = targetPath,
            DriveLetter = driveRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
            VolumeLabel = volumeLabel,
            IsDriveReady = isReady,
            FolderAlreadyExists = folderExists,
            IsAlreadyRegistered = isAlreadyRegistered,
            ErrorMessage = isAlreadyRegistered ? $"An MLibrary folder on drive {trimmed} is already registered." : null
        };
    }

    public async Task<RegisteredMLibrary> RegisterDriveLibraryAsync(string driveName, bool createFolder = false, CancellationToken cancellationToken = default)
    {
        var preview = await PreviewDriveRegistrationAsync(driveName, cancellationToken).ConfigureAwait(false);

        if (!preview.CanRegister)
        {
            throw new InvalidOperationException(preview.ErrorMessage ?? "Cannot register library on this drive.");
        }

        if (!preview.FolderAlreadyExists)
        {
            if (!createFolder)
            {
                throw new InvalidOperationException($"The folder '{preview.TargetPath}' does not exist on disk. Confirmation is required before creating it.");
            }

            try
            {
                Directory.CreateDirectory(preview.TargetPath);
                _logger.LogInformation("Created new MLibrary folder on disk: '{TargetPath}'.", preview.TargetPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create MLibrary directory on disk: '{TargetPath}'.", preview.TargetPath);
                throw new IOException($"Could not create MLibrary directory at '{preview.TargetPath}': {ex.Message}", ex);
            }
        }

        var scanRoot = await _scanRootService.AddRootAsync(preview.TargetPath, cancellationToken).ConfigureAwait(false);

        return new RegisteredMLibrary
        {
            Id = scanRoot.Id,
            Path = scanRoot.Path,
            NormalizedPath = scanRoot.NormalizedPath,
            DriveLetter = preview.DriveLetter,
            VolumeLabel = preview.VolumeLabel,
            IsDriveAvailable = true,
            FolderExists = true,
            IsEnabled = scanRoot.IsEnabled,
            CreatedAt = scanRoot.CreatedAt,
            LastScannedAt = scanRoot.LastScannedAt
        };
    }

    public async Task<RegisteredMLibrary> RegisterCustomLibraryPathAsync(string customPath, bool createFolder = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(customPath))
        {
            throw new ArgumentException("Library path cannot be empty.", nameof(customPath));
        }

        if (!_scanRootService.ValidatePath(customPath, out var normalizedPath, out var errorMessage))
        {
            throw new ArgumentException(errorMessage ?? "Invalid library path.", nameof(customPath));
        }

        // Must end with MLibrary or be an explicit directory
        if (!Directory.Exists(normalizedPath))
        {
            if (!createFolder)
            {
                throw new DirectoryNotFoundException($"The target directory '{normalizedPath}' does not exist on disk.");
            }

            try
            {
                Directory.CreateDirectory(normalizedPath);
                _logger.LogInformation("Created custom MLibrary directory: '{Path}'.", normalizedPath);
            }
            catch (Exception ex)
            {
                throw new IOException($"Could not create directory at '{normalizedPath}': {ex.Message}", ex);
            }
        }

        var scanRoot = await _scanRootService.AddRootAsync(normalizedPath, cancellationToken).ConfigureAwait(false);
        string driveLetter = Path.GetPathRoot(normalizedPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;

        return new RegisteredMLibrary
        {
            Id = scanRoot.Id,
            Path = scanRoot.Path,
            NormalizedPath = scanRoot.NormalizedPath,
            DriveLetter = driveLetter,
            VolumeLabel = string.Empty,
            IsDriveAvailable = true,
            FolderExists = true,
            IsEnabled = scanRoot.IsEnabled,
            CreatedAt = scanRoot.CreatedAt,
            LastScannedAt = scanRoot.LastScannedAt
        };
    }

    public async Task<bool> RemoveLibraryAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _scanRootService.RemoveRootAsync(id, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> SetLibraryEnabledAsync(int id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        return await _scanRootService.SetRootEnabledAsync(id, isEnabled, cancellationToken).ConfigureAwait(false);
    }

    public async Task<LibraryImportResult> ScanLibrariesAsync(
        IReadOnlyList<int>? libraryIds = null,
        IProgress<LibraryImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var options = new Models.LibraryImportOptions();
        if (libraryIds != null && libraryIds.Count > 0)
        {
            options.ScanRootIds = libraryIds.ToList();
        }

        _logger.LogInformation("Initiating MLibrary scan across registered storage locations.");
        var result = await _importService.ImportAsync(options, progress, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("MLibrary scan finished. {DiagnosticSummary}", result.FormatDiagnosticSummary());
        return result;
    }
}
