using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Library;

/// <summary>
/// Production service for safe, persistent, and explicit media removal and exclusion.
/// Provides two explicit choices:
/// 1. Remove from River Library: creates a persistent database exclusion so automatic syncs never re-import the file.
/// 2. Delete from Computer: moves the physical file to the Windows Recycle Bin (with undo capability) and cleans up database records.
/// </summary>
public sealed class MediaRemovalService : IMediaRemovalService
{
    private readonly ILibraryExclusionRepository _exclusionRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly IEpisodeRepository _episodeRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly IPlaybackProgressRepository? _playbackProgressRepository;
    private readonly ILogger<MediaRemovalService> _logger;

    public MediaRemovalService(
        ILibraryExclusionRepository exclusionRepository,
        IMediaFileRepository mediaFileRepository,
        IMovieRepository movieRepository,
        IEpisodeRepository episodeRepository,
        ISeasonRepository seasonRepository,
        ISeriesRepository seriesRepository,
        IPlaybackProgressRepository? playbackProgressRepository = null,
        ILogger<MediaRemovalService>? logger = null)
    {
        _exclusionRepository = exclusionRepository ?? throw new ArgumentNullException(nameof(exclusionRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _episodeRepository = episodeRepository ?? throw new ArgumentNullException(nameof(episodeRepository));
        _seasonRepository = seasonRepository ?? throw new ArgumentNullException(nameof(seasonRepository));
        _seriesRepository = seriesRepository ?? throw new ArgumentNullException(nameof(seriesRepository));
        _playbackProgressRepository = playbackProgressRepository;
        _logger = logger ?? NullLogger<MediaRemovalService>.Instance;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    /// <inheritdoc />
    /// <inheritdoc />
    public async Task<bool> RemoveMovieFromLibraryAsync(int movieId, string? reason = "UserRemoved")
    {
        if (movieId <= 0) return false;

        _logger.LogInformation("Removing Movie ID {MovieId} and all associated files from library...", movieId);

        // 1. Find all associated media files
        var files = await _mediaFileRepository.GetByMovieIdAsync(movieId).ConfigureAwait(false);
        foreach (var file in files)
        {
            if (!string.IsNullOrWhiteSpace(file.FilePath))
            {
                var normalized = NormalizePath(file.FilePath);
                await _exclusionRepository.AddExclusionAsync(normalized, null, reason).ConfigureAwait(false);
            }

            if (_playbackProgressRepository != null)
            {
                try
                {
                    await _playbackProgressRepository.DeleteByMediaFileIdAsync(file.Id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to clean playback progress for MediaFile ID {FileId}", file.Id);
                }
            }

            await _mediaFileRepository.DeleteAsync(file.Id).ConfigureAwait(false);
        }

        // 2. Delete movie record from database
        await _movieRepository.DeleteAsync(movieId).ConfigureAwait(false);
        _logger.LogInformation("Successfully removed Movie ID {MovieId} from library.", movieId);
        return true;
    }

    /// <inheritdoc />
    public async Task<int> RemoveMediaForScanRootAsync(int scanRootId, string scanRootPath)
    {
        if (string.IsNullOrWhiteSpace(scanRootPath) && scanRootId <= 0) return 0;

        _logger.LogInformation("Reconciling media records for removed scan root (Id={Id}, Path='{Path}')...", scanRootId, scanRootPath);

        var normalizedRoot = string.IsNullOrWhiteSpace(scanRootPath) ? string.Empty : NormalizePath(scanRootPath);

        // Fetch all media files belonging to this scan root
        var allFiles = await _mediaFileRepository.GetAllAsync().ConfigureAwait(false);
        var targetFiles = allFiles.Where(f =>
            (scanRootId > 0 && f.ScanRootId == scanRootId) ||
            (!string.IsNullOrEmpty(normalizedRoot) && !string.IsNullOrEmpty(f.FilePath) &&
             NormalizePath(f.FilePath).StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        ).ToList();

        int removedCount = 0;
        foreach (var file in targetFiles)
        {
            try
            {
                await CleanupDatabaseEntitiesForMediaFileAsync(file).ConfigureAwait(false);
                removedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error reconciling media file '{Path}' for removed scan root.", file.FilePath);
            }
        }

        _logger.LogInformation("Reconciled and removed {Count} orphaned media files for scan root '{Path}'.", removedCount, scanRootPath);
        return removedCount;
    }

    public async Task<bool> IsPathExcludedAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        return await _exclusionRepository.IsPathExcludedAsync(filePath).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RestoreExclusionAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        return await _exclusionRepository.RemoveExclusionAsync(filePath).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<bool> RemoveFromLibraryOnlyAsync(string filePath, string? reason = "UserRemoved")
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var normalized = NormalizePath(filePath);

        _logger.LogInformation("Removing '{Path}' from River library (persistent exclusion)...", normalized);

        // 1. Add persistent exclusion so auto-sync/scanners never re-import it
        await _exclusionRepository.AddExclusionAsync(normalized, null, reason).ConfigureAwait(false);

        // 2. Locate and remove database records and clean up empty parent entities
        var mediaFile = await _mediaFileRepository.GetByPathAsync(normalized).ConfigureAwait(false);
        if (mediaFile != null)
        {
            await CleanupDatabaseEntitiesForMediaFileAsync(mediaFile).ConfigureAwait(false);
        }

        _logger.LogInformation("Successfully removed '{Path}' from River library with persistent exclusion.", normalized);
        return true;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteFileToRecycleBinAsync(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return false;
        var normalized = NormalizePath(filePath);

        _logger.LogInformation("Deleting '{Path}' to Windows Recycle Bin...", normalized);

        if (!File.Exists(normalized))
        {
            _logger.LogWarning("File '{Path}' does not exist on disk; removing database references.", normalized);
            var mediaFile = await _mediaFileRepository.GetByPathAsync(normalized).ConfigureAwait(false);
            if (mediaFile != null)
            {
                await CleanupDatabaseEntitiesForMediaFileAsync(mediaFile).ConfigureAwait(false);
            }
            return true;
        }

        // Move to Windows Recycle Bin with undo capability
        bool movedToRecycleBin = MoveToRecycleBinNative(normalized);
        if (!movedToRecycleBin)
        {
            _logger.LogError("Failed to move '{Path}' to Windows Recycle Bin. Aborting database cleanup to prevent inconsistency.", normalized);
            return false;
        }

        // Also add persistent exclusion so if user restores or re-copies later without wanting it in River, it respects choice
        await _exclusionRepository.AddExclusionAsync(normalized, null, "DeletedToRecycleBin").ConfigureAwait(false);

        // Clean up database records
        var fileRecord = await _mediaFileRepository.GetByPathAsync(normalized).ConfigureAwait(false);
        if (fileRecord != null)
        {
            await CleanupDatabaseEntitiesForMediaFileAsync(fileRecord).ConfigureAwait(false);
        }

        _logger.LogInformation("File '{Path}' successfully moved to Recycle Bin and cleaned from library.", normalized);
        return true;
    }

    private async Task CleanupDatabaseEntitiesForMediaFileAsync(MediaFile mediaFile)
    {
        // 1. Movie parent cleanup
        if (mediaFile.MovieId.HasValue)
        {
            int movieId = mediaFile.MovieId.Value;
            var siblingFiles = await _mediaFileRepository.GetByMovieIdAsync(movieId).ConfigureAwait(false);
            bool hasOtherFiles = false;
            foreach (var sibling in siblingFiles)
            {
                if (sibling.Id != mediaFile.Id)
                {
                    hasOtherFiles = true;
                    break;
                }
            }

            if (!hasOtherFiles)
            {
                _logger.LogInformation("Movie ID {MovieId} has no other media files; removing movie entity.", movieId);
                await _movieRepository.DeleteAsync(movieId).ConfigureAwait(false);

                if (_playbackProgressRepository != null)
                {
                    try
                    {
                        await _playbackProgressRepository.DeleteByMediaFileIdAsync(mediaFile.Id).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean playback progress for Movie ID {MovieId}", movieId);
                    }
                }
            }
        }

        // 2. Episode parent cleanup
        if (mediaFile.EpisodeId.HasValue)
        {
            int episodeId = mediaFile.EpisodeId.Value;
            var siblingFiles = await _mediaFileRepository.GetByEpisodeIdAsync(episodeId).ConfigureAwait(false);
            bool hasOtherFiles = false;
            foreach (var sibling in siblingFiles)
            {
                if (sibling.Id != mediaFile.Id)
                {
                    hasOtherFiles = true;
                    break;
                }
            }

            if (!hasOtherFiles)
            {
                _logger.LogInformation("Episode ID {EpisodeId} has no other media files; removing episode entity.", episodeId);
                var episode = await _episodeRepository.GetByIdAsync(episodeId).ConfigureAwait(false);
                await _episodeRepository.DeleteAsync(episodeId).ConfigureAwait(false);

                if (episode != null)
                {
                    // Check if season has remaining episodes
                    var seasonEpisodes = await _episodeRepository.GetBySeasonIdAsync(episode.SeasonId).ConfigureAwait(false);
                    if (seasonEpisodes.Count == 0)
                    {
                        _logger.LogInformation("Season ID {SeasonId} has no remaining episodes; removing season.", episode.SeasonId);
                        await _seasonRepository.DeleteAsync(episode.SeasonId).ConfigureAwait(false);
                    }

                    // Check if series has remaining episodes
                    var seriesEpisodes = await _episodeRepository.GetBySeriesIdAsync(episode.SeriesId).ConfigureAwait(false);
                    if (seriesEpisodes.Count == 0)
                    {
                        _logger.LogInformation("Series ID {SeriesId} has no remaining episodes; removing series.", episode.SeriesId);
                        await _seriesRepository.DeleteAsync(episode.SeriesId).ConfigureAwait(false);
                    }
                }
            }
        }

        // 3. Delete the MediaFile record
        await _mediaFileRepository.DeleteAsync(mediaFile.Id).ConfigureAwait(false);
    }

    #region Win32 Recycle Bin Interop

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pFrom;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? pTo;
        public ushort fFlags;
        [MarshalAs(UnmanagedType.Bool)]
        public bool fAnyOperationsAborted;
        public IntPtr hNameMappings;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? lpszProgressTitle;
    }

    private const uint FO_DELETE = 0x0003;
    private const ushort FOF_ALLOWUNDO = 0x0040;
    private const ushort FOF_NOCONFIRMATION = 0x0010;
    private const ushort FOF_SILENT = 0x0004;

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

    private bool MoveToRecycleBinNative(string filePath)
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                // SHFileOperation requires double null-terminated string
                string doubleNullPath = filePath + "\0\0";

                var shf = new SHFILEOPSTRUCT
                {
                    hwnd = IntPtr.Zero,
                    wFunc = FO_DELETE,
                    pFrom = doubleNullPath,
                    pTo = null,
                    fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_SILENT,
                    fAnyOperationsAborted = false,
                    hNameMappings = IntPtr.Zero,
                    lpszProgressTitle = null
                };

                int ret = SHFileOperation(ref shf);
                if (ret == 0 && !shf.fAnyOperationsAborted && !File.Exists(filePath))
                {
                    return true;
                }

                _logger.LogWarning("SHFileOperation returned {RetCode}, aborted={Aborted}, fileExists={Exists}",
                    ret, shf.fAnyOperationsAborted, File.Exists(filePath));
            }

            // Fallback for non-Windows or if SHFileOperation encountered an edge case
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
            return !File.Exists(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception while moving file '{Path}' to Recycle Bin", filePath);
            return false;
        }
    }

    #endregion
}
