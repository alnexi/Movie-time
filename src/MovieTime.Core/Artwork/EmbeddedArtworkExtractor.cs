using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MovieTime.Core.Artwork;

/// <summary>
/// Production embedded artwork extractor parsing MP4 ilst/covr atoms, MKV EBML attachments,
/// and ID3v2 APIC tags directly from file streams without external codec or player overhead.
/// </summary>
public sealed class EmbeddedArtworkExtractor : IEmbeddedArtworkExtractor
{
    private const int MaxAllowedArtworkBytes = 25 * 1024 * 1024; // 25 MB guard
    private const int MinAllowedArtworkBytes = 256; // 256 bytes guard matching ImageHeaderValidator
    private readonly ILogger<EmbeddedArtworkExtractor> _logger;

    public EmbeddedArtworkExtractor(ILogger<EmbeddedArtworkExtractor>? logger = null)
    {
        _logger = logger ?? NullLogger<EmbeddedArtworkExtractor>.Instance;
    }

    public async Task<byte[]?> ExtractEmbeddedArtworkAsync(string filePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
        {
            return null;
        }

        var ext = Path.GetExtension(filePath).ToLowerInvariant();

        try
        {
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                useAsync: true);

            byte[]? rawImage = null;

            if (ext is ".mp4" or ".m4v" or ".mov")
            {
                rawImage = await ExtractFromMp4Async(stream, cancellationToken).ConfigureAwait(false);
            }
            else if (ext is ".mkv" or ".mka" or ".webm")
            {
                rawImage = await ExtractFromMatroskaAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                // General fallback: check for ID3v2 header
                rawImage = await ExtractFromId3v2Async(stream, cancellationToken).ConfigureAwait(false);
            }

            if (rawImage != null && rawImage.Length >= MinAllowedArtworkBytes)
            {
                var validation = ImageHeaderValidator.ValidateAndGetInfo(rawImage);
                if (validation.HasValue && validation.Value.IsValid)
                {
                    _logger.LogInformation("Found verified embedded {Format} artwork ({W}x{H}, {Bytes} bytes) in {Path}",
                        validation.Value.Format, validation.Value.Width, validation.Value.Height, rawImage.Length, filePath);
                    return rawImage;
                }

                _logger.LogWarning("Extracted binary payload in {Path} failed image header validation. Ignored.", filePath);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Embedded artwork extraction completed without match for {Path}", filePath);
        }

        return null;
    }

    #region MP4 Box Parsing

    private async Task<byte[]?> ExtractFromMp4Async(Stream stream, CancellationToken ct)
    {
        var buffer = new byte[8];
        var streamLength = stream.Length;

        while (stream.Position + 8 <= streamLength)
        {
            ct.ThrowIfCancellationRequested();

            var read = await stream.ReadAsync(buffer.AsMemory(0, 8), ct).ConfigureAwait(false);
            if (read < 8) break;

            var boxSize = (long)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
            var boxType = Encoding.ASCII.GetString(buffer, 4, 4);

            long headerSize = 8;
            if (boxSize == 1) // 64-bit extended size
            {
                var extBuffer = new byte[8];
                var extRead = await stream.ReadAsync(extBuffer.AsMemory(0, 8), ct).ConfigureAwait(false);
                if (extRead < 8) break;
                boxSize = (long)BinaryPrimitives.ReadUInt64BigEndian(extBuffer);
                headerSize = 16;
            }
            else if (boxSize == 0) // Extends to EOF
            {
                boxSize = streamLength - (stream.Position - headerSize);
            }

            if (boxSize < headerSize) break;
            var payloadSize = boxSize - headerSize;

            if (boxType == "moov")
            {
                return await ParseMp4MoovAsync(stream, stream.Position + payloadSize, ct).ConfigureAwait(false);
            }

            // Seek past non-moov boxes
            var nextPos = stream.Position + payloadSize;
            if (nextPos >= streamLength || nextPos < 0) break;
            stream.Seek(nextPos, SeekOrigin.Begin);
        }

        return null;
    }

