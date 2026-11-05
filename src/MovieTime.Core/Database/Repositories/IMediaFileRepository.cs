using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Database.Repositories;

public interface IMediaFileRepository
{
    Task<MediaFile?> GetByIdAsync(int id);
    Task<MediaFile?> GetByPathAsync(string normalizedPath);
    Task<List<MediaFile>> GetByMovieIdAsync(int movieId);
    Task<List<MediaFile>> GetByEpisodeIdAsync(int episodeId);
    Task<List<MediaFile>> GetByScanRootIdAsync(int scanRootId);
    Task<List<MediaFile>> GetByPathPrefixAsync(string pathPrefix);
    Task<List<MediaFile>> GetAllAsync();
    Task<int> InsertAsync(MediaFile mediaFile);
    Task<int> UpdateAsync(MediaFile mediaFile);
    Task<int> DeleteAsync(int id);
}
