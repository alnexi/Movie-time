using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Repositories;

public sealed class ScanRootRepository : IScanRootRepository
{
    private readonly IDatabaseService _databaseService;

    public ScanRootRepository(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    private SQLiteAsyncConnection Connection => _databaseService.GetConnection();

    public Task<List<ScanRoot>> GetAllAsync()
    {
        return Connection.Table<ScanRoot>().ToListAsync();
    }

    public async Task<ScanRoot?> GetByIdAsync(int id)
    {
        return await Connection.FindAsync<ScanRoot>(id).ConfigureAwait(false);
    }

    public async Task<ScanRoot?> GetByNormalizedPathAsync(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        return await Connection.FindWithQueryAsync<ScanRoot>(
            "SELECT * FROM ScanRoots WHERE NormalizedPath = ? COLLATE NOCASE LIMIT 1;",
            normalizedPath.Trim()).ConfigureAwait(false);
    }

    public Task<int> InsertAsync(ScanRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Connection.InsertAsync(root);
    }

    public Task<int> UpdateAsync(ScanRoot root)
    {
        ArgumentNullException.ThrowIfNull(root);
        return Connection.UpdateAsync(root);
    }

    public Task<int> DeleteAsync(int id)
    {
        return Connection.DeleteAsync<ScanRoot>(id);
    }
}
