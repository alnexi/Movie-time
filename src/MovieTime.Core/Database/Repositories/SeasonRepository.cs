using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Repositories;

public sealed class SeasonRepository : ISeasonRepository
{
    private readonly IDatabaseService _databaseService;

    public SeasonRepository(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    private SQLiteAsyncConnection Connection => _databaseService.GetConnection();

    public async Task<Season?> GetByIdAsync(int id)
    {
        return await Connection.FindAsync<Season>(id).ConfigureAwait(false);
    }

    public async Task<Season?> GetBySeriesAndNumberAsync(int seriesId, int seasonNumber)
    {
        return await Connection.FindWithQueryAsync<Season>(
            "SELECT * FROM Seasons WHERE SeriesId = ? AND SeasonNumber = ? LIMIT 1;",
            seriesId, seasonNumber).ConfigureAwait(false);
    }

    public Task<List<Season>> GetBySeriesIdAsync(int seriesId)
    {
        return Connection.Table<Season>()
            .Where(s => s.SeriesId == seriesId)
            .OrderBy(s => s.SeasonNumber)
            .ToListAsync();
    }

    public Task<int> InsertAsync(Season season)
    {
        ArgumentNullException.ThrowIfNull(season);
        return Connection.InsertAsync(season);
    }

    public Task<int> UpdateAsync(Season season)
    {
        ArgumentNullException.ThrowIfNull(season);
        return Connection.UpdateAsync(season);
    }

    public Task<int> DeleteAsync(int id)
    {
        return Connection.DeleteAsync<Season>(id);
    }
}
