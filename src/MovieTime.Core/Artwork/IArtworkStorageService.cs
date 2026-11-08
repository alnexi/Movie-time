using System;
using System.Threading;
using System.Threading.Tasks;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Manages deterministic local artwork caching, path resolution, and atomic file persistence under %LocalAppData%\MovieTime.
/// Guarantees that user media folders are never touched or modified.
/// </summary>
public interface IArtworkStorageService
{
    string ComputeMediaHash(string filePath, long sizeBytes, DateTime? modifiedUtc);
    string GetPosterPath(string fileHash);
    string GetBackdropPath(string fileHash);
    string GetThumbnailPath(string fileHash);

    bool HasValidPoster(string fileHash, out string existingPath);
    bool HasValidBackdrop(string fileHash, out string existingPath);
    bool HasValidThumbnail(string fileHash, out string existingPath);

    Task<string> SavePosterAsync(string fileHash, byte[] jpegBytes, CancellationToken ct = default);
    Task<string> SaveBackdropAsync(string fileHash, byte[] jpegBytes, CancellationToken ct = default);
    Task<string> SaveThumbnailAsync(string fileHash, byte[] jpegBytes, CancellationToken ct = default);
}
