using System;
using System.Threading;
using System.Threading.Tasks;
using MovieTime.Core.Library.Models;

namespace MovieTime.Core.Library;

/// <summary>
/// Background synchronization service orchestrating real-time filesystem monitoring
/// (via FileSystemWatcher) and periodic reconciliation across registered and enabled MLibrary directories.
/// </summary>
public interface ILibraryAutoSyncService : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Indicates whether the auto-sync service is currently active and monitoring directories.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Gets or sets the periodic reconciliation interval in seconds.
    /// Default is 60 seconds.
    /// </summary>
    int ReconciliationIntervalSeconds { get; set; }

    /// <summary>
    /// Starts real-time filesystem monitoring and periodic reconciliation.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops filesystem monitoring and periodic reconciliation.
    /// </summary>
    Task StopAsync();

    /// <summary>
    /// Synchronizes active FileSystemWatchers against current registered and enabled scan roots
    /// (e.g. after adding, removing, or toggling an MLibrary).
    /// </summary>
    Task RefreshWatchersAsync();

    /// <summary>
    /// Manually triggers a debounced synchronization scan.
    /// </summary>
    Task TriggerSyncAsync();

    /// <summary>
    /// Fired whenever an automatic watcher or periodic synchronization completes.
    /// </summary>
    event EventHandler<LibraryImportResult>? SyncCompleted;
}
