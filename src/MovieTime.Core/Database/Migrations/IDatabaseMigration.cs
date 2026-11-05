using System.Threading.Tasks;
using SQLite;

namespace MovieTime.Core.Database.Migrations;

/// <summary>
/// Defines a versioned database migration step.
/// </summary>
public interface IDatabaseMigration
{
    /// <summary>
    /// The target schema version this migration transitions the database to.
    /// </summary>
    int TargetVersion { get; }

    /// <summary>
    /// Human-readable description of the migration.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Applies schema changes asynchronously.
    /// </summary>
    /// <param name="database">The active SQLite connection.</param>
    Task ApplyAsync(SQLiteAsyncConnection database);
}