    private async Task<byte[]?> ParseMp4MoovAsync(Stream stream, long endPos, CancellationToken ct)
    {
        var buffer = new byte[8];

        while (stream.Position + 8 <= endPos)
        {
            ct.ThrowIfCancellationRequested();

            if (await stream.ReadAsync(buffer.AsMemory(0, 8), ct).ConfigureAwait(false) < 8) break;
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(buffer, 4, 4);

            if (size < 8) break;
            var boxEnd = stream.Position - 8 + size;

            if (type == "udta")
            {
                return await ParseMp4UdtaAsync(stream, Math.Min(boxEnd, endPos), ct).ConfigureAwait(false);
            }

            stream.Seek(boxEnd, SeekOrigin.Begin);
        }

        return null;
    }

    private async Task<byte[]?> ParseMp4UdtaAsync(Stream stream, long endPos, CancellationToken ct)
    {
        var buffer = new byte[8];

        while (stream.Position + 8 <= endPos)
        {
            ct.ThrowIfCancellationRequested();

            if (await stream.ReadAsync(buffer.AsMemory(0, 8), ct).ConfigureAwait(false) < 8) break;
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(buffer, 4, 4);

            if (size < 8) break;
            var boxEnd = stream.Position - 8 + size;

            if (type == "meta")
            {
                // MP4 meta box has a 4-byte version/flags header before child boxes
                if (stream.Position + 4 <= boxEnd)
                {
                    stream.Seek(4, SeekOrigin.Current);
                }
                return await ParseMp4MetaAsync(stream, Math.Min(boxEnd, endPos), ct).ConfigureAwait(false);
            }

            stream.Seek(boxEnd, SeekOrigin.Begin);
        }

        return null;
    }

    private async Task<byte[]?> ParseMp4MetaAsync(Stream stream, long endPos, CancellationToken ct)
    {
        var buffer = new byte[8];

        while (stream.Position + 8 <= endPos)
        {
            ct.ThrowIfCancellationRequested();

            if (await stream.ReadAsync(buffer.AsMemory(0, 8), ct).ConfigureAwait(false) < 8) break;
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(buffer, 4, 4);

            if (size < 8) break;
            var boxEnd = stream.Position - 8 + size;

            if (type == "ilst")
            {
                return await ParseMp4IlstAsync(stream, Math.Min(boxEnd, endPos), ct).ConfigureAwait(false);
            }

            stream.Seek(boxEnd, SeekOrigin.Begin);
        }

        return null;
    }

    private async Task<byte[]?> ParseMp4IlstAsync(Stream stream, long endPos, CancellationToken ct)
    {
        var buffer = new byte[8];

        while (stream.Position + 8 <= endPos)
        {
            ct.ThrowIfCancellationRequested();

            if (await stream.ReadAsync(buffer.AsMemory(0, 8), ct).ConfigureAwait(false) < 8) break;
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(buffer, 4, 4);

            if (size < 8) break;
            var boxEnd = stream.Position - 8 + size;

            if (type == "covr")
            {
                return await ParseMp4CovrAsync(stream, Math.Min(boxEnd, endPos), ct).ConfigureAwait(false);
            }

            stream.Seek(boxEnd, SeekOrigin.Begin);
        }

        return null;
    }

    private async Task<byte[]?> ParseMp4CovrAsync(Stream stream, long endPos, CancellationToken ct)
    {
        var buffer = new byte[8];

        while (stream.Position + 8 <= endPos)
        {
            ct.ThrowIfCancellationRequested();

            if (await stream.ReadAsync(buffer.AsMemory(0, 8), ct).ConfigureAwait(false) < 8) break;
            var size = (long)BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0, 4));
            var type = Encoding.ASCII.GetString(buffer, 4, 4);

