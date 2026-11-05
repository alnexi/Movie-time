using System.Threading.Tasks;
using MovieTime.Core.Entities;
using SQLite;

namespace MovieTime.Core.Database.Migrations;

/// <summary>
/// Version 1 Migration: Creates foundational tables and indexes for movies, series,
/// seasons, episodes, media files, playback progress, and watch history.
/// </summary>
public sealed class InitialSchemaMigration : IDatabaseMigration
{
    public int TargetVersion => 1;

    public string Description => "Create initial schema tables and compound indices.";

    public async Task ApplyAsync(SQLiteAsyncConnection database)
    {
        // 1. Create all entity tables
        await database.CreateTableAsync<Movie>().ConfigureAwait(false);
        await database.CreateTableAsync<Series>().ConfigureAwait(false);
        await database.CreateTableAsync<Season>().ConfigureAwait(false);
        await database.CreateTableAsync<Episode>().ConfigureAwait(false);
        await database.CreateTableAsync<MediaFile>().ConfigureAwait(false);
        await database.CreateTableAsync<PlaybackProgress>().ConfigureAwait(false);
        await database.CreateTableAsync<WatchHistory>().ConfigureAwait(false);

        // 2. Create compound indexes for high-frequency queries
        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_Seasons_Series_Number ON Seasons(SeriesId, SeasonNumber);"
        ).ConfigureAwait(false);

        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_Episodes_Series_Season_Number ON Episodes(SeriesId, SeasonId, EpisodeNumber);"
        ).ConfigureAwait(false);

        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_MediaFiles_MovieId ON MediaFiles(MovieId);"
        ).ConfigureAwait(false);

        await database.ExecuteAsync(
            "CREATE INDEX IF NOT EXISTS IX_MediaFiles_EpisodeId ON MediaFiles(EpisodeId);"
        ).ConfigureAwait(false);
    }
}
