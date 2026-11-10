using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Repositories;

/// <summary>
/// SQLite implementation of <see cref="ILibraryExclusionRepository"/>.
/// </summary>
public sealed class LibraryExclusionRepository : ILibraryExclusionRepository
{
    private readonly IDatabaseService _databaseService;
    private readonly ILogger<LibraryExclusionRepository> _logger;

    public LibraryExclusionRepository(
        IDatabaseService databaseService,
        ILogger<LibraryExclusionRepository>? logger = null)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _logger = logger ?? NullLogger<LibraryExclusionRepository>.Instance;
    }

    private static string NormalizePath(string path)
    {
        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    public async Task<LibraryExclusion?> GetByPathAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var normalized = NormalizePath(path);

        var conn = _databaseService.GetConnection();
        var direct = await conn.Table<LibraryExclusion>()
            .Where(e => e.Path == normalized)
            .FirstOrDefaultAsync()
            .ConfigureAwait(false);

        if (direct != null) return direct;

        // Fallback case-insensitive check
        var all = await conn.Table<LibraryExclusion>().ToListAsync().ConfigureAwait(false);
        foreach (var e in all)
        {
            if (string.Equals(NormalizePath(e.Path), normalized, StringComparison.OrdinalIgnoreCase))
            {
                return e;
            }
        }

        return null;
    }

    public async Task<bool> IsPathExcludedAsync(string path)
    {
        var exclusion = await GetByPathAsync(path).ConfigureAwait(false);
        return exclusion != null;
    }

    public async Task<IReadOnlyList<LibraryExclusion>> GetAllAsync()
    {
        var conn = _databaseService.GetConnection();
        var list = await conn.Table<LibraryExclusion>()
            .OrderByDescending(e => e.ExcludedAt)
            .ToListAsync()
            .ConfigureAwait(false);
        return list;
    }

    public async Task<int> AddExclusionAsync(string path, string? relativePath = null, string? reason = "UserRemoved")
    {
        if (string.IsNullOrWhiteSpace(path)) return 0;
        var normalized = NormalizePath(path);

        var existing = await GetByPathAsync(normalized).ConfigureAwait(false);
        if (existing != null)
        {
            return existing.Id;
        }

        var entity = new LibraryExclusion
        {
            Path = normalized,
            RelativePath = relativePath,
            Reason = reason,
            ExcludedAt = DateTime.UtcNow
        };

        var conn = _databaseService.GetConnection();
        await conn.InsertAsync(entity).ConfigureAwait(false);
        _logger.LogInformation("Persistent library exclusion added for {Path}", normalized);
        return entity.Id;
    }

    public async Task<bool> RemoveExclusionAsync(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        var existing = await GetByPathAsync(path).ConfigureAwait(false);
        if (existing == null) return false;

        var conn = _databaseService.GetConnection();
        int deleted = await conn.DeleteAsync(existing).ConfigureAwait(false);
        _logger.LogInformation("Persistent library exclusion removed for {Path}", existing.Path);
        return deleted > 0;
    }
}
