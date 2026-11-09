using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Repositories;

public sealed class MovieRepository : IMovieRepository
{
    private readonly IDatabaseService _databaseService;

    public MovieRepository(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    private SQLiteAsyncConnection Connection => _databaseService.GetConnection();

    public async Task<Movie?> GetByIdAsync(int id)
    {
        return await Connection.FindAsync<Movie>(id).ConfigureAwait(false);
    }

    public async Task<Movie?> FindByTitleAndYearAsync(string title, int? year)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var cleanTitle = title.Trim();
        if (year.HasValue)
        {
            return await Connection.FindWithQueryAsync<Movie>(
                "SELECT * FROM Movies WHERE Title = ? COLLATE NOCASE AND ReleaseYear = ? LIMIT 1;",
                cleanTitle, year.Value).ConfigureAwait(false);
        }

        return await Connection.FindWithQueryAsync<Movie>(
            "SELECT * FROM Movies WHERE Title = ? COLLATE NOCASE AND ReleaseYear IS NULL LIMIT 1;",
            cleanTitle).ConfigureAwait(false);
    }

    public async Task<List<Movie>> FindAllByTitleAndYearAsync(string title, int? year)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return new List<Movie>();
        }

        var cleanTitle = title.Trim();
        if (year.HasValue)
        {
            return await Connection.QueryAsync<Movie>(
                "SELECT * FROM Movies WHERE Title = ? COLLATE NOCASE AND ReleaseYear = ?;",
                cleanTitle, year.Value).ConfigureAwait(false);
        }

        return await Connection.QueryAsync<Movie>(
            "SELECT * FROM Movies WHERE Title = ? COLLATE NOCASE AND ReleaseYear IS NULL;",
            cleanTitle).ConfigureAwait(false);
    }

    public Task<List<Movie>> GetAllAsync()
    {
        return Connection.Table<Movie>().ToListAsync();
    }

    public Task<int> InsertAsync(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return Connection.InsertAsync(movie);
    }

    public Task<int> UpdateAsync(Movie movie)
    {
        ArgumentNullException.ThrowIfNull(movie);
        return Connection.UpdateAsync(movie);
    }

    public Task<int> DeleteAsync(int id)
    {
        return Connection.DeleteAsync<Movie>(id);
    }
}
