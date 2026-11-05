using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using SQLite;

namespace MovieTime.Core.Database;

/// <summary>
/// Provides local, thread-safe asynchronous SQLite database connectivity, schema migrations,
/// and core CRUD operations for Movie Time.
/// </summary>
public interface IDatabaseService : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Gets the absolute file path to the active SQLite database file.
    /// </summary>
    string DatabasePath { get; }

    /// <summary>
    /// Gets a value indicating whether the database has completed schema initialization.
    /// </summary>
    bool IsInitialized { get; }

    /// <summary>
    /// Gets the active schema version stored in SQLite user_version.
    /// </summary>
    int CurrentSchemaVersion { get; }

    /// <summary>
    /// Initializes the database engine, applies pending migrations, and configures PRAGMA settings.
    /// Safe against duplicate concurrent calls.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    Task InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the underlying <see cref="SQLiteAsyncConnection"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if called before <see cref="InitializeAsync"/>.</exception>
    SQLiteAsyncConnection GetConnection();

    /// <summary>
    /// Inserts a new entity into its mapped table.
    /// </summary>
    Task<int> InsertAsync<T>(T entity) where T : new();

    /// <summary>
    /// Updates an existing entity in its mapped table.
    /// </summary>
    Task<int> UpdateAsync<T>(T entity) where T : new();

    /// <summary>
    /// Deletes an entity from its mapped table.
    /// </summary>
    Task<int> DeleteAsync<T>(T entity) where T : new();

    /// <summary>
    /// Finds and returns an entity by its integer primary key.
    /// </summary>
    Task<T?> GetByIdAsync<T>(int id) where T : new();

    /// <summary>
    /// Returns all records for a given entity type.
    /// </summary>
    Task<List<T>> GetAllAsync<T>() where T : new();

    /// <summary>
    /// Returns a queryable table expression for LINQ queries.
    /// </summary>
    AsyncTableQuery<T> Table<T>() where T : new();
}
