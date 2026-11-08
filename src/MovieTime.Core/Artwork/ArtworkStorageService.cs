using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Services;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Production artwork storage service implementing collision-safe hash keys,
/// atomic file writes, and local %LocalAppData% caching.
/// </summary>
public sealed class ArtworkStorageService : IArtworkStorageService
{
    private readonly IStoragePathService _storagePathService;
    private readonly ILogger<ArtworkStorageService> _logger;
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _fileLocks = new();

    public ArtworkStorageService(
        IStoragePathService storagePathService,
        ILogger<ArtworkStorageService>? logger = null)
    {
        _storagePathService = storagePathService ?? throw new ArgumentNullException(nameof(storagePathService));
        _logger = logger ?? NullLogger<ArtworkStorageService>.Instance;
        _storagePathService.EnsureDirectoriesCreated();
    }

    public string ComputeMediaHash(string filePath, long sizeBytes, DateTime? modifiedUtc)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("Media file path cannot be empty.", nameof(filePath));
        }

        var normalized = Path.GetFullPath(filePath).ToUpperInvariant();
        long seconds = 0L;
        if (modifiedUtc.HasValue)
        {
            var dt = modifiedUtc.Value;
            var utc = dt.Kind switch
            {
                DateTimeKind.Utc => dt,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(dt, DateTimeKind.Utc),
                _ => dt.ToUniversalTime()
            };
            seconds = new DateTimeOffset(utc).ToUnixTimeSeconds();
        }

        var rawKey = $"{normalized}|{sizeBytes}|{seconds}";

        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(rawKey));
        return Convert.ToHexString(hashBytes)[..16].ToLowerInvariant();
    }

    public string GetPosterPath(string fileHash) =>
        Path.Combine(_storagePathService.PostersDirectory, $"poster_{fileHash}.jpg");

    public string GetBackdropPath(string fileHash) =>
        Path.Combine(_storagePathService.BackdropsDirectory, $"backdrop_{fileHash}.jpg");

    public string GetThumbnailPath(string fileHash) =>
        Path.Combine(_storagePathService.ThumbnailsDirectory, $"thumb_{fileHash}.jpg");

    public string GetPositionThumbnailPath(string fileHash, long positionSeconds) =>
        Path.Combine(_storagePathService.ThumbnailsDirectory, $"pos_{fileHash}_{positionSeconds}.jpg");

    public string GetPreviewFramePath(string fileHash, long positionSeconds, int frameIndex) =>
        Path.Combine(_storagePathService.PreviewsDirectory, $"prev_{fileHash}_{positionSeconds}_{frameIndex}.jpg");

    public bool HasValidPoster(string fileHash, out string existingPath) =>
        CheckValidFile(GetPosterPath(fileHash), out existingPath);

    public bool HasValidBackdrop(string fileHash, out string existingPath) =>
        CheckValidFile(GetBackdropPath(fileHash), out existingPath);

    public bool HasValidThumbnail(string fileHash, out string existingPath) =>
        CheckValidFile(GetThumbnailPath(fileHash), out existingPath);

    public bool HasValidPositionThumbnail(string fileHash, long positionSeconds, out string existingPath) =>
        CheckValidFile(GetPositionThumbnailPath(fileHash, positionSeconds), out existingPath);

    public bool HasValidPreviewFrame(string fileHash, long positionSeconds, int frameIndex, out string existingPath) =>
        CheckValidFile(GetPreviewFramePath(fileHash, positionSeconds, frameIndex), out existingPath);

    public Task<string> SavePosterAsync(string fileHash, byte[] jpegBytes, CancellationToken ct = default) =>
        SaveAtomicAsync(GetPosterPath(fileHash), jpegBytes, ct);

    public Task<string> SaveBackdropAsync(string fileHash, byte[] jpegBytes, CancellationToken ct = default) =>
        SaveAtomicAsync(GetBackdropPath(fileHash), jpegBytes, ct);

    public Task<string> SaveThumbnailAsync(string fileHash, byte[] jpegBytes, CancellationToken ct = default) =>
        SaveAtomicAsync(GetThumbnailPath(fileHash), jpegBytes, ct);

    public Task<string> SavePositionThumbnailAsync(string fileHash, long positionSeconds, byte[] jpegBytes, CancellationToken ct = default) =>
        SaveAtomicAsync(GetPositionThumbnailPath(fileHash, positionSeconds), jpegBytes, ct);

    public Task<string> SavePreviewFrameAsync(string fileHash, long positionSeconds, int frameIndex, byte[] jpegBytes, CancellationToken ct = default) =>
        SaveAtomicAsync(GetPreviewFramePath(fileHash, positionSeconds, frameIndex), jpegBytes, ct);

    private bool CheckValidFile(string path, out string validPath)
    {
        validPath = path;
        try
        {
            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                if (fi.Length > 256)
                {
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to verify cache file at {Path}", path);
        }

        return false;
    }

    private async Task<string> SaveAtomicAsync(string targetPath, byte[] data, CancellationToken ct)
    {
        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Cannot save empty artwork buffer.", nameof(data));
        }

        var dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var fileLock = _fileLocks.GetOrAdd(targetPath, _ => new SemaphoreSlim(1, 1));
        await fileLock.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            // If another task already wrote it while waiting, reuse it
            if (CheckValidFile(targetPath, out _))
            {
                return targetPath;
            }

            var tempPath = Path.Combine(_storagePathService.TempDirectory, $"art_{Guid.NewGuid():N}.tmp");
            try
            {
                await File.WriteAllBytesAsync(tempPath, data, ct).ConfigureAwait(false);

                // Atomic move to target
                File.Move(tempPath, targetPath, overwrite: true);
                _logger.LogInformation("Saved artwork cache to {Path} ({Bytes} bytes)", targetPath, data.Length);
                return targetPath;
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    try { File.Delete(tempPath); } catch { /* best effort */ }
                }
            }
        }
        finally
        {
            fileLock.Release();
        }
    }
}
