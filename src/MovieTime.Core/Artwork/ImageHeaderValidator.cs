using System;
using System.Buffers.Binary;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Validates binary image signatures and extracts dimensions without decoding compressed image bitmaps.
/// Guards against corrupt, truncated, or invalid binary blobs.
/// </summary>
public static class ImageHeaderValidator
{
    private const int MinValidImageBytes = 256;
    private const int MaxValidDimension = 16384;
    private const int MinValidDimension = 32;

    public static ImageHeaderInfo? ValidateAndGetInfo(ReadOnlySpan<byte> data)
    {
        if (data.Length < MinValidImageBytes)
        {
            return null;
        }

        // 1. JPEG: 0xFF, 0xD8, 0xFF
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            if (TryExtractJpegDimensions(data, out var w, out var h))
            {
                return new ImageHeaderInfo(ImageFormat.Jpeg, w, h, data.Length);
            }
            return null;
        }

        // 2. PNG: 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        if (data.Length >= 24 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
        {
            // IHDR chunk: width at 16..19, height at 20..23 (big endian)
            var width = BinaryPrimitives.ReadInt32BigEndian(data.Slice(16, 4));
            var height = BinaryPrimitives.ReadInt32BigEndian(data.Slice(20, 4));

            if (IsValidDimension(width) && IsValidDimension(height))
            {
                return new ImageHeaderInfo(ImageFormat.Png, width, height, data.Length);
            }
            return null;
        }

        // 3. WebP: RIFF ... WEBP
        if (data.Length >= 30 &&
            data[0] == 0x52 && data[1] == 0x49 && data[2] == 0x46 && data[3] == 0x46 &&
            data[8] == 0x57 && data[9] == 0x45 && data[10] == 0x42 && data[11] == 0x50)
        {
            if (TryExtractWebpDimensions(data, out var w, out var h))
            {
                return new ImageHeaderInfo(ImageFormat.WebP, w, h, data.Length);
            }
            return null;
        }

        // 4. BMP: "BM"
        if (data.Length >= 26 && data[0] == 0x42 && data[1] == 0x4D)
        {
            var width = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(18, 4));
            var height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data.Slice(22, 4)));

            if (IsValidDimension(width) && IsValidDimension(height))
            {
                return new ImageHeaderInfo(ImageFormat.Bmp, width, height, data.Length);
            }
        }

        return null;
    }

    private static bool TryExtractJpegDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;
        var offset = 2; // skip SOI (0xFF, 0xD8)

        while (offset + 4 < data.Length)
        {
            // Must begin with 0xFF
            if (data[offset] != 0xFF)
            {
                offset++;
                continue;
            }

            var marker = data[offset + 1];
            offset += 2;

            // Variable-length marker segments
            if (offset + 2 > data.Length) break;
            var segmentLength = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));

            // SOF0 (Baseline), SOF1 (Extended), SOF2 (Progressive)
            if (marker is 0xC0 or 0xC1 or 0xC2)
            {
                if (offset + 7 < data.Length)
                {
                    // Precision (1 byte) at offset+2
                    // Height (2 bytes) at offset+3
                    // Width (2 bytes) at offset+5
                    height = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 3, 2));
                    width = BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset + 5, 2));
                    return IsValidDimension(width) && IsValidDimension(height);
                }
                return false;
            }

            // Stop search if SOS (Start of Scan) or EOI is reached
            if (marker is 0xDA or 0xD9)
            {
                break;
            }

            if (segmentLength < 2) break;
            offset += segmentLength;
        }

        return false;
    }

    private static bool TryExtractWebpDimensions(ReadOnlySpan<byte> data, out int width, out int height)
    {
        width = 0;
        height = 0;

        // "VP8 " Lossy
        if (data.Length >= 30 && data[12] == 0x56 && data[13] == 0x50 && data[14] == 0x38 && data[15] == 0x20)
        {
            // Keyframe startcode 0x9D 0x01 0x2A
            if (data.Length >= 26 && data[23] == 0x9D && data[24] == 0x01 && data[25] == 0x2A)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(26, 2)) & 0x3FFF;
                height = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(28, 2)) & 0x3FFF;
                return IsValidDimension(width) && IsValidDimension(height);
            }
        }

        // "VP8L" Lossless
        if (data.Length >= 25 && data[12] == 0x56 && data[13] == 0x50 && data[14] == 0x38 && data[15] == 0x4C)
        {
            if (data[20] == 0x2F) // 1-byte signature
            {
                var b1 = data[21];
                var b2 = data[22];
                var b3 = data[23];
                var b4 = data[24];
                width = 1 + (b1 | ((b2 & 0x3F) << 8));
                height = 1 + (((b2 >> 6) | (b3 << 2) | ((b4 & 0x0F) << 10)));
                return IsValidDimension(width) && IsValidDimension(height);
            }
        }

        // "VP8X" Extended
        if (data.Length >= 30 && data[12] == 0x56 && data[13] == 0x50 && data[14] == 0x38 && data[15] == 0x58)
        {
            width = 1 + (data[24] | (data[25] << 8) | (data[26] << 16));
            height = 1 + (data[27] | (data[28] << 8) | (data[29] << 16));
            return IsValidDimension(width) && IsValidDimension(height);
        }

        return false;
    }

    private static bool IsValidDimension(int dim) => dim is >= MinValidDimension and <= MaxValidDimension;
}
