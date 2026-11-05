using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Repositories;

public sealed class EpisodeRepository : IEpisodeRepository
{
    private readonly IDatabaseService _databaseService;

    public EpisodeRepository(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    private SQLiteAsyncConnection Connection => _databaseService.GetConnection();

    public async Task<Episode?> GetByIdAsync(int id)
    {
        return await Connection.FindAsync<Episode>(id).ConfigureAwait(false);
    }

    public async Task<Episode?> GetBySeasonAndNumberAsync(int seasonId, int episodeNumber)
    {
        return await Connection.FindWithQueryAsync<Episode>(
            "SELECT * FROM Episodes WHERE SeasonId = ? AND EpisodeNumber = ? LIMIT 1;",
            seasonId, episodeNumber).ConfigureAwait(false);
    }

    public Task<List<Episode>> GetBySeasonIdAsync(int seasonId)
    {
        return Connection.Table<Episode>()
            .Where(e => e.SeasonId == seasonId)
            .OrderBy(e => e.EpisodeNumber)
            .ToListAsync();
    }

    public Task<List<Episode>> GetBySeriesIdAsync(int seriesId)
    {
        return Connection.Table<Episode>()
            .Where(e => e.SeriesId == seriesId)
            .OrderBy(e => e.SeasonId)
            .ThenBy(e => e.EpisodeNumber)
            .ToListAsync();
    }

    public Task<int> InsertAsync(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return Connection.InsertAsync(episode);
    }

    public Task<int> UpdateAsync(Episode episode)
    {
        ArgumentNullException.ThrowIfNull(episode);
        return Connection.UpdateAsync(episode);
    }

    public Task<int> DeleteAsync(int id)
    {
        return Connection.DeleteAsync<Episode>(id);
    }
}
