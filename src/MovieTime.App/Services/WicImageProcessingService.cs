using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Artwork;

namespace MovieTime.App.Services;

/// <summary>
/// Hardware-accelerated image scaling, aspect-ratio cropping, and BGRA pixel decoding
/// powered by the native Windows Imaging Component (WIC) in WinUI 3.
/// </summary>
public sealed class WicImageProcessingService : IImageProcessingService
{
    private readonly ILogger<WicImageProcessingService> _logger;

    public WicImageProcessingService(ILogger<WicImageProcessingService>? logger = null)
    {
        _logger = logger ?? NullLogger<WicImageProcessingService>.Instance;
    }

    public async Task<(int width, int height, byte[] bgraPixels)> DecodeToBgraAsync(
        byte[] sourceImageBytes,
        CancellationToken ct = default)
    {
        if (sourceImageBytes == null || sourceImageBytes.Length == 0)
        {
            return (0, 0, Array.Empty<byte>());
        }

        try
        {
            using var inStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(inStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(sourceImageBytes);
                await writer.StoreAsync();
            }

            var decoder = await BitmapDecoder.CreateAsync(inStream);
            var width = (int)decoder.PixelWidth;
            var height = (int)decoder.PixelHeight;

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            return (width, height, pixelData.DetachPixelData());
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to decode image bytes to BGRA pixel buffer.");
            return (0, 0, Array.Empty<byte>());
        }
    }

    public async Task<byte[]> CropAndScaleToPosterAsync(
        byte[] sourceImageBytes,
        int targetWidth = 600,
        int targetHeight = 900,
        CancellationToken ct = default)
    {
        // 2:3 vertical aspect ratio
        return await ProcessImageWithAspectCropAsync(sourceImageBytes, 2.0 / 3.0, targetWidth, targetHeight, ct).ConfigureAwait(false);
    }

    public async Task<byte[]> CropAndScaleToBackdropAsync(
        byte[] sourceImageBytes,
        int targetWidth = 1920,
        int targetHeight = 1080,
        CancellationToken ct = default)
    {
        // 16:9 widescreen aspect ratio
        return await ProcessImageWithAspectCropAsync(sourceImageBytes, 16.0 / 9.0, targetWidth, targetHeight, ct).ConfigureAwait(false);
    }

    private async Task<byte[]> ProcessImageWithAspectCropAsync(
        byte[] sourceBytes,
        double targetAspect,
        int finalWidth,
        int finalHeight,
        CancellationToken ct)
    {
        if (sourceBytes == null || sourceBytes.Length == 0)
        {
            return Array.Empty<byte>();
        }

        try
        {
            using var inStream = new InMemoryRandomAccessStream();
            using (var writer = new DataWriter(inStream.GetOutputStreamAt(0)))
            {
                writer.WriteBytes(sourceBytes);
                await writer.StoreAsync();
            }

            var decoder = await BitmapDecoder.CreateAsync(inStream);
            var srcWidth = (double)decoder.PixelWidth;
            var srcHeight = (double)decoder.PixelHeight;

            if (srcWidth <= 0 || srcHeight <= 0)
            {
                return sourceBytes;
            }

            var srcAspect = srcWidth / srcHeight;

            uint cropX = 0;
            uint cropY = 0;
            uint cropW = (uint)srcWidth;
            uint cropH = (uint)srcHeight;

            if (srcAspect > targetAspect)
            {
                // Source is wider than target: center crop width
                cropW = (uint)Math.Round(srcHeight * targetAspect);
                cropX = (uint)Math.Max(0, (srcWidth - cropW) / 2.0);
            }
            else if (srcAspect < targetAspect)
            {
                // Source is taller than target: center crop height
                cropH = (uint)Math.Round(srcWidth / targetAspect);
                cropY = (uint)Math.Max(0, (srcHeight - cropH) / 2.0);
            }

            var transform = new BitmapTransform
            {
                Bounds = new BitmapBounds
                {
                    X = cropX,
                    Y = cropY,
                    Width = Math.Min(cropW, (uint)srcWidth - cropX),
                    Height = Math.Min(cropH, (uint)srcHeight - cropY)
                },
                ScaledWidth = (uint)finalWidth,
                ScaledHeight = (uint)finalHeight,
                InterpolationMode = BitmapInterpolationMode.Fant // High quality downsampling
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb);

            using var outStream = new InMemoryRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);

            encoder.SetPixelData(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                (uint)finalWidth,
                (uint)finalHeight,
                96, 96,
                pixelData.DetachPixelData());

            await encoder.FlushAsync();

            var resultBytes = new byte[outStream.Size];
            using var reader = new DataReader(outStream.GetInputStreamAt(0));
            await reader.LoadAsync((uint)outStream.Size);
            reader.ReadBytes(resultBytes);

            return resultBytes;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to crop and scale image to aspect {Aspect}", targetAspect);
            return sourceBytes;
        }
    }
}
