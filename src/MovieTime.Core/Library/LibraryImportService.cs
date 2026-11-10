using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Artwork;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Library.Models;
using MovieTime.Core.Parsing;
using MovieTime.Core.Parsing.Models;
using MovieTime.Core.Scanning;
using MovieTime.Core.Scanning.Models;

namespace MovieTime.Core.Library;

/// <summary>
/// Production library synchronization service coordinating filesystem traversal,
/// filename heuristic parsing, entity graph creation, missing file reconciliation, and safe cancellation.
/// </summary>
public sealed class LibraryImportService : ILibraryImportService
{
    private readonly IScanRootService _scanRootService;
    private readonly IMediaScannerService _scannerService;
    private readonly IFilenameParserEngine _parserEngine;
    private readonly IDatabaseService _databaseService;
    private readonly IMovieRepository _movieRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IEpisodeRepository _episodeRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IArtworkPipelineService? _artworkPipelineService;
    private readonly ILibraryExclusionRepository? _exclusionRepository;
    private readonly ILogger<LibraryImportService> _logger;

    public event EventHandler<LibraryImportResult>? ImportCompleted;

    private readonly SemaphoreSlim _importLock = new(1, 1);
    private volatile bool _isImporting;

    public bool IsImporting => _isImporting;

    public LibraryImportService(
        IScanRootService scanRootService,
        IMediaScannerService scannerService,
        IFilenameParserEngine parserEngine,
        IDatabaseService databaseService,
        IMovieRepository movieRepository,
        ISeriesRepository seriesRepository,
        ISeasonRepository seasonRepository,
        IEpisodeRepository episodeRepository,
        IMediaFileRepository mediaFileRepository,
        ILogger<LibraryImportService>? logger = null)
        : this(
            scanRootService,
            scannerService,
            parserEngine,
            databaseService,
            movieRepository,
            seriesRepository,
            seasonRepository,
            episodeRepository,
            mediaFileRepository,
            null,
            null,
            logger)
    {
    }

    public LibraryImportService(
        IScanRootService scanRootService,
        IMediaScannerService scannerService,
        IFilenameParserEngine parserEngine,
        IDatabaseService databaseService,
        IMovieRepository movieRepository,
        ISeriesRepository seriesRepository,
        ISeasonRepository seasonRepository,
        IEpisodeRepository episodeRepository,
        IMediaFileRepository mediaFileRepository,
        IArtworkPipelineService? artworkPipelineService,
        ILogger<LibraryImportService>? logger)
        : this(
            scanRootService,
            scannerService,
            parserEngine,
            databaseService,
            movieRepository,
            seriesRepository,
            seasonRepository,
            episodeRepository,
            mediaFileRepository,
            artworkPipelineService,
            null,
            logger)
    {
    }

