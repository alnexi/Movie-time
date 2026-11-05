using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Library;

/// <summary>
/// Service for managing and validating user-selected library folders (scan roots).
/// Persists configured roots across application sessions without scanning entire disks or network shares.
/// </summary>
public interface IScanRootService
{
    /// <summary>
    /// Retrieves all configured scan roots.
    /// </summary>
    Task<IReadOnlyList<ScanRoot>> GetRootsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new scan root after validating the path. If the root already exists, ensures it is enabled.
    /// </summary>
    Task<ScanRoot> AddRootAsync(string path, CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a configured scan root by ID without deleting any media files or user records.
    /// </summary>
    Task<bool> RemoveRootAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables or disables a scan root.
    /// </summary>
    Task<bool> SetRootEnabledAsync(int id, bool isEnabled, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the last scanned timestamp for a root.
    /// </summary>
    Task UpdateLastScannedAsync(int id, DateTime scannedAt, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates a candidate directory path safely for local scanning.
    /// </summary>
    bool ValidatePath(string path, out string normalizedPath, out string? errorMessage);
}
