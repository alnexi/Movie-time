using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Repositories;

public sealed class MediaFileRepository : IMediaFileRepository
{
    private readonly IDatabaseService _databaseService;

    public MediaFileRepository(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    private SQLiteAsyncConnection Connection => _databaseService.GetConnection();

    public async Task<MediaFile?> GetByIdAsync(int id)
    {
        return await Connection.FindAsync<MediaFile>(id).ConfigureAwait(false);
    }

    public async Task<MediaFile?> GetByPathAsync(string normalizedPath)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return null;
        }

        return await Connection.FindWithQueryAsync<MediaFile>(
            "SELECT * FROM MediaFiles WHERE FilePath = ? COLLATE NOCASE LIMIT 1;",
            normalizedPath.Trim()).ConfigureAwait(false);
    }

    public Task<List<MediaFile>> GetByMovieIdAsync(int movieId)
    {
        return Connection.Table<MediaFile>()
            .Where(m => m.MovieId == movieId)
            .ToListAsync();
    }

    public Task<List<MediaFile>> GetByEpisodeIdAsync(int episodeId)
    {
        return Connection.Table<MediaFile>()
            .Where(m => m.EpisodeId == episodeId)
            .ToListAsync();
    }

    public Task<List<MediaFile>> GetByScanRootIdAsync(int scanRootId)
    {
        return Connection.Table<MediaFile>()
            .Where(m => m.ScanRootId == scanRootId)
            .ToListAsync();
    }

    public Task<List<MediaFile>> GetByPathPrefixAsync(string pathPrefix)
    {
        if (string.IsNullOrWhiteSpace(pathPrefix))
        {
            return Task.FromResult(new List<MediaFile>());
        }

        var prefixPattern = pathPrefix.TrimEnd('\\', '/') + "%";
        return Connection.QueryAsync<MediaFile>(
            "SELECT * FROM MediaFiles WHERE FilePath LIKE ? COLLATE NOCASE;",
            prefixPattern);
    }

    public Task<List<MediaFile>> GetAllAsync()
    {
        return Connection.Table<MediaFile>().ToListAsync();
    }

    public Task<int> InsertAsync(MediaFile mediaFile)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        return Connection.InsertAsync(mediaFile);
    }

    public Task<int> UpdateAsync(MediaFile mediaFile)
    {
        ArgumentNullException.ThrowIfNull(mediaFile);
        return Connection.UpdateAsync(mediaFile);
    }

    public Task<int> DeleteAsync(int id)
    {
        return Connection.DeleteAsync<MediaFile>(id);
    }
}
