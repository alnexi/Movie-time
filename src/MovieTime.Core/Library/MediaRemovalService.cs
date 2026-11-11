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
    private readonly IScanRootRepository? _scanRootRepository;
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
        : this(exclusionRepository, mediaFileRepository, movieRepository, episodeRepository, seasonRepository, seriesRepository, playbackProgressRepository, null, logger)
    {
    }

    public MediaRemovalService(
        ILibraryExclusionRepository exclusionRepository,
        IMediaFileRepository mediaFileRepository,
        IMovieRepository movieRepository,
        IEpisodeRepository episodeRepository,
        ISeasonRepository seasonRepository,
        ISeriesRepository seriesRepository,
        IPlaybackProgressRepository? playbackProgressRepository,
        IScanRootRepository? scanRootRepository,
        ILogger<MediaRemovalService>? logger = null)
    {
        _exclusionRepository = exclusionRepository ?? throw new ArgumentNullException(nameof(exclusionRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _episodeRepository = episodeRepository ?? throw new ArgumentNullException(nameof(episodeRepository));
        _seasonRepository = seasonRepository ?? throw new ArgumentNullException(nameof(seasonRepository));
        _seriesRepository = seriesRepository ?? throw new ArgumentNullException(nameof(seriesRepository));
        _playbackProgressRepository = playbackProgressRepository;
        _scanRootRepository = scanRootRepository;
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

    /// <summary>
    /// Checks whether the target file path resides strictly inside the specified directory path,
    /// respecting filesystem directory separators and boundaries.
    /// </summary>
    public static bool IsPathUnderDirectory(string filePath, string directoryPath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(directoryPath)) return false;
        var normFile = NormalizePath(filePath);
        var normDir = NormalizePath(directoryPath);
        if (normFile.Equals(normDir, StringComparison.OrdinalIgnoreCase)) return true;
        var dirWithSep = normDir.EndsWith(Path.DirectorySeparatorChar)
            ? normDir
            : normDir + Path.DirectorySeparatorChar;
        var dirWithAlt = normDir.EndsWith(Path.AltDirectorySeparatorChar)
            ? normDir
            : normDir + Path.AltDirectorySeparatorChar;
        return normFile.StartsWith(dirWithSep, StringComparison.OrdinalIgnoreCase) ||
               normFile.StartsWith(dirWithAlt, StringComparison.OrdinalIgnoreCase);
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

        if (_playbackProgressRepository != null)
        {
            try
            {
                await _playbackProgressRepository.DeleteByMovieIdAsync(movieId).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean playback progress for Movie ID {MovieId}", movieId);
            }
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

        // Fetch surviving active scan roots if repository is available to preserve shared/nested media
        var survivingRoots = new System.Collections.Generic.List<ScanRoot>();
        if (_scanRootRepository != null)
        {
            try
            {
                var allRoots = await _scanRootRepository.GetAllAsync().ConfigureAwait(false);
                survivingRoots = allRoots.Where(r => r.Id != scanRootId && r.IsEnabled).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load surviving scan roots for reconciliation check.");
            }
        }

        // Fetch all media files belonging to this scan root or located under its path
        var allFiles = await _mediaFileRepository.GetAllAsync().ConfigureAwait(false);
        var targetFiles = allFiles.Where(f =>
            (scanRootId > 0 && f.ScanRootId == scanRootId) ||
            (!string.IsNullOrEmpty(scanRootPath) && !string.IsNullOrEmpty(f.FilePath) &&
             IsPathUnderDirectory(f.FilePath, scanRootPath))
        ).ToList();

        int removedCount = 0;
        foreach (var file in targetFiles)
        {
            // If another surviving active scan root also covers this file, preserve it and reassign ScanRootId
            var coveringSurvivingRoot = survivingRoots.FirstOrDefault(r => IsPathUnderDirectory(file.FilePath, r.Path));
            if (coveringSurvivingRoot != null)
            {
                _logger.LogInformation("Media file '{Path}' is still covered by surviving scan root {RootId} ('{RootPath}'). Reassigning and preserving.",
                    file.FilePath, coveringSurvivingRoot.Id, coveringSurvivingRoot.Path);

                if (file.ScanRootId != coveringSurvivingRoot.Id)
                {
                    file.ScanRootId = coveringSurvivingRoot.Id;
                    await _mediaFileRepository.UpdateAsync(file).ConfigureAwait(false);
                }
                continue;
            }

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
        // 1. Clean up playback progress for this media file
        if (_playbackProgressRepository != null)
        {
            try
            {
                await _playbackProgressRepository.DeleteByMediaFileIdAsync(mediaFile.Id).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to clean playback progress for MediaFile ID {FileId}", mediaFile.Id);
            }
        }

        // 2. Movie parent cleanup
        if (mediaFile.MovieId.HasValue)
        {
            int movieId = mediaFile.MovieId.Value;
            var siblingFiles = await _mediaFileRepository.GetByMovieIdAsync(movieId).ConfigureAwait(false);
            bool hasOtherFiles = siblingFiles.Any(sibling => sibling.Id != mediaFile.Id);

            if (!hasOtherFiles)
            {
                _logger.LogInformation("Movie ID {MovieId} has no other media files; removing movie entity.", movieId);
                await _movieRepository.DeleteAsync(movieId).ConfigureAwait(false);

                if (_playbackProgressRepository != null)
                {
                    try
                    {
                        await _playbackProgressRepository.DeleteByMovieIdAsync(movieId).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean playback progress for Movie ID {MovieId}", movieId);
                    }
                }
            }
        }

        // 3. Episode parent cleanup
        if (mediaFile.EpisodeId.HasValue)
        {
            int episodeId = mediaFile.EpisodeId.Value;
            var siblingFiles = await _mediaFileRepository.GetByEpisodeIdAsync(episodeId).ConfigureAwait(false);
            bool hasOtherFiles = siblingFiles.Any(sibling => sibling.Id != mediaFile.Id);

            if (!hasOtherFiles)
            {
                _logger.LogInformation("Episode ID {EpisodeId} has no other media files; removing episode entity.", episodeId);
                var episode = await _episodeRepository.GetByIdAsync(episodeId).ConfigureAwait(false);
                await _episodeRepository.DeleteAsync(episodeId).ConfigureAwait(false);

                if (_playbackProgressRepository != null)
                {
                    try
                    {
                        await _playbackProgressRepository.DeleteByEpisodeIdAsync(episodeId).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to clean playback progress for Episode ID {EpisodeId}", episodeId);
                    }
                }

                if (episode != null)
                {
                    // Check if season has remaining episodes
                    var seasonEpisodes = await _episodeRepository.GetBySeasonIdAsync(episode.SeasonId).ConfigureAwait(false);
                    bool hasOtherEpisodesInSeason = seasonEpisodes.Any(e => e.Id != episodeId);
                    if (!hasOtherEpisodesInSeason)
                    {
                        _logger.LogInformation("Season ID {SeasonId} has no remaining episodes; removing season.", episode.SeasonId);
                        await _seasonRepository.DeleteAsync(episode.SeasonId).ConfigureAwait(false);
                    }

                    // Check if series has remaining episodes
                    var seriesEpisodes = await _episodeRepository.GetBySeriesIdAsync(episode.SeriesId).ConfigureAwait(false);
                    bool hasOtherEpisodesInSeries = seriesEpisodes.Any(e => e.Id != episodeId);
                    if (!hasOtherEpisodesInSeries)
                    {
                        _logger.LogInformation("Series ID {SeriesId} has no remaining episodes; removing series.", episode.SeriesId);
                        await _seriesRepository.DeleteAsync(episode.SeriesId).ConfigureAwait(false);
                    }
                }
            }
        }

        // 4. Delete the MediaFile record
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
