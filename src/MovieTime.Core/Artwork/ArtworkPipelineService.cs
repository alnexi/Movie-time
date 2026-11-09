using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Production coordinator for embedded artwork and adaptive video thumbnail extraction.
/// Integrates with SQLite repositories, manages deterministic caching, and notifies the UI on completion.
/// </summary>
public sealed class ArtworkPipelineService : IArtworkPipelineService
{
    private readonly IMovieRepository _movieRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly ISeriesRepository? _seriesRepository;
    private readonly ISeasonRepository? _seasonRepository;
    private readonly IEpisodeRepository? _episodeRepository;
    private readonly IEmbeddedArtworkExtractor _embeddedExtractor;
    private readonly IVideoFrameExtractor? _frameExtractor;
    private readonly IImageQualityEvaluator _qualityEvaluator;
    private readonly IArtworkStorageService _storageService;
    private readonly IImageProcessingService? _imageProcessor;
    private readonly ILogger<ArtworkPipelineService> _logger;

    private readonly SemaphoreSlim _batchLock = new(1, 1);

    public event Action<int, string?, string?>? ArtworkUpdated;
    public event Action<int, string?, string?>? SeriesArtworkUpdated;

    public ArtworkPipelineService(
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        IEmbeddedArtworkExtractor embeddedExtractor,
        IImageQualityEvaluator qualityEvaluator,
        IArtworkStorageService storageService,
        IVideoFrameExtractor? frameExtractor = null,
        IImageProcessingService? imageProcessor = null,
        ILogger<ArtworkPipelineService>? logger = null)
        : this(movieRepository, mediaFileRepository, embeddedExtractor, qualityEvaluator, storageService,
               frameExtractor, imageProcessor, null, null, null, logger)
    {
    }

    public ArtworkPipelineService(
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        IEmbeddedArtworkExtractor embeddedExtractor,
        IImageQualityEvaluator qualityEvaluator,
        IArtworkStorageService storageService,
        IVideoFrameExtractor? frameExtractor,
        IImageProcessingService? imageProcessor,
        ISeriesRepository? seriesRepository,
        ISeasonRepository? seasonRepository,
        IEpisodeRepository? episodeRepository,
        ILogger<ArtworkPipelineService>? logger = null)
    {
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _embeddedExtractor = embeddedExtractor ?? throw new ArgumentNullException(nameof(embeddedExtractor));
        _qualityEvaluator = qualityEvaluator ?? throw new ArgumentNullException(nameof(qualityEvaluator));
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _frameExtractor = frameExtractor;
        _imageProcessor = imageProcessor;
        _seriesRepository = seriesRepository;
        _seasonRepository = seasonRepository;
        _episodeRepository = episodeRepository;
        _logger = logger ?? NullLogger<ArtworkPipelineService>.Instance;
    }