            if (size < 8) break;
            var boxEnd = stream.Position - 8 + size;

            if (type == "data")
            {
                // 'data' box has 8 bytes after size/type: 4 bytes type flags + 4 bytes locale (0)
                if (stream.Position + 8 > boxEnd) break;
                stream.Seek(8, SeekOrigin.Current);

                var imageLength = (int)(boxEnd - stream.Position);
                if (imageLength is >= MinAllowedArtworkBytes and <= MaxAllowedArtworkBytes)
                {
                    var imageBytes = new byte[imageLength];
                    var totalRead = 0;
                    while (totalRead < imageLength)
                    {
                        var chunk = await stream.ReadAsync(imageBytes.AsMemory(totalRead, imageLength - totalRead), ct).ConfigureAwait(false);
                        if (chunk == 0) break;
                        totalRead += chunk;
                    }

                    if (totalRead == imageLength)
                    {
                        return imageBytes;
                    }
                }
            }

            stream.Seek(boxEnd, SeekOrigin.Begin);
        }

        return null;
    }

    #endregion

    #region Matroska (MKV / EBML) Parsing

    private async Task<byte[]?> ExtractFromMatroskaAsync(Stream stream, CancellationToken ct)
    {
        // 1. Validate EBML Header (0x1A, 0x45, 0xDF, 0xA3)
        var ebmlId = await ReadEbmlIdAsync(stream, ct).ConfigureAwait(false);
        if (ebmlId != 0x1A45DFA3) return null;

        var ebmlHeaderLen = await ReadEbmlVintAsync(stream, ct).ConfigureAwait(false);
        if (ebmlHeaderLen < 0 || stream.Position + ebmlHeaderLen > stream.Length) return null;
        stream.Seek(ebmlHeaderLen, SeekOrigin.Current);

        // 2. Read Segment ID (0x18, 0x53, 0x80, 0x67)
        var segmentId = await ReadEbmlIdAsync(stream, ct).ConfigureAwait(false);
        if (segmentId != 0x18538067) return null;

        var segmentLen = await ReadEbmlVintAsync(stream, ct).ConfigureAwait(false);
        var segmentStartPos = stream.Position;
        var segmentEndPos = segmentLen > 0 ? segmentStartPos + segmentLen : stream.Length;

        // 3. Scan Segment children for SeekHead (0x114D9B74) or Attachments (0x1941A469)
        long? attachmentsOffset = null;

        while (stream.Position < segmentEndPos && stream.Position + 4 <= stream.Length)
        {
            ct.ThrowIfCancellationRequested();

            var elementId = await ReadEbmlIdAsync(stream, ct).ConfigureAwait(false);
            if (elementId == 0) break;

            var elementLen = await ReadEbmlVintAsync(stream, ct).ConfigureAwait(false);
            if (elementLen < 0) break;

            var nextElementPos = stream.Position + elementLen;

            if (elementId == 0x114D9B74) // SeekHead
            {
                attachmentsOffset = await ScanSeekHeadForAttachmentsOffsetAsync(stream, stream.Position + elementLen, segmentStartPos, ct).ConfigureAwait(false);
                if (attachmentsOffset.HasValue)
                {
                    break;
                }
            }
            else if (elementId == 0x1941A469) // Direct Attachments
            {
                return await ParseAttachmentsElementAsync(stream, stream.Position + elementLen, ct).ConfigureAwait(false);
            }
            else if (elementId == 0x1F43B675) // Cluster element (audio/video data)
            {
                // If we reach Cluster and haven't found Attachments or a SeekHead pointer, don't read GBs of media
                if (!attachmentsOffset.HasValue)
                {
                    break;
                }
            }

            if (nextElementPos > stream.Length || nextElementPos < 0) break;
            stream.Seek(nextElementPos, SeekOrigin.Begin);
        }

        // If SeekHead pointed to Attachments, jump directly there
        if (attachmentsOffset.HasValue && attachmentsOffset.Value < stream.Length)
        {
            stream.Seek(attachmentsOffset.Value, SeekOrigin.Begin);
            var attId = await ReadEbmlIdAsync(stream, ct).ConfigureAwait(false);
            if (attId == 0x1941A469)
            {
                var attLen = await ReadEbmlVintAsync(stream, ct).ConfigureAwait(false);
                if (attLen > 0)
                {
                    return await ParseAttachmentsElementAsync(stream, stream.Position + attLen, ct).ConfigureAwait(false);
                }
            }
        }

        return null;
    }

    private async Task<long?> ScanSeekHeadForAttachmentsOffsetAsync(Stream stream, long endPos, long segmentStartPos, CancellationToken ct)
    {
        while (stream.Position < endPos && stream.Position + 4 <= stream.Length)
        {
            ct.ThrowIfCancellationRequested();

            var id = await ReadEbmlIdAsync(stream, ct).ConfigureAwait(false);
            var len = await ReadEbmlVintAsync(stream, ct).ConfigureAwait(false);
            if (id == 0 || len < 0) break;

            var nextSeekPos = stream.Position + len;

            if (id == 0x4DBB) // Seek entry
            {
                var entryEnd = stream.Position + len;
                uint seekId = 0;
                long seekPos = -1;

                while (stream.Position < entryEnd)
                {
                    var childId = await ReadEbmlIdAsync(stream, ct).ConfigureAwait(false);
                    var childLen = await ReadEbmlVintAsync(stream, ct).ConfigureAwait(false);
                    if (childId == 0 || childLen < 0) break;

                    if (childId == 0x53AB) // SeekID
                    {
                        var buf = new byte[childLen];
                        await stream.ReadAsync(buf.AsMemory(0, (int)childLen), ct).ConfigureAwait(false);
                        if (childLen == 4) seekId = BinaryPrimitives.ReadUInt32BigEndian(buf);
                    }
                    else if (childId == 0x53AC) // SeekPosition
                    {
                        seekPos = await ReadEbmlUIntAsync(stream, (int)childLen, ct).ConfigureAwait(false);
                    }
                    else
                    {
                        stream.Seek(childLen, SeekOrigin.Current);
                    }
                }

                if (seekId == 0x1941A469 && seekPos >= 0) // Attachments ID
                {
                    return segmentStartPos + seekPos;
                }
            }

            stream.Seek(nextSeekPos, SeekOrigin.Begin);
        }

        return null;
    }

    private async Task<byte[]?> ParseAttachmentsElementAsync(Stream stream, long endPos, CancellationToken ct)
    {
        byte[]? fallbackImage = null;

        while (stream.Position < endPos && stream.Position + 4 <= stream.Length)
        {
            ct.ThrowIfCancellationRequested();

            var id = await ReadEbmlIdAsync(stream, ct).ConfigureAwait(false);
            var len = await ReadEbmlVintAsync(stream, ct).ConfigureAwait(false);
            if (id == 0 || len < 0) break;

            var nextPos = stream.Position + len;

            if (id == 0x61A7) // AttachedFile
            {
                var file = await ParseAttachedFileAsync(stream, stream.Position + len, ct).ConfigureAwait(false);
                if (file.HasValue && file.Value.Data != null)
                {
                    var name = file.Value.FileName?.ToLowerInvariant() ?? string.Empty;
                    var mime = file.Value.MimeType?.ToLowerInvariant() ?? string.Empty;

                    // Prioritize dedicated poster / cover images
                    if (name.Contains("cover") || name.Contains("poster") || name.Contains("front"))
                    {
                        return file.Value.Data;
                    }

                    if (fallbackImage == null && (mime.StartsWith("image/") || name.EndsWith(".jpg") || name.EndsWith(".png")))
                    {
                        fallbackImage = file.Value.Data;
                    }
                }
            }

            stream.Seek(nextPos, SeekOrigin.Begin);
        }

        return fallbackImage;
    }

    private readonly record struct AttachedFileData(string? FileName, string? MimeType, byte[]? Data);

    private async Task<AttachedFileData?> ParseAttachedFileAsync(Stream stream, long endPos, CancellationToken ct)
    {
        string? fileName = null;
        string? mimeType = null;
        byte[]? data = null;

        while (stream.Position < endPos && stream.Position + 4 <= stream.Length)
        {
            ct.ThrowIfCancellationRequested();

            var id = await ReadEbmlIdAsync(stream, ct).ConfigureAwait(false);
            var len = await ReadEbmlVintAsync(stream, ct).ConfigureAwait(false);
            if (id == 0 || len < 0) break;

            var nextPos = stream.Position + len;

            if (id == 0x466E) // FileName (UTF-8 string)
            {
                var buf = new byte[len];
                await stream.ReadAsync(buf.AsMemory(0, (int)len), ct).ConfigureAwait(false);
                fileName = Encoding.UTF8.GetString(buf).Trim('\0', ' ');
            }
            else if (id == 0x4660) // FileMimeType (ASCII string)
            {
                var buf = new byte[len];
                await stream.ReadAsync(buf.AsMemory(0, (int)len), ct).ConfigureAwait(false);
                mimeType = Encoding.ASCII.GetString(buf).Trim('\0', ' ');
            }
            else if (id == 0x465C) // FileData (Binary)
            {
                if (len is >= MinAllowedArtworkBytes and <= MaxAllowedArtworkBytes)
                {
                    data = new byte[len];
                    var totalRead = 0;
                    while (totalRead < len)
                    {
                        var read = await stream.ReadAsync(data.AsMemory(totalRead, (int)len - totalRead), ct).ConfigureAwait(false);
                        if (read == 0) break;
                        totalRead += read;
                    }
                }
            }

            stream.Seek(nextPos, SeekOrigin.Begin);
        }

        return new AttachedFileData(fileName, mimeType, data);
    }

    private static async Task<uint> ReadEbmlIdAsync(Stream stream, CancellationToken ct)
    {
        var b = stream.ReadByte();
        if (b == -1) return 0;

        uint id = (uint)b;
        int numBytes;

        if ((b & 0x80) != 0) numBytes = 1;
        else if ((b & 0x40) != 0) numBytes = 2;
        else if ((b & 0x20) != 0) numBytes = 3;
        else if ((b & 0x10) != 0) numBytes = 4;
        else return 0;

        for (int i = 1; i < numBytes; i++)
        {
            var next = stream.ReadByte();
            if (next == -1) return 0;
            id = (id << 8) | (uint)next;
        }

        return id;
    }

    private static async Task<long> ReadEbmlVintAsync(Stream stream, CancellationToken ct)
    {
        var b = stream.ReadByte();
        if (b == -1) return -1;

        int numBytes;
        int mask;

        if ((b & 0x80) != 0) { numBytes = 1; mask = 0x7F; }
        else if ((b & 0x40) != 0) { numBytes = 2; mask = 0x3F; }
        else if ((b & 0x20) != 0) { numBytes = 3; mask = 0x1F; }
        else if ((b & 0x10) != 0) { numBytes = 4; mask = 0x0F; }
        else if ((b & 0x08) != 0) { numBytes = 5; mask = 0x07; }
        else if ((b & 0x04) != 0) { numBytes = 6; mask = 0x03; }
        else if ((b & 0x02) != 0) { numBytes = 7; mask = 0x01; }
        else if ((b & 0x01) != 0) { numBytes = 8; mask = 0x00; }
        else return -1;

        long val = b & mask;

        for (int i = 1; i < numBytes; i++)
        {
            var next = stream.ReadByte();
            if (next == -1) return -1;
            val = (val << 8) | (byte)next;
        }

        return val;
    }

    private static async Task<long> ReadEbmlUIntAsync(Stream stream, int length, CancellationToken ct)
    {
        if (length is < 1 or > 8) return -1;
        long val = 0;
        for (int i = 0; i < length; i++)
        {
            var b = stream.ReadByte();
            if (b == -1) return -1;
            val = (val << 8) | (byte)b;
        }
        return val;
    }

    #endregion

    #region ID3v2 APIC Parsing

    private async Task<byte[]?> ExtractFromId3v2Async(Stream stream, CancellationToken ct)
    {
        if (stream.Length < 10) return null;
        stream.Seek(0, SeekOrigin.Begin);

        var header = new byte[10];
        if (await stream.ReadAsync(header.AsMemory(0, 10), ct).ConfigureAwait(false) < 10) return null;

        // "ID3"
        if (header[0] != 0x49 || header[1] != 0x44 || header[2] != 0x33) return null;

        var majorVersion = header[3];
        if (majorVersion is not (3 or 4)) return null;

        // Size is a 28-bit synchsafe integer
        var tagSize = ((header[6] & 0x7F) << 21) |
                      ((header[7] & 0x7F) << 14) |
                      ((header[8] & 0x7F) << 7) |
                      (header[9] & 0x7F);

        var tagEnd = Math.Min(stream.Length, 10 + tagSize);

        while (stream.Position + 10 <= tagEnd)
        {
            ct.ThrowIfCancellationRequested();

            var frameHeader = new byte[10];
            if (await stream.ReadAsync(frameHeader.AsMemory(0, 10), ct).ConfigureAwait(false) < 10) break;

            var frameId = Encoding.ASCII.GetString(frameHeader, 0, 4);
            var frameSize = majorVersion == 4
                ? ((frameHeader[4] & 0x7F) << 21) | ((frameHeader[5] & 0x7F) << 14) | ((frameHeader[6] & 0x7F) << 7) | (frameHeader[7] & 0x7F)
                : BinaryPrimitives.ReadInt32BigEndian(frameHeader.AsSpan(4, 4));

            if (frameSize <= 0 || stream.Position + frameSize > tagEnd) break;

            if (frameId == "APIC") // Attached Picture
            {
                var payload = new byte[frameSize];
                if (await stream.ReadAsync(payload.AsMemory(0, frameSize), ct).ConfigureAwait(false) == frameSize)
                {
                    return ParseApicPayload(payload);
                }
            }

            stream.Seek(frameSize, SeekOrigin.Current);
        }

        return null;
    }

    private static byte[]? ParseApicPayload(byte[] payload)
    {
        if (payload.Length < 16) return null;

        var textEncoding = payload[0];
        var offset = 1;

        // Read MIME string (null-terminated ASCII)
        var mimeEnd = Array.IndexOf(payload, (byte)0, offset);
        if (mimeEnd == -1) return null;
        offset = mimeEnd + 1;

        if (offset >= payload.Length) return null;
        var pictureType = payload[offset++];

        // Read Description string (terminated based on textEncoding)
        if (textEncoding == 0 || textEncoding == 3) // ISO-8859-1 or UTF-8 (single zero)
        {
            var descEnd = Array.IndexOf(payload, (byte)0, offset);
            if (descEnd != -1) offset = descEnd + 1;
        }
        else // UTF-16 (double zero)
        {
            for (int i = offset; i + 1 < payload.Length; i += 2)
            {
                if (payload[i] == 0 && payload[i + 1] == 0)
                {
                    offset = i + 2;
                    break;
                }
            }
        }

        var imageLength = payload.Length - offset;
        if (imageLength is >= MinAllowedArtworkBytes and <= MaxAllowedArtworkBytes)
        {
            var image = new byte[imageLength];
            Array.Copy(payload, offset, image, 0, imageLength);
            return image;
        }

        return null;
    }

    #endregion
}
