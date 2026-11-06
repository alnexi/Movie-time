using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class PlaybackProgressRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _databaseService;
    private readonly PlaybackProgressRepository _repository;

    public PlaybackProgressRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PlaybackProgressTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_progress.db");

        var storage = new StoragePathService(_tempDir);
        _databaseService = new DatabaseService(storage, NullLogger<DatabaseService>.Instance, _dbPath);
        _databaseService.InitializeAsync().GetAwaiter().GetResult();

        _repository = new PlaybackProgressRepository(_databaseService);
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
            // best effort
        }
    }

    [Fact]
    public async Task SaveProgressAsync_WhenNew_InsertsRecordCorrectly()
    {
        long posTicks = TimeSpan.FromMinutes(25).Ticks;
        long durTicks = TimeSpan.FromMinutes(100).Ticks;

        var result = await _repository.SaveProgressAsync(
            mediaFileId: 42,
            movieId: 10,
            positionTicks: posTicks,
            durationTicks: durTicks,
            isFinished: false);

        Assert.Equal(1, result);

        var loaded = await _repository.GetByMediaFileIdAsync(42);
        Assert.NotNull(loaded);
        Assert.Equal(42, loaded.MediaFileId);
        Assert.Equal(10, loaded.MovieId);
        Assert.Equal(posTicks, loaded.PositionTicks);
        Assert.Equal(durTicks, loaded.DurationTicks);
        Assert.Equal(0.25, loaded.PositionPercentage, precision: 2);
        Assert.False(loaded.IsFinished);
        Assert.Equal(1, loaded.PlayCount);
    }

    [Fact]
    public async Task SaveProgressAsync_WhenExisting_UpdatesRecordCorrectly()
    {
        long durTicks = TimeSpan.FromHours(2).Ticks;

        // First watch session
        await _repository.SaveProgressAsync(101, 5, TimeSpan.FromMinutes(30).Ticks, durTicks, false);

        // Second watch session further into the movie
        long updatedTicks = TimeSpan.FromMinutes(75).Ticks;
        await _repository.SaveProgressAsync(101, 5, updatedTicks, durTicks, false);

        var loaded = await _repository.GetByMediaFileIdAsync(101);
        Assert.NotNull(loaded);
        Assert.Equal(updatedTicks, loaded.PositionTicks);
        Assert.Equal(0.625, loaded.PositionPercentage, precision: 3);
        Assert.False(loaded.IsFinished);
    }

    [Fact]
    public async Task SaveProgressAsync_WhenFinished_MarksFinishedAndIncrementsPlayCount()
    {
        long durTicks = TimeSpan.FromMinutes(90).Ticks;

        await _repository.SaveProgressAsync(202, 8, TimeSpan.FromMinutes(88).Ticks, durTicks, false);
        await _repository.SaveProgressAsync(202, 8, durTicks, durTicks, true);

        var loaded = await _repository.GetByMediaFileIdAsync(202);
        Assert.NotNull(loaded);
        Assert.True(loaded.IsFinished);
        Assert.Equal(2, loaded.PlayCount);
    }

    [Fact]
    public async Task GetByMovieIdAsync_ReturnsMostRecentPlaybackRecord()
    {
        long dur = TimeSpan.FromHours(2).Ticks;

        // Old progress for file A of movie 7
        await _repository.SaveProgressAsync(301, 7, TimeSpan.FromMinutes(10).Ticks, dur, false);

        await Task.Delay(20);

        // Recent progress for file B of movie 7
        await _repository.SaveProgressAsync(302, 7, TimeSpan.FromMinutes(45).Ticks, dur, false);

        var loaded = await _repository.GetByMovieIdAsync(7);
        Assert.NotNull(loaded);
        Assert.Equal(302, loaded.MediaFileId);
    }

    [Fact]
    public async Task DeleteByMediaFileIdAsync_RemovesRecordSuccessfully()
    {
        await _repository.SaveProgressAsync(404, 12, TimeSpan.FromMinutes(50).Ticks, TimeSpan.FromHours(2).Ticks, false);

        var beforeDelete = await _repository.GetByMediaFileIdAsync(404);
        Assert.NotNull(beforeDelete);

        var deleteResult = await _repository.DeleteByMediaFileIdAsync(404);
        Assert.Equal(1, deleteResult);

        var afterDelete = await _repository.GetByMediaFileIdAsync(404);
        Assert.Null(afterDelete);
    }
}
