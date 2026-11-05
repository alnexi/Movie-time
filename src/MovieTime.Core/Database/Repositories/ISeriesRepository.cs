using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Database.Repositories;

public interface ISeriesRepository
{
    Task<Series?> GetByIdAsync(int id);
    Task<Series?> FindByTitleAsync(string title);
    Task<List<Series>> GetAllAsync();
    Task<int> InsertAsync(Series series);
    Task<int> UpdateAsync(Series series);
    Task<int> DeleteAsync(int id);
}
