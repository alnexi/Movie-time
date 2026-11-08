using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Production service coordinating on-demand extraction and disk caching of
/// genuine playback-position thumbnails and multi-frame hover previews.
/// </summary>
public sealed class HoverPreviewService : IHoverPreviewService
{
    private readonly IArtworkStorageService _storageService;
    private readonly IVideoFrameExtractor _frameExtractor;
    private readonly ILogger<HoverPreviewService> _logger;

    public HoverPreviewService(
        IArtworkStorageService storageService,
        IVideoFrameExtractor frameExtractor,
        ILogger<HoverPreviewService>? logger = null)
    {
        _storageService = storageService ?? throw new ArgumentNullException(nameof(storageService));
        _frameExtractor = frameExtractor ?? throw new ArgumentNullException(nameof(frameExtractor));
        _logger = logger ?? NullLogger<HoverPreviewService>.Instance;
    }

    public async Task<string?> GetOrExtractPositionThumbnailAsync(
        string filePath,
        long sizeBytes,
        DateTime? modifiedUtc,
        long positionSeconds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var fileHash = _storageService.ComputeMediaHash(filePath, sizeBytes, modifiedUtc);

        // 1. Check cache
        if (_storageService.HasValidPositionThumbnail(fileHash, positionSeconds, out var cachedPath))
        {
            return cachedPath;
        }

        // 2. Extract frame at exact position
        try
        {
            var targetTime = TimeSpan.FromSeconds(Math.Max(0, positionSeconds));
            var frame = await _frameExtractor.ExtractFrameAtTimeAsync(
                filePath,
                targetTime,
                targetWidth: 520,
                targetHeight: 292,
                cancellationToken).ConfigureAwait(false);

            if (frame != null && frame.ImageBytes.Length > 0)
            {
                var savedPath = await _storageService.SavePositionThumbnailAsync(
                    fileHash,
                    positionSeconds,
                    frame.ImageBytes,
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Saved position thumbnail for {Path} at {Seconds}s: {SavedPath}",
                    filePath, positionSeconds, savedPath);
                return savedPath;
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed extracting position thumbnail at {Pos}s for {Path}", positionSeconds, filePath);
        }

        return null;
    }

    public async Task<IReadOnlyList<string>> GetOrExtractPreviewFramesAsync(
        string filePath,
        long sizeBytes,
        DateTime? modifiedUtc,
        long positionSeconds,
        CancellationToken cancellationToken = default)
    {
        var resultPaths = new List<string>();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return resultPaths;
        }

        var fileHash = _storageService.ComputeMediaHash(filePath, sizeBytes, modifiedUtc);
        const int frameCount = 4;

        // 1. Check if all 4 preview frames are already cached on disk
        var allCached = true;
        for (int i = 0; i < frameCount; i++)
        {
            if (_storageService.HasValidPreviewFrame(fileHash, positionSeconds, i, out var cachedPath))
            {
                resultPaths.Add(cachedPath);
            }
            else
            {
                allCached = false;
                break;
            }
        }

        if (allCached && resultPaths.Count == frameCount)
        {
            return resultPaths;
        }

        resultPaths.Clear();

        // 2. Extract 4 candidate frames spaced 800ms apart around saved position
        try
        {
            var startTime = TimeSpan.FromSeconds(Math.Max(0, positionSeconds));
            var interval = TimeSpan.FromMilliseconds(800);

            var frames = await _frameExtractor.ExtractFramesAroundTimeAsync(
                filePath,
                startTime,
                frameCount,
                interval,
                targetWidth: 520,
                targetHeight: 292,
                cancellationToken).ConfigureAwait(false);

            for (int i = 0; i < frames.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frame = frames[i];
                if (frame.ImageBytes.Length > 0)
                {
                    var savedPath = await _storageService.SavePreviewFrameAsync(
                        fileHash,
                        positionSeconds,
                        i,
                        frame.ImageBytes,
                        cancellationToken).ConfigureAwait(false);

                    resultPaths.Add(savedPath);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed extracting preview frame set for {Path}", filePath);
        }

        return resultPaths;
    }
}
