using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Repositories;

public sealed class PlaylistRepository : IPlaylistRepository
{
    private readonly IDatabaseService _databaseService;

    public PlaylistRepository(IDatabaseService databaseService)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
    }

    private SQLiteAsyncConnection Connection => _databaseService.GetConnection();

    public async Task<List<Playlist>> GetAllAsync()
    {
        return await Connection.Table<Playlist>()
            .OrderByDescending(p => p.UpdatedAtUtc)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<Playlist?> GetByIdAsync(int id)
    {
        return await Connection.FindAsync<Playlist>(id).ConfigureAwait(false);
    }

    public async Task<int> InsertAsync(Playlist playlist)
    {
        playlist.CreatedAtUtc = DateTime.UtcNow;
        playlist.UpdatedAtUtc = DateTime.UtcNow;
        return await Connection.InsertAsync(playlist).ConfigureAwait(false);
    }

    public async Task<int> UpdateAsync(Playlist playlist)
    {
        playlist.UpdatedAtUtc = DateTime.UtcNow;
        return await Connection.UpdateAsync(playlist).ConfigureAwait(false);
    }

    public async Task<int> DeleteAsync(int id)
    {
        // Delete items belonging to this playlist first
        await Connection.ExecuteAsync("DELETE FROM PlaylistItems WHERE PlaylistId = ?;", id).ConfigureAwait(false);
        return await Connection.DeleteAsync<Playlist>(id).ConfigureAwait(false);
    }

    public async Task<List<PlaylistItem>> GetItemsByPlaylistIdAsync(int playlistId)
    {
        return await Connection.Table<PlaylistItem>()
            .Where(pi => pi.PlaylistId == playlistId)
            .OrderBy(pi => pi.SortOrder)
            .ThenBy(pi => pi.Id)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    public async Task<int> AddItemAsync(int playlistId, int movieId)
    {
        // Check if already in playlist
        var existing = await Connection.Table<PlaylistItem>()
            .Where(pi => pi.PlaylistId == playlistId && pi.MovieId == movieId)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (existing != null)
        {
            return existing.Id;
        }

        // Determine max sort order
        var items = await GetItemsByPlaylistIdAsync(playlistId).ConfigureAwait(false);
        var maxOrder = items.Count > 0 ? items.Max(i => i.SortOrder) + 1 : 0;

        var newItem = new PlaylistItem
        {
            PlaylistId = playlistId,
            MovieId = movieId,
            SortOrder = maxOrder,
            AddedAtUtc = DateTime.UtcNow
        };

        await Connection.InsertAsync(newItem).ConfigureAwait(false);

        // Update playlist UpdatedAt
        await Connection.ExecuteAsync(
            "UPDATE Playlists SET UpdatedAtUtc = ? WHERE Id = ?;",
            DateTime.UtcNow, playlistId).ConfigureAwait(false);

        return newItem.Id;
    }

    public async Task<int> RemoveItemAsync(int playlistItemId)
    {
        var item = await Connection.FindAsync<PlaylistItem>(playlistItemId).ConfigureAwait(false);
        if (item == null) return 0;

        var result = await Connection.DeleteAsync<PlaylistItem>(playlistItemId).ConfigureAwait(false);

        await Connection.ExecuteAsync(
            "UPDATE Playlists SET UpdatedAtUtc = ? WHERE Id = ?;",
            DateTime.UtcNow, item.PlaylistId).ConfigureAwait(false);

        return result;
    }

    public async Task<int> RemoveMovieFromPlaylistAsync(int playlistId, int movieId)
    {
        var result = await Connection.ExecuteAsync(
            "DELETE FROM PlaylistItems WHERE PlaylistId = ? AND MovieId = ?;",
            playlistId, movieId).ConfigureAwait(false);

        await Connection.ExecuteAsync(
            "UPDATE Playlists SET UpdatedAtUtc = ? WHERE Id = ?;",
            DateTime.UtcNow, playlistId).ConfigureAwait(false);

        return result;
    }

    public async Task UpdateItemOrdersAsync(int playlistId, List<int> playlistItemIdsInOrder)
    {
        if (playlistItemIdsInOrder == null || playlistItemIdsInOrder.Count == 0) return;

        for (int i = 0; i < playlistItemIdsInOrder.Count; i++)
        {
            await Connection.ExecuteAsync(
                "UPDATE PlaylistItems SET SortOrder = ? WHERE Id = ? AND PlaylistId = ?;",
                i, playlistItemIdsInOrder[i], playlistId).ConfigureAwait(false);
        }

        await Connection.ExecuteAsync(
            "UPDATE Playlists SET UpdatedAtUtc = ? WHERE Id = ?;",
            DateTime.UtcNow, playlistId).ConfigureAwait(false);
    }
}
