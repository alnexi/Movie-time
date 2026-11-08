using System.Threading;
using System.Threading.Tasks;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Abstraction for scaling, cropping, and pixel decoding of images.
/// Enables platform-optimized hardware-accelerated processing in WinUI 3 (via WIC)
/// while maintaining decoupling in Core.
/// </summary>
public interface IImageProcessingService
{
    Task<byte[]> CropAndScaleToPosterAsync(byte[] sourceImageBytes, int targetWidth = 600, int targetHeight = 900, CancellationToken ct = default);
    Task<byte[]> CropAndScaleToBackdropAsync(byte[] sourceImageBytes, int targetWidth = 1920, int targetHeight = 1080, CancellationToken ct = default);
    Task<(int width, int height, byte[] bgraPixels)> DecodeToBgraAsync(byte[] sourceImageBytes, CancellationToken ct = default);
}
