using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using MovieTime.Core.Database.Migrations;
using MovieTime.Core.Services;
using SQLite;

namespace MovieTime.Core.Database;

/// <summary>
/// Default implementation of <see cref="IDatabaseService"/> backed by sqlite-net-pcl and SQLitePCLRaw.
/// Guarantees offline local persistence, idempotent initialization, WAL journal mode, and schema migrations.
/// </summary>
public sealed class DatabaseService : IDatabaseService
{
    private readonly IStoragePathService _storagePathService;
    private readonly ILogger<DatabaseService> _logger;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly List<IDatabaseMigration> _migrations;

    private SQLiteAsyncConnection? _connection;
    private volatile bool _isInitialized;
    private int _currentSchemaVersion;
    private bool _disposed;

    public string DatabasePath { get; }
    public bool IsInitialized => _isInitialized;
    public int CurrentSchemaVersion => _currentSchemaVersion;

    private SQLiteAsyncConnection Connection =>
        _connection ?? throw new InvalidOperationException("Database has not been initialized. Call InitializeAsync() first.");

    public DatabaseService(
        IStoragePathService storagePathService,
        ILogger<DatabaseService> logger,
        string? customDatabasePath = null)
    {
        _storagePathService = storagePathService ?? throw new ArgumentNullException(nameof(storagePathService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        DatabasePath = !string.IsNullOrWhiteSpace(customDatabasePath)
            ? customDatabasePath
            : _storagePathService.DatabaseFilePath;

        // Register available migrations in ascending version order
        _migrations = new List<IDatabaseMigration>
        {
            new InitialSchemaMigration(),
            new SchemaVersion2Migration(),
            new SchemaVersion3Migration()
        };
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_isInitialized)
        {
            return;
        }

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_isInitialized)
            {
                return;
            }

            _logger.LogDebug("Initializing SQLite database engine for path: {DatabasePath}", DatabasePath);

            // 1. Ensure target directory exists on disk
            var directory = Path.GetDirectoryName(DatabasePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 2. Initialize native SQLitePCL provider bundle
            SQLitePCL.Batteries_V2.Init();

            // 3. Establish asynchronous connection
            const SQLiteOpenFlags flags = SQLiteOpenFlags.ReadWrite | SQLiteOpenFlags.Create | SQLiteOpenFlags.FullMutex;
            _connection = new SQLiteAsyncConnection(DatabasePath, flags, storeDateTimeAsTicks: true);

            // 4. Configure performance and integrity PRAGMA settings
            await _connection.ExecuteScalarAsync<string>("PRAGMA journal_mode = WAL;").ConfigureAwait(false);
            await _connection.ExecuteAsync("PRAGMA foreign_keys = ON;").ConfigureAwait(false);

            // 5. Query active user_version
            var userVersion = await _connection.ExecuteScalarAsync<int>("PRAGMA user_version;").ConfigureAwait(false);
            _logger.LogDebug("Detected current SQLite user_version: {Version}", userVersion);

            // 6. Apply pending migrations sequentially
            foreach (var migration in _migrations)
            {
                if (userVersion < migration.TargetVersion)
                {
                    _logger.LogInformation("Applying SQLite schema migration to version {TargetVersion}: {Description}",
                        migration.TargetVersion, migration.Description);

                    await migration.ApplyAsync(_connection).ConfigureAwait(false);
                    await _connection.ExecuteAsync($"PRAGMA user_version = {migration.TargetVersion};").ConfigureAwait(false);
                    userVersion = migration.TargetVersion;
                }
            }

            _currentSchemaVersion = userVersion;
            _isInitialized = true;

            _logger.LogInformation("SQLite database successfully initialized at {DatabasePath}. Schema version: {Version}",
                DatabasePath, _currentSchemaVersion);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SQLite database at {DatabasePath}", DatabasePath);
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public SQLiteAsyncConnection GetConnection() => Connection;

    public Task<int> InsertAsync<T>(T entity) where T : new() => Connection.InsertAsync(entity);

    public Task<int> UpdateAsync<T>(T entity) where T : new() => Connection.UpdateAsync(entity);

    public Task<int> DeleteAsync<T>(T entity) where T : new() => Connection.DeleteAsync(entity);

    public async Task<T?> GetByIdAsync<T>(int id) where T : new()
    {
        return await Connection.FindAsync<T>(id).ConfigureAwait(false);
    }

    public Task<List<T>> GetAllAsync<T>() where T : new() => Connection.Table<T>().ToListAsync();

    public AsyncTableQuery<T> Table<T>() where T : new() => Connection.Table<T>();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection != null)
        {
            try
            {
                await _connection.CloseAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error occurred while closing SQLite connection for {DatabasePath}", DatabasePath);
            }
            finally
            {
                _connection = null;
            }
        }

        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_connection != null)
        {
            try
            {
                _connection.CloseAsync().GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error occurred while closing SQLite connection synchronously for {DatabasePath}", DatabasePath);
            }
            finally
            {
                _connection = null;
            }
        }

        _initLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
