using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Database.Repositories;

public interface IScanRootRepository
{
    Task<List<ScanRoot>> GetAllAsync();
    Task<ScanRoot?> GetByIdAsync(int id);
    Task<ScanRoot?> GetByNormalizedPathAsync(string normalizedPath);
    Task<int> InsertAsync(ScanRoot root);
    Task<int> UpdateAsync(ScanRoot root);
    Task<int> DeleteAsync(int id);
}
