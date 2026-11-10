using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Migrations;

/// <summary>
/// Version 4 Migration: Creates library_exclusions table and indexes for persistent media exclusion.
/// </summary>
public sealed class SchemaVersion4Migration : IDatabaseMigration
{
    public int TargetVersion => 4;

    public string Description => "Create library_exclusions table for persistent user media exclusions.";

    public async Task ApplyAsync(SQLiteAsyncConnection database)
    {
        await database.CreateTableAsync<LibraryExclusion>().ConfigureAwait(false);

        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_library_exclusions_Path ON library_exclusions(Path);"
        ).ConfigureAwait(false);
    }
}
