using System;
using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Repositories;

/// <summary>
/// SQLite implementation of <see cref="IPlaybackProgressRepository"/>.
/// </summary>
public sealed class PlaybackProgressRepository : IPlaybackProgressRepository
{
    private readonly IDatabaseService _databaseService;

    public PlaybackProgressRepository(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    private SQLiteAsyncConnection Connection => _databaseService.GetConnection();

    public async Task<PlaybackProgress?> GetByMediaFileIdAsync(int mediaFileId)
    {
        return await Connection.Table<PlaybackProgress>()
            .Where(p => p.MediaFileId == mediaFileId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task<PlaybackProgress?> GetByMovieIdAsync(int movieId)
    {
        return await Connection.Table<PlaybackProgress>()
            .Where(p => p.MovieId == movieId)
            .OrderByDescending(p => p.LastWatchedAt)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);
    }

    public async Task<int> SaveProgressAsync(int mediaFileId, int? movieId, long positionTicks, long durationTicks, bool isFinished)
    {
        var existing = await GetByMediaFileIdAsync(mediaFileId).ConfigureAwait(false);
        var percentage = durationTicks > 0 ? (double)positionTicks / durationTicks : 0.0;

        if (existing == null)
        {
            var progress = new PlaybackProgress
            {
                MediaFileId = mediaFileId,
                MovieId = movieId,
                PositionTicks = positionTicks,
                DurationTicks = durationTicks,
                PositionPercentage = percentage,
                IsFinished = isFinished,
                LastWatchedAt = DateTime.UtcNow,
                PlayCount = 1
            };
            return await Connection.InsertOrReplaceAsync(progress).ConfigureAwait(false);
        }

        existing.MovieId = movieId ?? existing.MovieId;
        existing.PositionTicks = positionTicks;
        existing.DurationTicks = durationTicks > 0 ? durationTicks : existing.DurationTicks;
        existing.PositionPercentage = percentage;
        existing.IsFinished = isFinished;
        existing.LastWatchedAt = DateTime.UtcNow;
        if (isFinished)
        {
            existing.PlayCount++;
        }
        return await Connection.InsertOrReplaceAsync(existing).ConfigureAwait(false);
    }

    public async Task<int> DeleteByMediaFileIdAsync(int mediaFileId)
    {
        var existing = await GetByMediaFileIdAsync(mediaFileId).ConfigureAwait(false);
        if (existing != null)
        {
            return await Connection.DeleteAsync(existing).ConfigureAwait(false);
        }
        return 0;
    }
}
