using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Database.Repositories;

public interface IMovieRepository
{
    Task<Movie?> GetByIdAsync(int id);
    Task<Movie?> FindByTitleAndYearAsync(string title, int? year);
    Task<List<Movie>> FindAllByTitleAndYearAsync(string title, int? year);
    Task<List<Movie>> GetAllAsync();
    Task<int> InsertAsync(Movie movie);
    Task<int> UpdateAsync(Movie movie);
    Task<int> DeleteAsync(int id);
}
