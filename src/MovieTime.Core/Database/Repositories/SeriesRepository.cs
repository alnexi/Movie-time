using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Repositories;

public sealed class SeriesRepository : ISeriesRepository
{
    private readonly IDatabaseService _databaseService;

    public SeriesRepository(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    private SQLiteAsyncConnection Connection => _databaseService.GetConnection();

    public async Task<Series?> GetByIdAsync(int id)
    {
        return await Connection.FindAsync<Series>(id).ConfigureAwait(false);
    }

    public async Task<Series?> FindByTitleAsync(string title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        return await Connection.FindWithQueryAsync<Series>(
            "SELECT * FROM Series WHERE Title = ? COLLATE NOCASE LIMIT 1;",
            title.Trim()).ConfigureAwait(false);
    }

    public Task<List<Series>> GetAllAsync()
    {
        return Connection.Table<Series>().ToListAsync();
    }

    public Task<int> InsertAsync(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return Connection.InsertAsync(series);
    }

    public Task<int> UpdateAsync(Series series)
    {
        ArgumentNullException.ThrowIfNull(series);
        return Connection.UpdateAsync(series);
    }

    public Task<int> DeleteAsync(int id)
    {
        return Connection.DeleteAsync<Series>(id);
    }
}
