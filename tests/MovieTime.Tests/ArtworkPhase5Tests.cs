using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Artwork;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class ArtworkPhase5Tests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly StoragePathService _storagePathService;
    private readonly DatabaseService _databaseService;
    private readonly MovieRepository _movieRepository;
    private readonly MediaFileRepository _mediaFileRepository;
    private readonly ArtworkStorageService _storageService;
    private readonly ImageQualityEvaluator _qualityEvaluator;

    public ArtworkPhase5Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ArtworkPhase5Tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_artwork.db");

        _storagePathService = new StoragePathService(_tempDir);
        _databaseService = new DatabaseService(_storagePathService, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _movieRepository = new MovieRepository(_databaseService);
        _mediaFileRepository = new MediaFileRepository(_databaseService);
        _storageService = new ArtworkStorageService(_storagePathService, NullLogger<ArtworkStorageService>.Instance);
        _qualityEvaluator = new ImageQualityEvaluator();
    }

    public void Dispose()
    {
        _databaseService.Dispose();
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }

    #region Helper Factories for Synthetic Images

    public static byte[] CreateSyntheticJpeg(int width, int height)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // SOI
        writer.Write((byte)0xFF);
        writer.Write((byte)0xD8);

        // APP0 (JFIF)
        writer.Write((byte)0xFF);
        writer.Write((byte)0xE0);
        writer.Write((byte)0x00);
        writer.Write((byte)0x10); // Length = 16
        writer.Write(Encoding.ASCII.GetBytes("JFIF\0"));
        writer.Write((byte)1);
        writer.Write((byte)1);
        writer.Write((byte)0);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((byte)0);
        writer.Write((byte)0);

        // COM (Comment) padding to satisfy 1024 minimum bytes
        writer.Write((byte)0xFF);
        writer.Write((byte)0xFE);
        writer.Write((byte)0x04);
        writer.Write((byte)0x02); // Length = 1026
        writer.Write(new byte[1024]);

        // SOF0 (Baseline DCT)
        writer.Write((byte)0xFF);
        writer.Write((byte)0xC0);
        writer.Write((byte)0x00);
        writer.Write((byte)0x11); // Length = 17
        writer.Write((byte)8);    // Precision
        writer.Write((byte)(height >> 8));
        writer.Write((byte)(height & 0xFF));
        writer.Write((byte)(width >> 8));
        writer.Write((byte)(width & 0xFF));
        writer.Write((byte)3);    // 3 components

        // Component 1
        writer.Write((byte)1);
        writer.Write((byte)0x22);
        writer.Write((byte)0);

        // Component 2
        writer.Write((byte)2);
        writer.Write((byte)0x11);
        writer.Write((byte)1);

        // Component 3
        writer.Write((byte)3);
        writer.Write((byte)0x11);
        writer.Write((byte)1);

        // EOI
        writer.Write((byte)0xFF);
        writer.Write((byte)0xD9);

        return ms.ToArray();
    }

    public static byte[] CreateSyntheticPng(int width, int height)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // PNG 8-byte signature
        writer.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        // IHDR chunk: length (13), chunk type "IHDR", width, height, bit depth, color type, compression, filter, interlace, CRC
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x00);
        writer.Write((byte)0x0D);

        writer.Write(Encoding.ASCII.GetBytes("IHDR"));
        writer.Write((byte)(width >> 24));
        writer.Write((byte)((width >> 16) & 0xFF));
        writer.Write((byte)((width >> 8) & 0xFF));
        writer.Write((byte)(width & 0xFF));

        writer.Write((byte)(height >> 24));
        writer.Write((byte)((height >> 16) & 0xFF));
        writer.Write((byte)((height >> 8) & 0xFF));
        writer.Write((byte)(height & 0xFF));

        writer.Write((byte)8); // 8-bit
        writer.Write((byte)6); // RGBA
        writer.Write((byte)0);
        writer.Write((byte)0);
        writer.Write((byte)0);

        // 4 bytes CRC
        writer.Write(new byte[] { 0x00, 0x00, 0x00, 0x00 });

        // Add dummy chunk to pad beyond 256 bytes
        writer.Write((int)256);
        writer.Write(Encoding.ASCII.GetBytes("tEXt"));
        writer.Write(new byte[256]);
        writer.Write((int)0);

        return ms.ToArray();
    }

    public static byte[] CreateSyntheticWebp(int width, int height)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // RIFF header
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write((int)300); // File size - 8
        writer.Write(Encoding.ASCII.GetBytes("WEBPVP8 "));

        // VP8 chunk
        writer.Write((int)280); // Chunk size
        // 3 bytes frame tag
        writer.Write(new byte[] { 0x00, 0x00, 0x00 });
        // 3 bytes start code 9d 01 2a
        writer.Write(new byte[] { 0x9D, 0x01, 0x2A });
        // Width (14 bits) + horizontal scale (2 bits)
        ushort wCode = (ushort)(width & 0x3FFF);
        writer.Write((byte)(wCode & 0xFF));
        writer.Write((byte)(wCode >> 8));
        // Height (14 bits) + vertical scale (2 bits)
        ushort hCode = (ushort)(height & 0x3FFF);
        writer.Write((byte)(hCode & 0xFF));
        writer.Write((byte)(hCode >> 8));

        // Pad to >= 256 bytes
        writer.Write(new byte[260]);

        return ms.ToArray();
    }

    public static byte[] CreateSyntheticBmp(int width, int height)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // BM signature
        writer.Write((byte)'B');
        writer.Write((byte)'M');
        writer.Write((int)300); // Total size
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((int)54); // Pixel offset

        // BITMAPINFOHEADER (40 bytes)
        writer.Write((int)40);
        writer.Write((int)width);
        writer.Write((int)height);
        writer.Write((ushort)1);  // Planes
        writer.Write((ushort)24); // 24-bit
        writer.Write((int)0);     // Compression
        writer.Write((int)0);
        writer.Write((int)0);
        writer.Write((int)0);
        writer.Write((int)0);
        writer.Write((int)0);

        // Pad to >= 256 bytes
        writer.Write(new byte[250]);

        return ms.ToArray();
    }

    #endregion

    #region Task 1: Image Header & Format Validation Tests

    [Fact]
    public void ImageHeaderValidator_ValidJpeg_ExtractsCorrectDimensions()
    {
        var jpegBytes = CreateSyntheticJpeg(600, 900);
        var header = ImageHeaderValidator.ValidateAndGetInfo(jpegBytes);

        Assert.NotNull(header);
        Assert.Equal(ImageFormat.Jpeg, header.Value.Format);
        Assert.Equal(600, header.Value.Width);
        Assert.Equal(900, header.Value.Height);
        Assert.True(header.Value.IsVerticalPoster);
        Assert.False(header.Value.IsWidescreenBackdrop);
    }

    [Fact]
    public void ImageHeaderValidator_ValidPng_ExtractsCorrectDimensions()
    {
        var pngBytes = CreateSyntheticPng(1920, 1080);
        var header = ImageHeaderValidator.ValidateAndGetInfo(pngBytes);

        Assert.NotNull(header);
        Assert.Equal(ImageFormat.Png, header.Value.Format);
        Assert.Equal(1920, header.Value.Width);
        Assert.Equal(1080, header.Value.Height);
        Assert.True(header.Value.IsWidescreenBackdrop);
        Assert.False(header.Value.IsVerticalPoster);
    }

    [Fact]
    public void ImageHeaderValidator_ValidWebP_ExtractsCorrectDimensions()
    {
        var webpBytes = CreateSyntheticWebp(1280, 720);
        var header = ImageHeaderValidator.ValidateAndGetInfo(webpBytes);

        Assert.NotNull(header);
        Assert.Equal(ImageFormat.WebP, header.Value.Format);
        Assert.Equal(1280, header.Value.Width);
        Assert.Equal(720, header.Value.Height);
        Assert.True(header.Value.IsWidescreenBackdrop);
    }

    [Fact]
    public void ImageHeaderValidator_ValidBmp_ExtractsCorrectDimensions()
    {
        var bmpBytes = CreateSyntheticBmp(640, 480);
        var header = ImageHeaderValidator.ValidateAndGetInfo(bmpBytes);

        Assert.NotNull(header);
        Assert.Equal(ImageFormat.Bmp, header.Value.Format);
        Assert.Equal(640, header.Value.Width);
        Assert.Equal(480, header.Value.Height);
    }

    [Fact]
    public void ImageHeaderValidator_ArbitraryBinaryPayload_Rejected()
    {
        // 512 bytes of arbitrary non-image binary data
        var randomBytes = new byte[512];
        new Random(42).NextBytes(randomBytes);

        var header = ImageHeaderValidator.ValidateAndGetInfo(randomBytes);

        Assert.Null(header);
    }

    [Fact]
    public void ImageHeaderValidator_TooSmallDimensions_Rejected()
    {
        // JPEG that is only 16x16 (below minimum 32x32)
        var smallJpeg = CreateSyntheticJpeg(16, 16);
        var header = ImageHeaderValidator.ValidateAndGetInfo(smallJpeg);

        Assert.Null(header);
    }

    #endregion

    #region Task 2: Image Quality Evaluation Tests

    [Fact]
    public void ImageQualityEvaluator_PureBlackFrame_RejectedAsUnusable()
    {
        // 100 x 100 pure black BGRA frame
        var pixels = new byte[100 * 100 * 4]; // all zeros
        var score = _qualityEvaluator.Evaluate(pixels, 100, 100);

        Assert.False(score.IsUsable);
        Assert.False(score.IsAcceptable);
        Assert.True(score.Luminance < 1.0);
        Assert.Contains("dark", score.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImageQualityEvaluator_BlownOutWhiteFrame_RejectedAsUnusable()
    {
        // 100 x 100 pure white BGRA frame
        var pixels = new byte[100 * 100 * 4];
        Array.Fill(pixels, (byte)255);
        var score = _qualityEvaluator.Evaluate(pixels, 100, 100);

        Assert.False(score.IsUsable);
        Assert.False(score.IsAcceptable);
        Assert.True(score.Luminance > 250);
        Assert.Contains("overexposed", score.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImageQualityEvaluator_HighContrastSharpColorfulFrame_Accepted()
    {
        // Generate high contrast alternating patterns with color variation
        int width = 120;
        int height = 80;
        var pixels = new byte[width * height * 4];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int idx = (y * width + x) * 4;
                // High contrast checkerboard with distinct luminance & colorfulness
                bool checker = ((x + y) % 2 == 0);
                pixels[idx + 0] = checker ? (byte)20 : (byte)220;   // B
                pixels[idx + 1] = checker ? (byte)220 : (byte)30;   // G
                pixels[idx + 2] = checker ? (byte)240 : (byte)20;   // R
                pixels[idx + 3] = 255;                              // A
            }
        }

        var score = _qualityEvaluator.Evaluate(pixels, width, height);

        Assert.True(score.IsUsable);
        Assert.True(score.OverallScore > 50.0);
        Assert.True(score.Sharpness > 10.0);
        Assert.True(score.Contrast > 20.0);
    }

    #endregion

    #region Task 3: Artwork Storage & Cache Keys Tests

    [Fact]
    public void ArtworkStorageService_MultiDriveDifferentiation_ProducesDistinctHashKeys()
    {
        long fileSize = 4_000_000_000L;
        var modified = DateTime.UtcNow;

        string keyD = _storageService.ComputeMediaHash(@"D:\Movies\Gladiator.mkv", fileSize, modified);
        string keyE = _storageService.ComputeMediaHash(@"E:\Movies\Gladiator.mkv", fileSize, modified);

        Assert.NotEqual(keyD, keyE);
    }

    [Fact]
    public void ArtworkStorageService_TimestampChange_InvalidatesCacheKey()
    {
        string path = @"D:\Movies\Inception.mkv";
        long fileSize = 2_500_000_000L;

        string key1 = _storageService.ComputeMediaHash(path, fileSize, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        string key2 = _storageService.ComputeMediaHash(path, fileSize, new DateTime(2024, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public async Task ArtworkStorageService_AtomicSavePosterAndBackdrop_CreatesFilesCleanly()
    {
        string hashKey = "test_hash_" + Guid.NewGuid().ToString("N");
        var posterBytes = CreateSyntheticJpeg(600, 900);
        var backdropBytes = CreateSyntheticJpeg(1920, 1080);

        string posterPath = await _storageService.SavePosterAsync(hashKey, posterBytes);
        string backdropPath = await _storageService.SaveBackdropAsync(hashKey, backdropBytes);

        Assert.True(File.Exists(posterPath));
        Assert.True(File.Exists(backdropPath));

        Assert.Equal(posterPath, _storageService.GetPosterPath(hashKey));
        Assert.Equal(backdropPath, _storageService.GetBackdropPath(hashKey));

        Assert.True(_storageService.HasValidPoster(hashKey, out var existingPoster));
        Assert.Equal(posterPath, existingPoster);

        Assert.True(_storageService.HasValidBackdrop(hashKey, out var existingBackdrop));
        Assert.Equal(backdropPath, existingBackdrop);
    }

    #endregion

    #region Task 4: Embedded Artwork Extraction Tests

    [Fact]
    public async Task EmbeddedArtworkExtractor_ValidMp4CovrAtom_ExtractsArtwork()
    {
        // Synthesize an MP4 file with moov/udta/meta/ilst/covr/data structure
        var jpegData = CreateSyntheticJpeg(400, 600);
        string tempMp4 = Path.Combine(_tempDir, "sample_embedded.mp4");

        using (var fs = new FileStream(tempMp4, FileMode.Create, FileAccess.Write))
        using (var writer = new BinaryWriter(fs))
        {
            // ftyp atom
            writer.Write((int)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(20));
            writer.Write(Encoding.ASCII.GetBytes("ftyp"));
            writer.Write(Encoding.ASCII.GetBytes("mp42"));
            writer.Write((int)0);
            writer.Write(Encoding.ASCII.GetBytes("isom"));

            // moov atom payload
            using var moovMs = new MemoryStream();
            using var moovWriter = new BinaryWriter(moovMs);

            // udta atom
            using var udtaMs = new MemoryStream();
            using var udtaWriter = new BinaryWriter(udtaMs);

            // meta atom (4 bytes version/flags + ilst)
            using var metaMs = new MemoryStream();
            using var metaWriter = new BinaryWriter(metaMs);
            metaWriter.Write((int)0); // version/flags

            // ilst atom
            using var ilstMs = new MemoryStream();
            using var ilstWriter = new BinaryWriter(ilstMs);

            // covr atom
            using var covrMs = new MemoryStream();
            using var covrWriter = new BinaryWriter(covrMs);

            // data atom (8 bytes header + 8 bytes type/locale + payload)
            int dataLength = 8 + 8 + jpegData.Length;
            covrWriter.Write((int)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(dataLength));
            covrWriter.Write(Encoding.ASCII.GetBytes("data"));
            covrWriter.Write((int)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(13)); // type: 13 = jpeg
            covrWriter.Write((int)0); // locale
            covrWriter.Write(jpegData);

            byte[] covrPayload = covrMs.ToArray();
            ilstWriter.Write((int)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(8 + covrPayload.Length));
            ilstWriter.Write(Encoding.ASCII.GetBytes("covr"));
            ilstWriter.Write(covrPayload);

            byte[] ilstPayload = ilstMs.ToArray();
            metaWriter.Write((int)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(8 + ilstPayload.Length));
            metaWriter.Write(Encoding.ASCII.GetBytes("ilst"));
            metaWriter.Write(ilstPayload);

            byte[] metaPayload = metaMs.ToArray();
            udtaWriter.Write((int)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(8 + metaPayload.Length));
            udtaWriter.Write(Encoding.ASCII.GetBytes("meta"));
            udtaWriter.Write(metaPayload);

            byte[] udtaPayload = udtaMs.ToArray();
            moovWriter.Write((int)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(8 + udtaPayload.Length));
            moovWriter.Write(Encoding.ASCII.GetBytes("udta"));
            moovWriter.Write(udtaPayload);

            byte[] moovPayload = moovMs.ToArray();
            writer.Write((int)System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(8 + moovPayload.Length));
            writer.Write(Encoding.ASCII.GetBytes("moov"));
            writer.Write(moovPayload);
        }

        var extractor = new EmbeddedArtworkExtractor(NullLogger<EmbeddedArtworkExtractor>.Instance);
        var extracted = await extractor.ExtractEmbeddedArtworkAsync(tempMp4);

        Assert.NotNull(extracted);
        Assert.True(extracted.Length > 0);

        var header = ImageHeaderValidator.ValidateAndGetInfo(extracted);
        Assert.NotNull(header);
        Assert.Equal(400, header.Value.Width);
        Assert.Equal(600, header.Value.Height);
    }

    [Fact]
    public async Task EmbeddedArtworkExtractor_CorruptedFile_ReturnsNullSafely()
    {
        string corruptPath = Path.Combine(_tempDir, "corrupt.mkv");
        await File.WriteAllBytesAsync(corruptPath, new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x00, 0x11, 0x22 });

        var extractor = new EmbeddedArtworkExtractor(NullLogger<EmbeddedArtworkExtractor>.Instance);
        var result = await extractor.ExtractEmbeddedArtworkAsync(corruptPath);

        Assert.Null(result);
    }

    [Fact]
    public async Task EmbeddedArtworkExtractor_NonExistentFile_ReturnsNullSafely()
    {
        var extractor = new EmbeddedArtworkExtractor(NullLogger<EmbeddedArtworkExtractor>.Instance);
        var result = await extractor.ExtractEmbeddedArtworkAsync(@"Z:\NonExistent\FakeMovie.mp4");

        Assert.Null(result);
    }

    #endregion

    #region Task 5: Full Artwork Pipeline Coordination Tests

    private sealed class FakeEmbeddedExtractor : IEmbeddedArtworkExtractor
    {
        public byte[]? ArtworkToReturn { get; set; }

        public Task<byte[]?> ExtractEmbeddedArtworkAsync(string filePath, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ArtworkToReturn);
        }
    }

    private sealed class FakeFrameExtractor : IVideoFrameExtractor
    {
        public List<CandidateVideoFrame> Candidates { get; set; } = new();

        public Task<IReadOnlyList<CandidateVideoFrame>> ExtractCandidateFramesAsync(string filePath, long durationMs, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CandidateVideoFrame>>(Candidates);
        }
    }

    private sealed class PassThroughImageProcessor : IImageProcessingService
    {
        public Task<byte[]> CropAndScaleToPosterAsync(byte[] sourceImageBytes, int targetWidth, int targetHeight, CancellationToken ct = default)
        {
            return Task.FromResult(sourceImageBytes);
        }

        public Task<byte[]> CropAndScaleToBackdropAsync(byte[] sourceImageBytes, int targetWidth, int targetHeight, CancellationToken ct = default)
        {
            return Task.FromResult(sourceImageBytes);
        }

        public Task<(int width, int height, byte[] bgraPixels)> DecodeToBgraAsync(byte[] sourceImageBytes, CancellationToken ct = default)
        {
            return Task.FromResult((100, 100, new byte[100 * 100 * 4]));
        }
    }

    [Fact]
    public async Task ArtworkPipeline_WhenEmbeddedArtworkExists_ExtractsAndSavesPoster()
    {
        // 1. Arrange a movie in database
        var movie = new Movie
        {
            Title = "The Dark Knight",
            ReleaseYear = 2008,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        string sampleVideo = Path.Combine(_tempDir, "The.Dark.Knight.2008.mkv");
        await File.WriteAllBytesAsync(sampleVideo, new byte[1024]);

        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = sampleVideo,
            FileName = Path.GetFileName(sampleVideo),
            FileExtension = ".mkv",
            ContainerFormat = "mkv",
            SizeBytes = 1024,
            FileCreatedAt = DateTime.UtcNow,
            FileModifiedAt = DateTime.UtcNow,
            LastScannedAt = DateTime.UtcNow
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        var fakeEmbedded = new FakeEmbeddedExtractor
        {
            ArtworkToReturn = CreateSyntheticJpeg(600, 900)
        };
        var fakeFrames = new FakeFrameExtractor();
        var passThroughProcessor = new PassThroughImageProcessor();

        var pipeline = new ArtworkPipelineService(
            _movieRepository,
            _mediaFileRepository,
            fakeEmbedded,
            _qualityEvaluator,
            _storageService,
            fakeFrames,
            passThroughProcessor,
            NullLogger<ArtworkPipelineService>.Instance);

        int eventFiredMovieId = -1;
        pipeline.ArtworkUpdated += (id, poster, backdrop) => eventFiredMovieId = id;

        // 2. Act
        var result = await pipeline.ProcessMovieArtworkAsync(movie.Id);

        // 3. Assert
        Assert.NotNull(result);
        Assert.Equal(movie.Id, result.MovieId);
        Assert.NotNull(result.PosterPath);
        Assert.True(File.Exists(result.PosterPath));
        Assert.Equal(movie.Id, eventFiredMovieId);

        // Check database update
        var updatedMovie = await _movieRepository.GetByIdAsync(movie.Id);
        Assert.NotNull(updatedMovie);
        Assert.Equal(result.PosterPath, updatedMovie.PosterPath);
    }

    [Fact]
    public async Task ArtworkPipeline_WhenAlreadyCached_ReusesExistingArtworkWithoutExtraction()
    {
        var movie = new Movie
        {
            Title = "Interstellar",
            ReleaseYear = 2014,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        string sampleVideo = Path.Combine(_tempDir, "Interstellar.2014.mkv");
        await File.WriteAllBytesAsync(sampleVideo, new byte[2048]);

        var fileInfo = new FileInfo(sampleVideo);
        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = sampleVideo,
            FileName = Path.GetFileName(sampleVideo),
            FileExtension = ".mkv",
            ContainerFormat = "mkv",
            SizeBytes = 2048,
            FileCreatedAt = fileInfo.CreationTimeUtc,
            FileModifiedAt = fileInfo.LastWriteTimeUtc,
            LastScannedAt = DateTime.UtcNow
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        // Pre-populate storage cache
        string hashKey = _storageService.ComputeMediaHash(sampleVideo, fileInfo.Length, fileInfo.LastWriteTimeUtc);
        string existingPoster = await _storageService.SavePosterAsync(hashKey, CreateSyntheticJpeg(600, 900));
        string existingBackdrop = await _storageService.SaveBackdropAsync(hashKey, CreateSyntheticJpeg(1920, 1080));

        var fakeEmbedded = new FakeEmbeddedExtractor { ArtworkToReturn = null };
        var fakeFrames = new FakeFrameExtractor();
        var processor = new PassThroughImageProcessor();

        var pipeline = new ArtworkPipelineService(
            _movieRepository,
            _mediaFileRepository,
            fakeEmbedded,
            _qualityEvaluator,
            _storageService,
            fakeFrames,
            processor,
            NullLogger<ArtworkPipelineService>.Instance);

        // Act
        var result = await pipeline.ProcessMovieArtworkAsync(movie.Id);

        // Assert
        Assert.True(result.WasCached);
        Assert.Equal(existingPoster, result.PosterPath);
        Assert.Equal(existingBackdrop, result.BackdropPath);
    }

    [Fact]
    public async Task ArtworkPipeline_WhenNoEmbeddedAndNoFrames_SetsFallbackFlags()
    {
        var movie = new Movie
        {
            Title = "Mystery Film",
            ReleaseYear = 2020,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        await _movieRepository.InsertAsync(movie);

        string sampleVideo = Path.Combine(_tempDir, "Mystery.Film.2020.mkv");
        await File.WriteAllBytesAsync(sampleVideo, new byte[512]);

        var mediaFile = new MediaFile
        {
            MovieId = movie.Id,
            FilePath = sampleVideo,
            FileName = Path.GetFileName(sampleVideo),
            FileExtension = ".mkv",
            ContainerFormat = "mkv",
            SizeBytes = 512,
            FileCreatedAt = DateTime.UtcNow,
            FileModifiedAt = DateTime.UtcNow,
            LastScannedAt = DateTime.UtcNow
        };
        await _mediaFileRepository.InsertAsync(mediaFile);

        var fakeEmbedded = new FakeEmbeddedExtractor { ArtworkToReturn = null };
        var fakeFrames = new FakeFrameExtractor { Candidates = new List<CandidateVideoFrame>() };
        var processor = new PassThroughImageProcessor();

        var pipeline = new ArtworkPipelineService(
            _movieRepository,
            _mediaFileRepository,
            fakeEmbedded,
            _qualityEvaluator,
            _storageService,
            fakeFrames,
            processor,
            NullLogger<ArtworkPipelineService>.Instance);

        // Act
        var result = await pipeline.ProcessMovieArtworkAsync(movie.Id);

        // Assert
        Assert.True(result.IsPosterFallback);
        Assert.True(result.IsBackdropFallback);

        var updatedMovie = await _movieRepository.GetByIdAsync(movie.Id);
        Assert.NotNull(updatedMovie);
        Assert.True(updatedMovie.IsPosterFallback);
        Assert.True(updatedMovie.IsBackdropFallback);
    }

    #endregion
}
