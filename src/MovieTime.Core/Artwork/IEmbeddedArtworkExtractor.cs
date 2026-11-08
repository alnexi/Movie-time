using System.Threading;
using System.Threading.Tasks;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Detects and extracts genuine embedded cover artwork from physical media files (MP4/M4V, MKV/WebM, etc.).
/// Guarantees that only valid, non-corrupted image payloads are returned.
/// </summary>
public interface IEmbeddedArtworkExtractor
{
    Task<byte[]?> ExtractEmbeddedArtworkAsync(string filePath, CancellationToken cancellationToken = default);
}
