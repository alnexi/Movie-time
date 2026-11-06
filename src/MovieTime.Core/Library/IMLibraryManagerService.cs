using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MovieTime.Core.Library.Models;

namespace MovieTime.Core.Library;

/// <summary>
/// Service managing multi-drive MLibrary storage folders inspired by Steam's library management.
/// Restricts media discovery strictly to registered MLibrary directories without touching entire disks.
/// </summary>
public interface IMLibraryManagerService
{
    /// <summary>
    /// Enumerates all local storage drives present in the system.
    /// </summary>
    IReadOnlyList<DriveStorageInfo> GetAvailableDrives();

    /// <summary>
    /// Retrieves all registered MLibrary folders with active drive and folder availability status.
    /// </summary>
    Task<IReadOnlyList<RegisteredMLibrary>> GetRegisteredLibrariesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Previews adding an MLibrary folder on the given drive.
    /// </summary>
    Task<MLibraryRegistrationPreview> PreviewDriveRegistrationAsync(string driveName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an MLibrary on the specified drive (e.g. D:\MLibrary).
    /// If the folder does not exist, it will only be created if <paramref name="createFolder"/> is true.
    /// </summary>
    Task<RegisteredMLibrary> RegisterDriveLibraryAsync(string driveName, bool createFolder = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers an existing custom directory path as an MLibrary folder.
    /// </summary>
    Task<RegisteredMLibrary> RegisterCustomLibraryPathAsync(string customPath, bool createFolder = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters an MLibrary folder by ID without deleting any physical media files or records.
    /// </summary>
    Task<bool> RemoveLibraryAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables an MLibrary folder.
    /// </summary>
    Task<bool> SetLibraryEnabledAsync(int id, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Scans registered MLibrary folders to discover and reconcile local movies.
    /// </summary>
    Task<LibraryImportResult> ScanLibrariesAsync(
        IReadOnlyList<int>? libraryIds = null,
        IProgress<LibraryImportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
