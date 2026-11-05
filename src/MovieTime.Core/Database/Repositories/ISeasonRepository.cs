using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Database.Repositories;

public interface ISeasonRepository
{
    Task<Season?> GetByIdAsync(int id);
    Task<Season?> GetBySeriesAndNumberAsync(int seriesId, int seasonNumber);
    Task<List<Season>> GetBySeriesIdAsync(int seriesId);
    Task<int> InsertAsync(Season season);
    Task<int> UpdateAsync(Season season);
    Task<int> DeleteAsync(int id);
}
