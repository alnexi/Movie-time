using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Editing;
using Windows.Storage;
using Windows.Storage.FileProperties;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Artwork;

namespace MovieTime.App.Services;

/// <summary>
/// Hardware-accelerated native video thumbnail and candidate frame extractor
/// utilizing Windows Media Foundation and WinRT MediaComposition APIs.
/// </summary>
public sealed class WindowsVideoFrameExtractor : IVideoFrameExtractor
{
    private readonly ILogger<WindowsVideoFrameExtractor> _logger;

    public WindowsVideoFrameExtractor(ILogger<WindowsVideoFrameExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<WindowsVideoFrameExtractor>.Instance;
    }

    public async Task<IReadOnlyList<CandidateVideoFrame>> ExtractCandidateFramesAsync(
        string filePath,
        long durationMs,
        CancellationToken cancellationToken = default)
    {
        var frames = new List<CandidateVideoFrame>();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return frames;
        }

        StorageFile? storageFile = null;
        try
        {
            storageFile = await StorageFile.GetFileFromPathAsync(filePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve StorageFile for video path: {Path}", filePath);
            return frames;
        }

        // 1. Calculate Candidate Timestamps across the timeline
        var targetTimestamps = CalculateCandidateTimestamps(durationMs);

        // 2. Attempt multi-timestamp extraction using MediaComposition
        try
        {
            var clip = await MediaClip.CreateFromFileAsync(storageFile);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var clipDurationMs = clip.OriginalDuration.TotalMilliseconds;
            if (clipDurationMs > 1000 && (durationMs <= 0 || Math.Abs(durationMs - clipDurationMs) > 10000))
            {
                targetTimestamps = CalculateCandidateTimestamps((long)clipDurationMs);
            }

            foreach (var time in targetTimestamps)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Target 1280x720 for fast frame decoding and analysis
                    var imageStream = await composition.GetThumbnailAsync(
                        time,
                        1280,
                        720,
                        VideoFramePrecision.NearestKeyFrame);

                    if (imageStream != null && imageStream.Size > 1024)
                    {
                        var bytes = new byte[imageStream.Size];
                        using var reader = new Windows.Storage.Streams.DataReader(imageStream.GetInputStreamAt(0));
                        await reader.LoadAsync((uint)imageStream.Size);
                        reader.ReadBytes(bytes);

                        frames.Add(new CandidateVideoFrame
                        {
                            Timestamp = time,
                            ImageBytes = bytes,
                            Width = 1280,
                            Height = 720
                        });
                    }
                }
                catch (Exception exFrame)
                {
                    _logger.LogDebug(exFrame, "MediaComposition frame extraction failed at timestamp {Time}", time);
                }
            }
        }
        catch (Exception exComp)
        {
            _logger.LogDebug(exComp, "MediaComposition pipeline not available for container '{Path}'. Falling back to Shell thumbnail.", filePath);
        }

        // 3. Fallback: Native Windows Shell Video Thumbnail Provider
        if (frames.Count == 0)
        {
            try
            {
                var thumb = await storageFile.GetThumbnailAsync(
                    ThumbnailMode.VideosView,
                    1280,
                    ThumbnailOptions.UseCurrentScale);

                if (thumb != null && thumb.Size > 1024)
                {
                    var bytes = new byte[thumb.Size];
                    using var reader = new Windows.Storage.Streams.DataReader(thumb.GetInputStreamAt(0));
                    await reader.LoadAsync((uint)thumb.Size);
                    reader.ReadBytes(bytes);

                    frames.Add(new CandidateVideoFrame
                    {
                        Timestamp = TimeSpan.FromSeconds(60),
                        ImageBytes = bytes,
                        Width = 1280,
                        Height = 720
                    });

                    _logger.LogInformation("Extracted fallback video thumbnail via Windows Shell for {Path}", filePath);
                }
            }
            catch (Exception exThumb)
            {
                _logger.LogWarning(exThumb, "Windows Shell thumbnail extraction failed for {Path}", filePath);
            }
        }

        return frames;
    }

    public async Task<CandidateVideoFrame?> ExtractFrameAtTimeAsync(
        string filePath,
        TimeSpan timestamp,
        int targetWidth = 520,
        int targetHeight = 292,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            var clip = await MediaClip.CreateFromFileAsync(storageFile);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            // Clamp timestamp to clip duration
            var safeTime = timestamp;
            if (safeTime < TimeSpan.Zero) safeTime = TimeSpan.Zero;
            if (clip.OriginalDuration > TimeSpan.Zero && safeTime >= clip.OriginalDuration)
            {
                safeTime = TimeSpan.FromMilliseconds(Math.Max(0, clip.OriginalDuration.TotalMilliseconds - 1000));
            }

            var imageStream = await composition.GetThumbnailAsync(
                safeTime,
                targetWidth,
                targetHeight,
                VideoFramePrecision.NearestKeyFrame);

            if (imageStream != null && imageStream.Size > 1024)
            {
                var bytes = new byte[imageStream.Size];
                using var reader = new Windows.Storage.Streams.DataReader(imageStream.GetInputStreamAt(0));
                await reader.LoadAsync((uint)imageStream.Size);
                reader.ReadBytes(bytes);

                return new CandidateVideoFrame
                {
                    Timestamp = safeTime,
                    ImageBytes = bytes,
                    Width = targetWidth,
                    Height = targetHeight
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Direct frame extraction failed at {Time} for {Path}", timestamp, filePath);
        }

        return null;
    }

    public async Task<IReadOnlyList<CandidateVideoFrame>> ExtractFramesAroundTimeAsync(
        string filePath,
        TimeSpan startTime,
        int frameCount,
        TimeSpan interval,
        int targetWidth = 520,
        int targetHeight = 292,
        CancellationToken cancellationToken = default)
    {
        var frames = new List<CandidateVideoFrame>();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath) || frameCount <= 0)
        {
            return frames;
        }

        try
        {
            var storageFile = await StorageFile.GetFileFromPathAsync(filePath);
            var clip = await MediaClip.CreateFromFileAsync(storageFile);
            var composition = new MediaComposition();
            composition.Clips.Add(clip);

            var clipDuration = clip.OriginalDuration;

            for (int i = 0; i < frameCount; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var frameTime = startTime + TimeSpan.FromTicks(interval.Ticks * i);
                if (frameTime < TimeSpan.Zero) frameTime = TimeSpan.Zero;
                if (clipDuration > TimeSpan.Zero && frameTime >= clipDuration)
                {
                    frameTime = TimeSpan.FromMilliseconds(Math.Max(0, clipDuration.TotalMilliseconds - 1000));
                }

                try
                {
                    var imageStream = await composition.GetThumbnailAsync(
                        frameTime,
                        targetWidth,
                        targetHeight,
                        VideoFramePrecision.NearestKeyFrame);

                    if (imageStream != null && imageStream.Size > 1024)
                    {
                        var bytes = new byte[imageStream.Size];
                        using var reader = new Windows.Storage.Streams.DataReader(imageStream.GetInputStreamAt(0));
                        await reader.LoadAsync((uint)imageStream.Size);
                        reader.ReadBytes(bytes);

                        frames.Add(new CandidateVideoFrame
                        {
                            Timestamp = frameTime,
                            ImageBytes = bytes,
                            Width = targetWidth,
                            Height = targetHeight
                        });
                    }
                }
                catch (Exception exFrame)
                {
                    _logger.LogDebug(exFrame, "Failed extracting preview frame at {Time}", frameTime);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed extracting preview frame sequence for {Path}", filePath);
        }

        return frames;
    }

    private static List<TimeSpan> CalculateCandidateTimestamps(long durationMs)
    {
        var list = new List<TimeSpan>();

        if (durationMs > 30_000)
        {
            // 6 initial candidate percentages across timeline: 15%, 25%, 40%, 55%, 70%, 85%
            var percentages = new[] { 0.15, 0.25, 0.40, 0.55, 0.70, 0.85 };
            foreach (var p in percentages)
            {
                list.Add(TimeSpan.FromMilliseconds(durationMs * p));
            }
        }
        else
        {
            // Unknown or short duration fallback offsets
            var offsetsSeconds = new[] { 60, 180, 360, 600, 900, 1200 };
            foreach (var s in offsetsSeconds)
            {
                list.Add(TimeSpan.FromSeconds(s));
            }
        }

        return list;
    }
}
