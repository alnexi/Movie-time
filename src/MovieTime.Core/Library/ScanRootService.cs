using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Library;

/// <summary>
/// Production service for managing, validating, and persisting user-selected library folders.
/// Enforces local-only non-root directory safety constraints.
/// </summary>
public sealed class ScanRootService : IScanRootService
{
    private readonly IScanRootRepository _repository;
    private readonly ILogger<ScanRootService> _logger;

    public ScanRootService(
        IScanRootRepository repository,
        ILogger<ScanRootService>? logger = null)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? NullLogger<ScanRootService>.Instance;
    }

    public async Task<IReadOnlyList<ScanRoot>> GetRootsAsync(CancellationToken cancellationToken = default)
    {
        return await _repository.GetAllAsync().ConfigureAwait(false);
    }

    public async Task<ScanRoot> AddRootAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!ValidatePath(path, out var normalizedPath, out var errorMessage))
        {
            throw new ArgumentException(errorMessage, nameof(path));
        }

        var existing = await _repository.GetByNormalizedPathAsync(normalizedPath).ConfigureAwait(false);
        if (existing != null)
        {
            if (!existing.IsEnabled)
            {
                existing.IsEnabled = true;
                await _repository.UpdateAsync(existing).ConfigureAwait(false);
                _logger.LogInformation("Re-enabled existing library scan root '{NormalizedPath}'.", normalizedPath);
            }
            return existing;
        }

        var newRoot = new ScanRoot
        {
            Path = path.Trim(),
            NormalizedPath = normalizedPath,
            IsEnabled = true,
            CreatedAt = DateTime.UtcNow
        };

        await _repository.InsertAsync(newRoot).ConfigureAwait(false);
        _logger.LogInformation("Added new library scan root: '{NormalizedPath}' (Id={Id}).", normalizedPath, newRoot.Id);
        return newRoot;
    }

    public async Task<bool> RemoveRootAsync(int id, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByIdAsync(id).ConfigureAwait(false);
        if (existing == null)
        {
            return false;
        }

        var rows = await _repository.DeleteAsync(id).ConfigureAwait(false);
        _logger.LogInformation("Removed library scan root '{Path}' (Id={Id}). Associated media files remain preserved.",
            existing.Path, id);
        return rows > 0;
    }

    public async Task<bool> SetRootEnabledAsync(int id, bool isEnabled, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByIdAsync(id).ConfigureAwait(false);
        if (existing == null)
        {
            return false;
        }

        if (existing.IsEnabled != isEnabled)
        {
            existing.IsEnabled = isEnabled;
            await _repository.UpdateAsync(existing).ConfigureAwait(false);
            _logger.LogInformation("Scan root {Id} ({Path}) enabled status updated to {IsEnabled}.", id, existing.Path, isEnabled);
        }

        return true;
    }

    public async Task UpdateLastScannedAsync(int id, DateTime scannedAt, CancellationToken cancellationToken = default)
    {
        var existing = await _repository.GetByIdAsync(id).ConfigureAwait(false);
        if (existing != null)
        {
            existing.LastScannedAt = scannedAt;
            await _repository.UpdateAsync(existing).ConfigureAwait(false);
        }
    }

    public bool ValidatePath(string path, out string normalizedPath, out string? errorMessage)
    {
        normalizedPath = string.Empty;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(path))
        {
            errorMessage = "Directory path cannot be empty.";
            return false;
        }

        var trimmed = path.Trim();

        // 1. Check for UNC / Network paths or URL schemes
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal) ||
            trimmed.StartsWith("//", StringComparison.Ordinal) ||
            trimmed.Contains("://", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = "Network locations and remote URLs are not supported. Only local storage paths are permitted.";
            return false;
        }

        // 2. Check for invalid path characters
        if (trimmed.IndexOfAny(Path.GetInvalidPathChars()) != -1)
        {
            errorMessage = "Path contains invalid file system characters.";
            return false;
        }

        // 3. Must be rooted
        if (!Path.IsPathRooted(trimmed))
        {
            errorMessage = "Path must be an absolute path.";
            return false;
        }

        // 4. Canonicalize
        try
        {
            var fullPath = Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            // 5. Check if pointing directly to a whole drive root
            var driveRoot = Path.GetPathRoot(fullPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (string.Equals(fullPath, driveRoot, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Scanning an entire disk root directly is not permitted.";
                return false;
            }

            normalizedPath = fullPath;
            return true;
        }
        catch (Exception ex)
        {
            errorMessage = $"Invalid path syntax: {ex.Message}";
            return false;
        }
    }
}
