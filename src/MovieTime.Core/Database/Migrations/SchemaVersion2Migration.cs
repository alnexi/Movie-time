using System;
using System.Linq;
using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Migrations;

/// <summary>
/// Version 2 Migration: Creates the ScanRoots table for library folder management,
/// and adds ScanRootId and IsMissing columns and indexes to MediaFiles.
/// </summary>
public sealed class SchemaVersion2Migration : IDatabaseMigration
{
    public int TargetVersion => 2;

    public string Description => "Create ScanRoots table and add ScanRootId and IsMissing columns to MediaFiles.";

    public async Task ApplyAsync(SQLiteAsyncConnection database)
    {
        // 1. Create ScanRoots table
        await database.CreateTableAsync<ScanRoot>().ConfigureAwait(false);

        // 2. Safely add new columns to MediaFiles table if upgrading an existing version 1 database
        var columns = await database.QueryAsync<TableColumnInfo>("PRAGMA table_info(MediaFiles);").ConfigureAwait(false);

        var hasScanRootId = columns.Any(c => string.Equals(c.Name, "ScanRootId", StringComparison.OrdinalIgnoreCase));
        if (!hasScanRootId)
        {
            await database.ExecuteAsync("ALTER TABLE MediaFiles ADD COLUMN ScanRootId INTEGER;").ConfigureAwait(false);
        }

        var hasIsMissing = columns.Any(c => string.Equals(c.Name, "IsMissing", StringComparison.OrdinalIgnoreCase));
        if (!hasIsMissing)
        {
            await database.ExecuteAsync("ALTER TABLE MediaFiles ADD COLUMN IsMissing INTEGER NOT NULL DEFAULT 0;").ConfigureAwait(false);
        }

        // 3. Create indices for newly introduced query patterns
        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_MediaFiles_IsMissing ON MediaFiles(IsMissing);"
        ).ConfigureAwait(false);

        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_MediaFiles_ScanRootId ON MediaFiles(ScanRootId);"
        ).ConfigureAwait(false);

        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_ScanRoots_NormalizedPath ON ScanRoots(NormalizedPath);"
        ).ConfigureAwait(false);
    }

    private sealed class TableColumnInfo
    {
        public string Name { get; set; } = string.Empty;
    }
}
