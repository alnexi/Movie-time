using System.Threading.Tasks;

namespace MovieTime.Core.Library;

/// <summary>
/// Service providing safe, persistent, and explicit media removal and exclusion.
/// Distinguishes between removing from River Library (persistent exclusion) and
/// moving the actual video file to the Windows Recycle Bin.
/// </summary>
public interface IMediaRemovalService
{
    /// <summary>
    /// Removes the file from the River library only, creating a persistent exclusion
    /// so that future automatic syncs and scans never re-import it.
    /// Cleans up database records for the file and any empty movie/series/episode parent entities.
    /// </summary>
    Task<bool> RemoveFromLibraryOnlyAsync(string filePath, string? reason = "UserRemoved");

    /// <summary>
    /// Moves the file to the Windows Recycle Bin (with undo capability) and cleans up its database records.
    /// </summary>
    Task<bool> DeleteFileToRecycleBinAsync(string filePath);

    /// <summary>
    /// Restores a previously excluded file back into the eligible library list.
    /// </summary>
    Task<bool> RestoreExclusionAsync(string filePath);

    /// <summary>
    /// Checks whether a given path is currently excluded.
    /// </summary>
    Task<bool> IsPathExcludedAsync(string filePath);

    /// <summary>
    /// Removes a movie and all its associated media files and progress from the River library,
    /// adding persistent exclusions for any registered file paths.
    /// Safely handles movies whose physical files are missing or already deleted.
    /// </summary>
    Task<bool> RemoveMovieFromLibraryAsync(int movieId, string? reason = "UserRemoved");

    /// <summary>
    /// Reconciles and removes all media files and associated parent entities (movies, episodes, seasons, series)
    /// belonging exclusively to the specified scan root folder when that scan root is unregistered.
    /// </summary>
    Task<int> RemoveMediaForScanRootAsync(int scanRootId, string scanRootPath);
}
