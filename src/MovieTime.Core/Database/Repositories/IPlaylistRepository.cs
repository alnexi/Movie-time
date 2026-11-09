using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Database.Repositories;

public interface IPlaylistRepository
{
    Task<List<Playlist>> GetAllAsync();
    Task<Playlist?> GetByIdAsync(int id);
    Task<int> InsertAsync(Playlist playlist);
    Task<int> UpdateAsync(Playlist playlist);
    Task<int> DeleteAsync(int id);

    Task<List<PlaylistItem>> GetItemsByPlaylistIdAsync(int playlistId);
    Task<int> AddItemAsync(int playlistId, int movieId);
    Task<int> RemoveItemAsync(int playlistItemId);
    Task<int> RemoveMovieFromPlaylistAsync(int playlistId, int movieId);
    Task UpdateItemOrdersAsync(int playlistId, List<int> playlistItemIdsInOrder);
}