    public async Task<ArtworkExtractionResult> ProcessMovieArtworkAsync(
        int movieId,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default)
    {
        var movie = await _movieRepository.GetByIdAsync(movieId).ConfigureAwait(false);
        if (movie == null)
        {
            return new ArtworkExtractionResult
            {
                MovieId = movieId,
                IsPosterFallback = true,
                IsBackdropFallback = true,
                StatusMessage = "Movie not found"
            };
        }

        var mediaFiles = await _mediaFileRepository.GetByMovieIdAsync(movieId).ConfigureAwait(false);
        var primaryFile = mediaFiles.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath))
                          ?? mediaFiles.FirstOrDefault();

        if (primaryFile == null || string.IsNullOrWhiteSpace(primaryFile.FilePath) || !File.Exists(primaryFile.FilePath))
        {
            _logger.LogWarning("No existing physical media file found on disk for Movie {Id} ('{Title}')", movie.Id, movie.Title);
            movie.IsPosterFallback = true;
            movie.IsBackdropFallback = true;
            movie.PosterPath = null;
            movie.BackdropPath = null;
            await _movieRepository.UpdateAsync(movie).ConfigureAwait(false);
            ArtworkUpdated?.Invoke(movie.Id, null, null);

            return new ArtworkExtractionResult
            {
                MovieId = movieId,
                IsPosterFallback = true,
                IsBackdropFallback = true,
                StatusMessage = "Physical media file missing on disk"
            };
        }

        var fileHash = _storageService.ComputeMediaHash(primaryFile.FilePath, primaryFile.SizeBytes, primaryFile.FileModifiedAt);

        // 1. Cache Reuse Check
        if (!forceRegenerate)
        {
            var hasCachedPoster = _storageService.HasValidPoster(fileHash, out var cachedPoster);
            var hasCachedBackdrop = _storageService.HasValidBackdrop(fileHash, out var cachedBackdrop);

            if (hasCachedPoster && hasCachedBackdrop)
            {
                var changed = false;
                if (movie.PosterPath != cachedPoster || movie.IsPosterFallback)
                {
                    movie.PosterPath = cachedPoster;
                    movie.IsPosterFallback = false;
                    changed = true;
                }
                if (movie.BackdropPath != cachedBackdrop || movie.IsBackdropFallback)
                {
                    movie.BackdropPath = cachedBackdrop;
                    movie.IsBackdropFallback = false;
                    changed = true;
                }

                if (changed)
                {
                    movie.UpdatedAt = DateTime.UtcNow;
                    await _movieRepository.UpdateAsync(movie).ConfigureAwait(false);
                    ArtworkUpdated?.Invoke(movie.Id, movie.PosterPath, movie.BackdropPath);
                }

                return new ArtworkExtractionResult
                {
                    MovieId = movieId,
                    PosterPath = cachedPoster,
                    BackdropPath = cachedBackdrop,
                    WasCached = true,
                    IsPosterFallback = false,
                    IsBackdropFallback = false,
                    StatusMessage = "Reused cached artwork"
                };
            }
        }

        string? resolvedPosterPath = null;
        string? resolvedBackdropPath = null;
        var hasEmbedded = false;

        // 2. Embedded Artwork Extraction (Tier 1)
        try
        {
            var embeddedBytes = await _embeddedExtractor.ExtractEmbeddedArtworkAsync(primaryFile.FilePath, cancellationToken).ConfigureAwait(false);
            if (embeddedBytes != null && embeddedBytes.Length > 0)
            {
                byte[] posterBytes = embeddedBytes;
                if (_imageProcessor != null)
                {
                    posterBytes = await _imageProcessor.CropAndScaleToPosterAsync(embeddedBytes, 600, 900, cancellationToken).ConfigureAwait(false);
                }

                if (posterBytes.Length > 0)
                {
                    resolvedPosterPath = await _storageService.SavePosterAsync(fileHash, posterBytes, cancellationToken).ConfigureAwait(false);
                    hasEmbedded = true;
                    _logger.LogInformation("Extracted genuine embedded poster for Movie {Id} ('{Title}')", movie.Id, movie.Title);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed during embedded artwork extraction for {Path}", primaryFile.FilePath);
        }

        // 3. Local Video Thumbnail Candidate Extraction (Tier 2)
        if (_frameExtractor != null && _imageProcessor != null)
        {
            try
            {
                var durationMs = primaryFile.DurationTicks > 0
                    ? primaryFile.DurationTicks / TimeSpan.TicksPerMillisecond
                    : 0L;

                var candidates = await _frameExtractor.ExtractCandidateFramesAsync(primaryFile.FilePath, durationMs, cancellationToken).ConfigureAwait(false);
                if (candidates.Count > 0)
                {
                    // Score all candidates
                    CandidateVideoFrame? bestCandidate = null;
                    var bestScore = -1.0;

                    foreach (var candidate in candidates)
                    {
                        byte[]? bgra = candidate.BgraPixels;
                        var w = candidate.Width;
                        var h = candidate.Height;

                        if (bgra == null && candidate.ImageBytes.Length > 0)
                        {
                            var decoded = await _imageProcessor.DecodeToBgraAsync(candidate.ImageBytes, cancellationToken).ConfigureAwait(false);
                            w = decoded.width;
                            h = decoded.height;
                            bgra = decoded.bgraPixels;
                        }

                        if (bgra != null && w > 0 && h > 0)
                        {
                            var score = _qualityEvaluator.Evaluate(bgra, w, h);
                            candidate.QualityScore = score;
                            _logger.LogDebug("Candidate at {Time}: {Score}", candidate.Timestamp, score);

                            if (score.OverallScore > bestScore)
                            {
                                bestScore = score.OverallScore;
                                bestCandidate = candidate;
                            }

                            // Early exit if high quality threshold is achieved
                            if (score.IsAcceptable)
                            {
                                bestCandidate = candidate;
                                break;
                            }
                        }
                    }

                    if (bestCandidate != null && (bestCandidate.QualityScore == null || bestCandidate.QualityScore.IsUsable))
                    {
                        // Generate 16:9 Backdrop from candidate
                        var backdropBytes = await _imageProcessor.CropAndScaleToBackdropAsync(bestCandidate.ImageBytes, 1920, 1080, cancellationToken).ConfigureAwait(false);
                        if (backdropBytes.Length > 0)
                        {
                            resolvedBackdropPath = await _storageService.SaveBackdropAsync(fileHash, backdropBytes, cancellationToken).ConfigureAwait(false);
                            _logger.LogInformation("Generated 16:9 backdrop for Movie {Id} ('{Title}') from frame at {Time}",
                                movie.Id, movie.Title, bestCandidate.Timestamp);
                        }

                        // If no embedded poster was found, derive poster from top candidate
                        if (string.IsNullOrEmpty(resolvedPosterPath))
                        {
                            var derivedPoster = await _imageProcessor.CropAndScaleToPosterAsync(bestCandidate.ImageBytes, 600, 900, cancellationToken).ConfigureAwait(false);
                            if (derivedPoster.Length > 0)
                            {
                                resolvedPosterPath = await _storageService.SavePosterAsync(fileHash, derivedPoster, cancellationToken).ConfigureAwait(false);
                                _logger.LogInformation("Derived 2:3 poster for Movie {Id} ('{Title}') from candidate frame", movie.Id, movie.Title);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed during video thumbnail extraction for {Path}", primaryFile.FilePath);
            }
        }

        // 4. Update Database Entity State
        movie.PosterPath = resolvedPosterPath;
        movie.IsPosterFallback = string.IsNullOrEmpty(resolvedPosterPath);

        movie.BackdropPath = resolvedBackdropPath;
        movie.IsBackdropFallback = string.IsNullOrEmpty(resolvedBackdropPath);

        movie.UpdatedAt = DateTime.UtcNow;
        await _movieRepository.UpdateAsync(movie).ConfigureAwait(false);

        ArtworkUpdated?.Invoke(movie.Id, movie.PosterPath, movie.BackdropPath);

        return new ArtworkExtractionResult
        {
            MovieId = movieId,
            PosterPath = resolvedPosterPath,
            BackdropPath = resolvedBackdropPath,
            HasEmbeddedArtwork = hasEmbedded,
            IsPosterFallback = movie.IsPosterFallback,
            IsBackdropFallback = movie.IsBackdropFallback,
            WasCached = false,
            StatusMessage = hasEmbedded
                ? "Extracted embedded artwork and video backdrop"
                : resolvedPosterPath != null
                    ? "Generated video poster and backdrop"
                    : "Marked as typographical fallback"
        };
    }

    public async Task<ArtworkExtractionResult> ProcessSeriesArtworkAsync(
        int seriesId,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default)
    {
        if (_seriesRepository == null || _episodeRepository == null)
        {
            return new ArtworkExtractionResult
            {
                SeriesId = seriesId,
                IsPosterFallback = true,
                IsBackdropFallback = true,
                StatusMessage = "Repository not available"
            };
        }

        var series = await _seriesRepository.GetByIdAsync(seriesId).ConfigureAwait(false);
        if (series == null)
        {
            return new ArtworkExtractionResult
            {
                SeriesId = seriesId,
                IsPosterFallback = true,
                IsBackdropFallback = true,
                StatusMessage = "Series not found"
            };
        }

        var episodes = await _episodeRepository.GetBySeriesIdAsync(seriesId).ConfigureAwait(false);
        var orderedEpisodes = episodes
            .OrderBy(e => e.SeasonId)
            .ThenBy(e => e.EpisodeNumber)
            .ToList();

        MediaFile? primaryFile = null;
        foreach (var ep in orderedEpisodes)
        {
            var files = await _mediaFileRepository.GetByEpisodeIdAsync(ep.Id).ConfigureAwait(false);
            var file = files.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath));
            if (file != null)
            {
                primaryFile = file;
                break;
            }
        }

        if (primaryFile == null || string.IsNullOrWhiteSpace(primaryFile.FilePath) || !File.Exists(primaryFile.FilePath))
        {
            series.IsPosterFallback = true;
            series.IsBackdropFallback = true;
            series.PosterPath = null;
            series.BackdropPath = null;
            await _seriesRepository.UpdateAsync(series).ConfigureAwait(false);
            SeriesArtworkUpdated?.Invoke(series.Id, null, null);

            return new ArtworkExtractionResult
            {
                SeriesId = seriesId,
                IsPosterFallback = true,
                IsBackdropFallback = true,
                StatusMessage = "Physical media file missing on disk"
            };
        }

        var fileHash = _storageService.ComputeMediaHash(primaryFile.FilePath, primaryFile.SizeBytes, primaryFile.FileModifiedAt);

        // 1. Cache Reuse Check
        if (!forceRegenerate)
        {
            var hasCachedPoster = _storageService.HasValidPoster(fileHash, out var cachedPoster);
            var hasCachedBackdrop = _storageService.HasValidBackdrop(fileHash, out var cachedBackdrop);

            if (hasCachedPoster && hasCachedBackdrop)
            {
                series.PosterPath = cachedPoster;
                series.BackdropPath = cachedBackdrop;
                series.IsPosterFallback = false;
                series.IsBackdropFallback = false;
                series.UpdatedAt = DateTime.UtcNow;
                await _seriesRepository.UpdateAsync(series).ConfigureAwait(false);

                SeriesArtworkUpdated?.Invoke(series.Id, cachedPoster, cachedBackdrop);

                return new ArtworkExtractionResult
                {
                    SeriesId = seriesId,
                    PosterPath = cachedPoster,
                    BackdropPath = cachedBackdrop,
                    WasCached = true,
                    StatusMessage = "Loaded from disk cache"
                };
            }
        }

        string? posterPath = null;
        string? backdropPath = null;
        bool hasEmbedded = false;

        try
        {
            var embeddedBytes = await _embeddedExtractor.ExtractEmbeddedArtworkAsync(primaryFile.FilePath, cancellationToken).ConfigureAwait(false);
            if (embeddedBytes != null && embeddedBytes.Length > 0)
            {
                byte[] posterBytes = embeddedBytes;
                if (_imageProcessor != null)
                {
                    posterBytes = await _imageProcessor.CropAndScaleToPosterAsync(embeddedBytes, 600, 900, cancellationToken).ConfigureAwait(false);
                }

                if (posterBytes.Length > 0)
                {
                    posterPath = await _storageService.SavePosterAsync(fileHash, posterBytes, cancellationToken).ConfigureAwait(false);
                    hasEmbedded = true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract embedded artwork for series {SeriesId} file: {FilePath}", seriesId, primaryFile.FilePath);
        }

        if (_frameExtractor != null && _imageProcessor != null)
        {
            try
            {
                var durationMs = primaryFile.DurationTicks > 0
                    ? primaryFile.DurationTicks / TimeSpan.TicksPerMillisecond
                    : 0L;

                var candidates = await _frameExtractor.ExtractCandidateFramesAsync(primaryFile.FilePath, durationMs, cancellationToken).ConfigureAwait(false);
                if (candidates.Count > 0)
                {
                    CandidateVideoFrame? bestCandidate = null;
                    var bestScore = -1.0;

                    foreach (var candidate in candidates)
                    {
                        byte[]? bgra = candidate.BgraPixels;
                        var w = candidate.Width;
                        var h = candidate.Height;

                        if (bgra == null && candidate.ImageBytes.Length > 0)
                        {
                            var decoded = await _imageProcessor.DecodeToBgraAsync(candidate.ImageBytes, cancellationToken).ConfigureAwait(false);
                            w = decoded.width;
                            h = decoded.height;
                            bgra = decoded.bgraPixels;
                        }

                        if (bgra != null && w > 0 && h > 0)
                        {
                            var score = _qualityEvaluator.Evaluate(bgra, w, h);
                            candidate.QualityScore = score;

                            if (score.OverallScore > bestScore)
                            {
                                bestScore = score.OverallScore;
                                bestCandidate = candidate;
                            }

                            if (score.IsAcceptable)
                            {
                                bestCandidate = candidate;
                                break;
                            }
                        }
                    }

                    if (bestCandidate != null && (bestCandidate.QualityScore == null || bestCandidate.QualityScore.IsUsable))
                    {
                        var backdropBytes = await _imageProcessor.CropAndScaleToBackdropAsync(bestCandidate.ImageBytes, 1920, 1080, cancellationToken).ConfigureAwait(false);
                        if (backdropBytes.Length > 0)
                        {
                            backdropPath = await _storageService.SaveBackdropAsync(fileHash, backdropBytes, cancellationToken).ConfigureAwait(false);
                        }

                        if (string.IsNullOrEmpty(posterPath))
                        {
                            var derivedPoster = await _imageProcessor.CropAndScaleToPosterAsync(bestCandidate.ImageBytes, 600, 900, cancellationToken).ConfigureAwait(false);
                            if (derivedPoster.Length > 0)
                            {
                                posterPath = await _storageService.SavePosterAsync(fileHash, derivedPoster, cancellationToken).ConfigureAwait(false);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract video frame for series {SeriesId} file: {FilePath}", seriesId, primaryFile.FilePath);
            }
        }

        series.PosterPath = posterPath;
        series.BackdropPath = backdropPath;
        series.IsPosterFallback = string.IsNullOrEmpty(posterPath);
        series.IsBackdropFallback = string.IsNullOrEmpty(backdropPath);
        series.UpdatedAt = DateTime.UtcNow;

        await _seriesRepository.UpdateAsync(series).ConfigureAwait(false);
        SeriesArtworkUpdated?.Invoke(series.Id, posterPath, backdropPath);

        return new ArtworkExtractionResult
        {
            SeriesId = seriesId,
            PosterPath = posterPath,
            BackdropPath = backdropPath,
            HasEmbeddedArtwork = hasEmbedded,
            IsPosterFallback = series.IsPosterFallback,
            IsBackdropFallback = series.IsBackdropFallback,
            WasCached = false,
            StatusMessage = "Extracted successfully"
        };
    }

    public async Task<ArtworkExtractionResult> ProcessEpisodeArtworkAsync(
        int episodeId,
        bool forceRegenerate = false,
        CancellationToken cancellationToken = default)
    {
        if (_episodeRepository == null)
        {
            return new ArtworkExtractionResult
            {
                EpisodeId = episodeId,
                IsPosterFallback = true,
                StatusMessage = "Repository not available"
            };
        }

        var episode = await _episodeRepository.GetByIdAsync(episodeId).ConfigureAwait(false);
        if (episode == null)
        {
            return new ArtworkExtractionResult
            {
                EpisodeId = episodeId,
                IsPosterFallback = true,
                StatusMessage = "Episode not found"
            };
        }

        var mediaFiles = await _mediaFileRepository.GetByEpisodeIdAsync(episodeId).ConfigureAwait(false);
        var primaryFile = mediaFiles.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath))
                          ?? mediaFiles.FirstOrDefault();

        if (primaryFile == null || string.IsNullOrWhiteSpace(primaryFile.FilePath) || !File.Exists(primaryFile.FilePath))
        {
            episode.ThumbnailPath = null;
            episode.IsThumbnailFallback = true;
            await _episodeRepository.UpdateAsync(episode).ConfigureAwait(false);

            return new ArtworkExtractionResult
            {
                EpisodeId = episodeId,
                IsPosterFallback = true,
                StatusMessage = "Physical media file missing on disk"
            };
        }

        var fileHash = _storageService.ComputeMediaHash(primaryFile.FilePath, primaryFile.SizeBytes, primaryFile.FileModifiedAt);

        if (!forceRegenerate && _storageService.HasValidPoster(fileHash, out var cachedThumb))
        {
            episode.ThumbnailPath = cachedThumb;
            episode.IsThumbnailFallback = false;
            episode.UpdatedAt = DateTime.UtcNow;
            await _episodeRepository.UpdateAsync(episode).ConfigureAwait(false);

            return new ArtworkExtractionResult
            {
                EpisodeId = episodeId,
                PosterPath = cachedThumb,
                WasCached = true,
                StatusMessage = "Loaded from disk cache"
            };
        }

        string? thumbPath = null;
        bool hasEmbedded = false;

        try
        {
            var embeddedBytes = await _embeddedExtractor.ExtractEmbeddedArtworkAsync(primaryFile.FilePath, cancellationToken).ConfigureAwait(false);
            if (embeddedBytes != null && embeddedBytes.Length > 0)
            {
                byte[] finalBytes = embeddedBytes;
                if (_imageProcessor != null)
                {
                    finalBytes = await _imageProcessor.CropAndScaleToBackdropAsync(embeddedBytes, 640, 360, cancellationToken).ConfigureAwait(false);
                }

                if (finalBytes.Length > 0)
                {
                    hasEmbedded = true;
                    thumbPath = await _storageService.SavePosterAsync(fileHash, finalBytes, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract embedded artwork for episode {EpisodeId}", episodeId);
        }

        if (thumbPath == null && _frameExtractor != null && _imageProcessor != null)
        {
            try
            {
                var durationMs = primaryFile.DurationTicks > 0
                    ? primaryFile.DurationTicks / TimeSpan.TicksPerMillisecond
                    : 0L;

                var candidates = await _frameExtractor.ExtractCandidateFramesAsync(primaryFile.FilePath, durationMs, cancellationToken).ConfigureAwait(false);
                if (candidates.Count > 0)
                {
                    CandidateVideoFrame? bestCandidate = null;
                    var bestScore = -1.0;

                    foreach (var candidate in candidates)
                    {
                        byte[]? bgra = candidate.BgraPixels;
                        var w = candidate.Width;
                        var h = candidate.Height;

                        if (bgra == null && candidate.ImageBytes.Length > 0)
                        {
                            var decoded = await _imageProcessor.DecodeToBgraAsync(candidate.ImageBytes, cancellationToken).ConfigureAwait(false);
                            w = decoded.width;
                            h = decoded.height;
                            bgra = decoded.bgraPixels;
                        }

                        if (bgra != null && w > 0 && h > 0)
                        {
                            var score = _qualityEvaluator.Evaluate(bgra, w, h);
                            candidate.QualityScore = score;

                            if (score.OverallScore > bestScore)
                            {
                                bestScore = score.OverallScore;
                                bestCandidate = candidate;
                            }

                            if (score.IsAcceptable)
                            {
                                bestCandidate = candidate;
                                break;
                            }
                        }
                    }

                    if (bestCandidate != null && (bestCandidate.QualityScore == null || bestCandidate.QualityScore.IsUsable))
                    {
                        var thumbBytes = await _imageProcessor.CropAndScaleToBackdropAsync(bestCandidate.ImageBytes, 640, 360, cancellationToken).ConfigureAwait(false);
                        if (thumbBytes.Length > 0)
                        {
                            thumbPath = await _storageService.SavePosterAsync(fileHash, thumbBytes, cancellationToken).ConfigureAwait(false);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to extract video frame for episode {EpisodeId}", episodeId);
            }
        }

        episode.ThumbnailPath = thumbPath;
        episode.IsThumbnailFallback = string.IsNullOrEmpty(thumbPath);
        episode.UpdatedAt = DateTime.UtcNow;
        await _episodeRepository.UpdateAsync(episode).ConfigureAwait(false);

        return new ArtworkExtractionResult
        {
            EpisodeId = episodeId,
            PosterPath = thumbPath,
            HasEmbeddedArtwork = hasEmbedded,
            IsPosterFallback = episode.IsThumbnailFallback,
            StatusMessage = "Processed episode artwork"
        };
    }

    public async Task ProcessPendingLibraryArtworkAsync(
        IProgress<ArtworkPipelineProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (!await _batchLock.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            _logger.LogInformation("A batch artwork extraction operation is already in progress.");
            return;
        }

        try
        {
            var allMovies = await _movieRepository.GetAllAsync().ConfigureAwait(false);
            var pendingMovies = allMovies.Where(m =>
                string.IsNullOrEmpty(m.PosterPath) ||
                string.IsNullOrEmpty(m.BackdropPath) ||
                m.IsPosterFallback ||
                m.IsBackdropFallback ||
                !File.Exists(m.PosterPath) ||
                !File.Exists(m.BackdropPath)).ToList();

            var total = pendingMovies.Count;
            var processed = 0;
            var extracted = 0;
            var fallback = 0;

            _logger.LogInformation("Starting batch artwork pipeline for {Count} movies", total);

            foreach (var movie in pendingMovies)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress?.Report(new ArtworkPipelineProgress(total, processed, extracted, fallback, movie.Title));

                try
                {
                    var result = await ProcessMovieArtworkAsync(movie.Id, forceRegenerate: false, cancellationToken).ConfigureAwait(false);
                    if (result.HasAnyArtwork)
                    {
                        extracted++;
                    }
                    else
                    {
                        fallback++;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Unexpected error extracting artwork for movie {Id} ('{Title}')", movie.Id, movie.Title);
                    fallback++;
                }

                processed++;
                progress?.Report(new ArtworkPipelineProgress(total, processed, extracted, fallback, movie.Title));
            }

            if (_seriesRepository != null)
            {
                try
                {
                    var allSeries = await _seriesRepository.GetAllAsync().ConfigureAwait(false);
                    var pendingSeries = allSeries.Where(s =>
                        string.IsNullOrEmpty(s.PosterPath) ||
                        string.IsNullOrEmpty(s.BackdropPath) ||
                        s.IsPosterFallback ||
                        s.IsBackdropFallback ||
                        !File.Exists(s.PosterPath) ||
                        !File.Exists(s.BackdropPath)).ToList();

                    foreach (var show in pendingSeries)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        try
                        {
                            await ProcessSeriesArtworkAsync(show.Id, forceRegenerate: false, cancellationToken).ConfigureAwait(false);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to process artwork for series {Id} ({Title})", show.Id, show.Title);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing pending series artwork.");
                }
            }

            if (_episodeRepository != null)
            {
                try
                {
                    var allEpisodes = await _episodeRepository.GetBySeriesIdAsync(0).ConfigureAwait(false); // or iterate per series
                    // Or retrieve all series and get episodes
                    var allSeries = await (_seriesRepository?.GetAllAsync() ?? Task.FromResult(new System.Collections.Generic.List<Series>())).ConfigureAwait(false);
                    foreach (var s in allSeries)
                    {
                        if (cancellationToken.IsCancellationRequested) break;
                        var eps = await _episodeRepository.GetBySeriesIdAsync(s.Id).ConfigureAwait(false);
                        foreach (var ep in eps.Where(e => string.IsNullOrEmpty(e.ThumbnailPath) || e.IsThumbnailFallback || !File.Exists(e.ThumbnailPath)))
                        {
                            if (cancellationToken.IsCancellationRequested) break;
                            try
                            {
                                await ProcessEpisodeArtworkAsync(ep.Id, forceRegenerate: false, cancellationToken).ConfigureAwait(false);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to process artwork for episode {Id}", ep.Id);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error processing pending episode thumbnails.");
                }
            }

            _logger.LogInformation("Completed batch artwork pipeline. Processed: {P}/{Total}, Extracted: {Ext}, Fallbacks: {Fb}",
                processed, total, extracted, fallback);
        }
        finally
        {
            _batchLock.Release();
        }
    }
}