    public LibraryImportService(
        IScanRootService scanRootService,
        IMediaScannerService scannerService,
        IFilenameParserEngine parserEngine,
        IDatabaseService databaseService,
        IMovieRepository movieRepository,
        ISeriesRepository seriesRepository,
        ISeasonRepository seasonRepository,
        IEpisodeRepository episodeRepository,
        IMediaFileRepository mediaFileRepository,
        IArtworkPipelineService? artworkPipelineService,
        ILibraryExclusionRepository? exclusionRepository = null,
        ILogger<LibraryImportService>? logger = null)
    {
        _scanRootService = scanRootService ?? throw new ArgumentNullException(nameof(scanRootService));
        _scannerService = scannerService ?? throw new ArgumentNullException(nameof(scannerService));
        _parserEngine = parserEngine ?? throw new ArgumentNullException(nameof(parserEngine));
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _seriesRepository = seriesRepository ?? throw new ArgumentNullException(nameof(seriesRepository));
        _seasonRepository = seasonRepository ?? throw new ArgumentNullException(nameof(seasonRepository));
        _episodeRepository = episodeRepository ?? throw new ArgumentNullException(nameof(episodeRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _artworkPipelineService = artworkPipelineService;
        _exclusionRepository = exclusionRepository;
        _logger = logger ?? NullLogger<LibraryImportService>.Instance;
    }

    public async Task<LibraryImportResult> ImportAsync(
        LibraryImportOptions? options = null,
        IProgress<LibraryImportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new LibraryImportResult { IsCancelled = true };
        }

        // 1. Guard against concurrent imports
        if (!_importLock.Wait(0))
        {
            throw new InvalidOperationException("A library import operation is already in progress.");
        }

        _isImporting = true;
        var stopwatch = Stopwatch.StartNew();
        var result = new LibraryImportResult();

        try
        {
            // 2. Ensure database engine is initialized and migrated
            await _databaseService.InitializeAsync(cancellationToken).ConfigureAwait(false);

            // 3. Resolve configured scan roots
            var allRoots = await _scanRootService.GetRootsAsync(cancellationToken).ConfigureAwait(false);
            var enabledRoots = allRoots.Where(r => r.IsEnabled).ToList();

            if (options?.ScanRootIds != null && options.ScanRootIds.Count > 0)
            {
                enabledRoots = enabledRoots.Where(r => options.ScanRootIds.Contains(r.Id)).ToList();
            }

            if (enabledRoots.Count == 0)
            {
                _logger.LogInformation("No active scan roots configured. Import finished with 0 items.");
                result.Duration = stopwatch.Elapsed;
                return result;
            }

            // 4. Track processed files across all roots to avoid double-processing overlapping roots
            var seenFilePathsThisSession = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var root in enabledRoots)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    result.IsCancelled = true;
                    _logger.LogInformation("Library import was cancelled before scanning root '{RootPath}'.", root.Path);
                    break;
                }

                // Verify physical drive readiness and directory accessibility (CRITICAL DRIVE SAFETY: safeguard against disconnected drives)
                var driveRoot = Path.GetPathRoot(root.Path);
                bool isDriveReady = true;
                if (!string.IsNullOrEmpty(driveRoot))
                {
                    try
                    {
                        var driveInfo = new DriveInfo(driveRoot);
                        isDriveReady = driveInfo.IsReady;
                    }
                    catch
                    {
                        // In case querying drive properties fails, rely on Directory.Exists
                    }
                }

                if (!isDriveReady || !Directory.Exists(root.Path))
                {
                    _logger.LogWarning("Configured library root '{RootPath}' is currently inaccessible (DriveReady={DriveReady}). Skipping root without modifying records to safeguard offline drives.", root.Path, isDriveReady);
                    result.Errors.Add(new ScanError(root.Path, isDriveReady ? "Directory is not accessible on disk." : "Drive is offline or disconnected.", nameof(DirectoryNotFoundException)));
                    continue;
                }

                progress?.Report(new LibraryImportProgress
                {
                    CurrentRoot = root.Path,
                    StatusMessage = $"Scanning directory: {root.Path}"
                });

                // Scan files in this root
                var scanOptions = new MediaScanOptions
                {
                    RootDirectories = new[] { root.Path }
                };

                var scanResult = await _scannerService.ScanAsync(scanOptions, null, cancellationToken).ConfigureAwait(false);

                if (scanResult.Errors != null && scanResult.Errors.Count > 0)
                {
                    result.Errors.AddRange(scanResult.Errors);
                }

                if (cancellationToken.IsCancellationRequested || scanResult.WasCancelled)
                {
                    result.IsCancelled = true;
                    _logger.LogInformation("Library import was cancelled during root scan for '{RootPath}'.", root.Path);
                    break;
                }

                var seenInThisRoot = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var totalFilesInRoot = scanResult.ScannedFiles.Count;
                var processedInRoot = 0;

                // Pre-fetch existing media files in this root to detect moved/renamed files
                var rootExistingFiles = await _mediaFileRepository.GetByPathPrefixAsync(root.NormalizedPath).ConfigureAwait(false);
                var existingByPath = rootExistingFiles.ToDictionary(f => f.FilePath, StringComparer.OrdinalIgnoreCase);
                var unlocatedCandidates = rootExistingFiles.Where(f => !File.Exists(f.FilePath)).ToList();

                // Process discovered media files
                foreach (var file in scanResult.ScannedFiles)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        result.IsCancelled = true;
                        break;
                    }

