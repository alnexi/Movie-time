using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Database.Repositories;

public interface IEpisodeRepository
{
    Task<Episode?> GetByIdAsync(int id);
    Task<Episode?> GetBySeasonAndNumberAsync(int seasonId, int episodeNumber);
    Task<List<Episode>> GetBySeasonIdAsync(int seasonId);
    Task<List<Episode>> GetBySeriesIdAsync(int seriesId);
    Task<int> InsertAsync(Episode episode);
    Task<int> UpdateAsync(Episode episode);
    Task<int> DeleteAsync(int id);
}
