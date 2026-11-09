using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Migrations;

/// <summary>
/// Version 3 Migration: Creates Playlists and PlaylistItems tables with indexes.
/// </summary>
public sealed class SchemaVersion3Migration : IDatabaseMigration
{
    public int TargetVersion => 3;

    public string Description => "Create Playlists and PlaylistItems tables for local persistent playlists.";

    public async Task ApplyAsync(SQLiteAsyncConnection database)
    {
        await database.CreateTableAsync<Playlist>().ConfigureAwait(false);
        await database.CreateTableAsync<PlaylistItem>().ConfigureAwait(false);

        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_PlaylistItems_PlaylistId ON PlaylistItems(PlaylistId);"
        ).ConfigureAwait(false);

        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_PlaylistItems_MovieId ON PlaylistItems(MovieId);"
        ).ConfigureAwait(false);

        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_PlaylistItems_SortOrder ON PlaylistItems(SortOrder);"
        ).ConfigureAwait(false);
    }
}