                    var normalizedPath = Path.GetFullPath(file.FilePath);

                    // Check persistent user library exclusion
                    if (_exclusionRepository != null && await _exclusionRepository.IsPathExcludedAsync(normalizedPath).ConfigureAwait(false))
                    {
                        _logger.LogDebug("Skipping excluded media file '{FilePath}'", normalizedPath);
                        result.FilesSkipped++;
                        continue;
                    }

                    seenInThisRoot.Add(normalizedPath);

                    // Skip duplicate processing if already discovered through an ancestor/overlapping root
                    if (!seenFilePathsThisSession.Add(normalizedPath))
                    {
                        result.DuplicateFiles++;
                        result.FilesSkipped++;
                        continue;
                    }

                    result.FilesDiscovered++;
                    processedInRoot++;

                    progress?.Report(new LibraryImportProgress
                    {
                        CurrentRoot = root.Path,
                        CurrentFile = file.FileName,
                        FilesDiscovered = result.FilesDiscovered,
                        FilesProcessed = processedInRoot,
                        TotalFiles = totalFilesInRoot,
                        StatusMessage = $"Processing file {processedInRoot}/{totalFilesInRoot}: {file.FileName}"
                    });

                    try
                    {
                        // Check if this newly located file corresponds to a moved or renamed media file
                        if (!existingByPath.ContainsKey(normalizedPath))
                        {
                            var matchedCandidate = unlocatedCandidates.FirstOrDefault(c =>
                                c.SizeBytes == file.SizeBytes &&
                                c.FileModifiedAt.HasValue &&
                                Math.Abs((c.FileModifiedAt.Value - file.LastModifiedUtc).TotalSeconds) < 2);

                            if (matchedCandidate != null)
                            {
                                unlocatedCandidates.Remove(matchedCandidate);
                                var oldPath = matchedCandidate.FilePath;
                                matchedCandidate.FilePath = normalizedPath;
                                matchedCandidate.FileName = file.FileName;
                                matchedCandidate.IsMissing = false;
                                matchedCandidate.LastScannedAt = DateTime.UtcNow;

                                var newDir = Path.GetDirectoryName(file.FilePath);
                                var parsed = _parserEngine.Parse(file.FileName, newDir);
                                RecordClassification(parsed, file.FileName, result);

                                await EnsureEntityAssociationAsync(parsed, matchedCandidate, result, isUpdate: true).ConfigureAwait(false);
                                await _mediaFileRepository.UpdateAsync(matchedCandidate).ConfigureAwait(false);

                                result.FilesMoved++;
                                result.FilesUpdated++;
                                _logger.LogInformation("Detected renamed or moved media file under root '{RootPath}': '{OldPath}' -> '{NewPath}'. Preserved entity ID {Id}.",
                                    root.NormalizedPath, oldPath, normalizedPath, matchedCandidate.Id);
                                continue;
                            }
                        }

                        await ProcessMediaFileAsync(file, normalizedPath, root, options, result, cancellationToken).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process media file: {FilePath}", normalizedPath);
                        result.Errors.Add(new ScanError(normalizedPath, ex.Message, ex.GetType().Name));
                    }
                }

                if (result.IsCancelled || cancellationToken.IsCancellationRequested)
                {
                    result.IsCancelled = true;
                    break;
                }

                // Root scan completed cleanly: reconcile missing files for this root only
                await ReconcileMissingFilesForRootAsync(root, seenInThisRoot, result, cancellationToken).ConfigureAwait(false);
                await _scanRootService.UpdateLastScannedAsync(root.Id, DateTime.UtcNow, cancellationToken).ConfigureAwait(false);
            }

            result.Duration = stopwatch.Elapsed;
            _logger.LogInformation("{DiagnosticSummary}", result.FormatDiagnosticSummary());

            if (_artworkPipelineService != null && !result.IsCancelled && (result.FilesImported > 0 || result.FilesUpdated > 0))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _artworkPipelineService.ProcessPendingLibraryArtworkAsync(cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Background artwork pipeline run encountered an error after library import.");
                    }
                });
            }

            ImportCompleted?.Invoke(this, result);
            return result;
        }
        catch (OperationCanceledException)
        {
            result.IsCancelled = true;
            result.Duration = stopwatch.Elapsed;
            _logger.LogInformation("Library import was cancelled by request.");
            return result;
        }
        finally
        {
            _isImporting = false;
            _importLock.Release();
        }
    }

    private static void RecordClassification(ParsedMediaInfo parsed, string fileName, LibraryImportResult result)
    {
        if (parsed.Classification == MediaClassification.Movie)
        {
            result.MoviesClassified++;
        }
        else if (parsed.Classification == MediaClassification.TvEpisode || parsed.Classification == MediaClassification.TvSeason)
        {
            result.TvEpisodesClassified++;
        }
        else
        {
            result.UnknownFiles++;
            result.Diagnostics.Add(new ImportDiagnosticItem(
                fileName: fileName,
                classification: parsed.Classification.ToString(),
                reason: parsed.DiagnosticReason ?? "Non-feature or unclassified media"
            ));
        }
    }

    private async Task ProcessMediaFileAsync(
        ScannedMediaFile file,
        string normalizedPath,
        ScanRoot root,
        LibraryImportOptions? options,
        LibraryImportResult result,
        CancellationToken cancellationToken)
    {
        var existingMediaFile = await _mediaFileRepository.GetByPathAsync(normalizedPath).ConfigureAwait(false);

        if (existingMediaFile != null)
        {
            var isSizeMatch = existingMediaFile.SizeBytes == file.SizeBytes;
            var isModifiedMatch = existingMediaFile.FileModifiedAt.HasValue &&
                Math.Abs((existingMediaFile.FileModifiedAt.Value - file.LastModifiedUtc).TotalSeconds) < 1;
            var forceReparse = options?.ForceReparse == true;
            var isCurrentlyUnlinked = !existingMediaFile.MovieId.HasValue && !existingMediaFile.EpisodeId.HasValue;

            if (isSizeMatch && isModifiedMatch && !forceReparse && !isCurrentlyUnlinked)
            {
                // File content unchanged and already linked to an entity
                if (existingMediaFile.IsMissing)
                {
                    existingMediaFile.IsMissing = false;
                    existingMediaFile.LastScannedAt = DateTime.UtcNow;
                    await _mediaFileRepository.UpdateAsync(existingMediaFile).ConfigureAwait(false);
                }
                else
                {
                    existingMediaFile.LastScannedAt = DateTime.UtcNow;
                    await _mediaFileRepository.UpdateAsync(existingMediaFile).ConfigureAwait(false);
                }

                result.FilesUnchanged++;
                return;
            }

            // File modified on disk OR previously unlinked: re-evaluate classification
            existingMediaFile.SizeBytes = file.SizeBytes;
            existingMediaFile.FileModifiedAt = file.LastModifiedUtc;
            existingMediaFile.LastScannedAt = DateTime.UtcNow;
            existingMediaFile.IsMissing = false;

            var directoryPath = Path.GetDirectoryName(file.FilePath);
            var parsed = _parserEngine.Parse(file.FileName, directoryPath);
            RecordClassification(parsed, file.FileName, result);

            await EnsureEntityAssociationAsync(parsed, existingMediaFile, result, isUpdate: true).ConfigureAwait(false);

            await _mediaFileRepository.UpdateAsync(existingMediaFile).ConfigureAwait(false);
            result.FilesUpdated++;
            return;
        }

        // New media file discovered
        var newDirectoryPath = Path.GetDirectoryName(file.FilePath);
        var parsedInfo = _parserEngine.Parse(file.FileName, newDirectoryPath);
        RecordClassification(parsedInfo, file.FileName, result);

        var newMediaFile = new MediaFile
        {
            ScanRootId = root.Id,
            FilePath = normalizedPath,
            FileName = file.FileName,
            FileExtension = file.FileExtension,
            SizeBytes = file.SizeBytes,
            FileCreatedAt = file.CreatedUtc,
            FileModifiedAt = file.LastModifiedUtc,
            LastScannedAt = DateTime.UtcNow,
            IsMissing = false
        };

        await EnsureEntityAssociationAsync(parsedInfo, newMediaFile, result, isUpdate: false).ConfigureAwait(false);
        await _mediaFileRepository.InsertAsync(newMediaFile).ConfigureAwait(false);
        result.FilesImported++;
    }

    private async Task EnsureEntityAssociationAsync(
        ParsedMediaInfo parsed,
        MediaFile mediaFile,
        LibraryImportResult result,
        bool isUpdate)
    {
        if (parsed.Classification == MediaClassification.Movie)
        {
            // B-03: Identical filenames on different drives must remain distinct.
            // Match against candidates that either belong to this scan root or have no files yet.
            var candidateMovies = await _movieRepository.FindAllByTitleAndYearAsync(parsed.Title, parsed.Year).ConfigureAwait(false);
            Movie? movie = null;

            foreach (var cand in candidateMovies)
            {
                var existingFiles = await _mediaFileRepository.GetByMovieIdAsync(cand.Id).ConfigureAwait(false);
                if (existingFiles.Count == 0 || existingFiles.Any(f => f.ScanRootId == mediaFile.ScanRootId))
                {
                    movie = cand;
                    break;
                }
            }

            if (movie == null)
            {
                movie = new Movie
                {
                    Title = parsed.Title,
                    ReleaseYear = parsed.Year,
                    Edition = parsed.Edition,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _movieRepository.InsertAsync(movie).ConfigureAwait(false);
                result.MoviesCreated++;
            }
            else if (isUpdate)
            {
                bool movieUpdated = false;
                if (!string.IsNullOrWhiteSpace(parsed.Edition) && movie.Edition != parsed.Edition)
                {
                    movie.Edition = parsed.Edition;
                    movieUpdated = true;
                }
                if (parsed.Year.HasValue && movie.ReleaseYear != parsed.Year)
                {
                    movie.ReleaseYear = parsed.Year;
                    movieUpdated = true;
                }
                if (movieUpdated)
                {
                    movie.UpdatedAt = DateTime.UtcNow;
                    await _movieRepository.UpdateAsync(movie).ConfigureAwait(false);
                    result.MoviesUpdated++;
                }
            }

            mediaFile.MovieId = movie.Id;
            mediaFile.EpisodeId = null;
        }
        else if (parsed.Classification == MediaClassification.TvEpisode)
        {
            var series = await _seriesRepository.FindByTitleAsync(parsed.Title).ConfigureAwait(false);
            if (series == null)
            {
                series = new Series
                {
                    Title = parsed.Title,
                    StartYear = parsed.Year,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _seriesRepository.InsertAsync(series).ConfigureAwait(false);
                result.SeriesCreated++;
            }
            else if (series.StartYear == null && parsed.Year.HasValue)
            {
                series.StartYear = parsed.Year;
                series.UpdatedAt = DateTime.UtcNow;
                await _seriesRepository.UpdateAsync(series).ConfigureAwait(false);
            }

            var seasonNum = parsed.SeasonNumber ?? 1;
            var season = await _seasonRepository.GetBySeriesAndNumberAsync(series.Id, seasonNum).ConfigureAwait(false);
            if (season == null)
            {
                season = new Season
                {
                    SeriesId = series.Id,
                    SeasonNumber = seasonNum,
                    Title = seasonNum == 0 ? "Specials" : $"Season {seasonNum}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _seasonRepository.InsertAsync(season).ConfigureAwait(false);
            }

            var epNum = parsed.EpisodeNumber ?? 1;
            var episode = await _episodeRepository.GetBySeasonAndNumberAsync(season.Id, epNum).ConfigureAwait(false);
            if (episode == null)
            {
                episode = new Episode
                {
                    SeriesId = series.Id,
                    SeasonId = season.Id,
                    EpisodeNumber = epNum,
                    Title = parsed.EpisodeTitle ?? $"Episode {epNum}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                await _episodeRepository.InsertAsync(episode).ConfigureAwait(false);
                result.EpisodesCreated++;
            }
            else if (!string.IsNullOrWhiteSpace(parsed.EpisodeTitle) &&
                     (string.IsNullOrWhiteSpace(episode.Title) || episode.Title.Equals($"Episode {epNum}", StringComparison.OrdinalIgnoreCase)))
            {
                episode.Title = parsed.EpisodeTitle;
                episode.UpdatedAt = DateTime.UtcNow;
                await _episodeRepository.UpdateAsync(episode).ConfigureAwait(false);
            }

            mediaFile.MovieId = null;
            mediaFile.EpisodeId = episode.Id;

            // If multi-episode, ensure subsequent episodes in the sequence exist in database
            if (parsed.IsMultiEpisode && parsed.EndEpisodeNumber.HasValue)
            {
                for (int e = epNum + 1; e <= parsed.EndEpisodeNumber.Value; e++)
                {
                    var extraEp = await _episodeRepository.GetBySeasonAndNumberAsync(season.Id, e).ConfigureAwait(false);
                    if (extraEp == null)
                    {
                        extraEp = new Episode
                        {
                            SeriesId = series.Id,
                            SeasonId = season.Id,
                            EpisodeNumber = e,
                            Title = $"Episode {e}",
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow
                        };
                        await _episodeRepository.InsertAsync(extraEp).ConfigureAwait(false);
                        result.EpisodesCreated++;
                    }
                }
            }
        }
        else
        {
            // Unknown or ambiguous media file: preserve in MediaFiles with null entity links
            mediaFile.MovieId = null;
            mediaFile.EpisodeId = null;
        }
    }

    private async Task ReconcileMissingFilesForRootAsync(
        ScanRoot root,
        HashSet<string> seenInThisRoot,
        LibraryImportResult result,
        CancellationToken cancellationToken)
    {
        var existingFiles = await _mediaFileRepository.GetByPathPrefixAsync(root.NormalizedPath).ConfigureAwait(false);

        foreach (var existing in existingFiles)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            if (_exclusionRepository != null && await _exclusionRepository.IsPathExcludedAsync(existing.FilePath).ConfigureAwait(false))
            {
                // File was explicitly excluded by user; do not mark missing
                continue;
            }

            var isDiscovered = seenInThisRoot.Contains(existing.FilePath);

            if (!isDiscovered)
            {
                if (!existing.IsMissing)
                {
                    existing.IsMissing = true;
                    await _mediaFileRepository.UpdateAsync(existing).ConfigureAwait(false);
                    result.FilesMissing++;
                    _logger.LogInformation("File not found under root '{RootPath}' during completed scan: '{FilePath}'. Marked as missing.",
                        root.NormalizedPath, existing.FilePath);
                }
            }
            else
            {
                if (existing.IsMissing)
                {
                    existing.IsMissing = false;
                    await _mediaFileRepository.UpdateAsync(existing).ConfigureAwait(false);
                    _logger.LogInformation("Previously missing file was rediscovered under root '{RootPath}': '{FilePath}'. Restored to present.",
                        root.NormalizedPath, existing.FilePath);
                }
            }
        }
    }
}
