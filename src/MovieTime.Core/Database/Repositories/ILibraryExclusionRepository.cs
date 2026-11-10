using System.Collections.Generic;
using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Database.Repositories;

/// <summary>
/// Repository for managing persistent library media exclusions.
/// </summary>
public interface ILibraryExclusionRepository
{
    Task<LibraryExclusion?> GetByPathAsync(string path);
    Task<bool> IsPathExcludedAsync(string path);
    Task<IReadOnlyList<LibraryExclusion>> GetAllAsync();
    Task<int> AddExclusionAsync(string path, string? relativePath = null, string? reason = "UserRemoved");
    Task<bool> RemoveExclusionAsync(string path);
}
