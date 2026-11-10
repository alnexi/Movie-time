using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Library.Models;

namespace MovieTime.Core.Library;

/// <summary>
/// Hybrid filesystem auto-synchronization service.
/// Combines debounced real-time System.IO.FileSystemWatcher monitoring with periodic
/// background reconciliation to guarantee that Movie Time / River's library accurately
/// mirrors the actual filesystem state across all active MLibrary drives.
/// </summary>
public sealed class LibraryAutoSyncService : ILibraryAutoSyncService
{
    private const int DefaultDebounceMilliseconds = 2000;
    private const int DefaultReconciliationSeconds = 60;
    private const int WatcherBufferSize = 65536; // 64 KB buffer for high event loads

    private readonly IScanRootService _scanRootService;
    private readonly ILibraryImportService _importService;
    private readonly ILogger<LibraryAutoSyncService> _logger;

    private readonly Dictionary<int, FileSystemWatcher> _watchers = new();
    private readonly object _watcherLock = new();
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    private Timer? _debounceTimer;
    private Timer? _reconciliationTimer;
    private CancellationTokenSource? _cts;

    private int _reconciliationIntervalSeconds = DefaultReconciliationSeconds;
    private bool _isRunning;
    private bool _isDisposed;

    public bool IsRunning => _isRunning && !_isDisposed;

    public int ReconciliationIntervalSeconds
    {
        get => _reconciliationIntervalSeconds;
        set
        {
            _reconciliationIntervalSeconds = Math.Max(5, value);
            if (_isRunning && _reconciliationTimer != null)
            {
                var interval = TimeSpan.FromSeconds(_reconciliationIntervalSeconds);
                _reconciliationTimer.Change(interval, interval);
                _logger.LogInformation("Reconciliation interval updated to {IntervalSeconds} seconds.", _reconciliationIntervalSeconds);
            }
        }
    }

    public event EventHandler<LibraryImportResult>? SyncCompleted;

    public LibraryAutoSyncService(
        IScanRootService scanRootService,
        ILibraryImportService importService,
        ILogger<LibraryAutoSyncService>? logger = null)
    {
        _scanRootService = scanRootService ?? throw new ArgumentNullException(nameof(scanRootService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _logger = logger ?? NullLogger<LibraryAutoSyncService>.Instance;

        // Relay ImportCompleted events from the underlying import service
        _importService.ImportCompleted += (s, e) =>
        {
            SyncCompleted?.Invoke(this, e);
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        lock (_watcherLock)
        {
            if (_isRunning || _isDisposed)
            {
                return;
            }

            _isRunning = true;
            _cts = new CancellationTokenSource();
        }

        _logger.LogInformation("Starting LibraryAutoSyncService (Reconciliation: {ReconSeconds}s, Debounce: {DebounceMs}ms)...",
            _reconciliationIntervalSeconds, DefaultDebounceMilliseconds);

        // 1. Initialize FileSystemWatchers for all currently enabled scan roots
        await RefreshWatchersAsync().ConfigureAwait(false);

        // 2. Set up the debounced synchronization timer (one-shot, armed on events)
        _debounceTimer = new Timer(OnDebounceTimerElapsed, null, Timeout.Infinite, Timeout.Infinite);

        // 3. Set up periodic reconciliation timer
        var interval = TimeSpan.FromSeconds(_reconciliationIntervalSeconds);
        _reconciliationTimer = new Timer(OnPeriodicTimerElapsed, null, interval, interval);

        _logger.LogInformation("LibraryAutoSyncService started successfully with {WatcherCount} active watcher(s).", _watchers.Count);
    }

    public Task StopAsync()
    {
        lock (_watcherLock)
        {
            if (!_isRunning)
            {
                return Task.CompletedTask;
            }

            _isRunning = false;
            _cts?.Cancel();

            _debounceTimer?.Change(Timeout.Infinite, Timeout.Infinite);
            _reconciliationTimer?.Change(Timeout.Infinite, Timeout.Infinite);

            DisposeAllWatchers();
        }

        _logger.LogInformation("LibraryAutoSyncService stopped.");
        return Task.CompletedTask;
    }

    public async Task RefreshWatchersAsync()
    {
        if (_isDisposed) return;

        try
        {
            var roots = await _scanRootService.GetRootsAsync(_cts?.Token ?? CancellationToken.None).ConfigureAwait(false);
            var enabledRoots = roots.Where(r => r.IsEnabled).ToList();

            lock (_watcherLock)
            {
                var currentIds = new HashSet<int>(enabledRoots.Select(r => r.Id));

                // 1. Remove watchers for roots that were disabled or deleted
                var toRemove = _watchers.Keys.Where(id => !currentIds.Contains(id)).ToList();
                foreach (var id in toRemove)
                {
                    if (_watchers.TryGetValue(id, out var watcher))
                    {
                        TeardownWatcher(watcher);
                        _watchers.Remove(id);
                        _logger.LogInformation("Removed FileSystemWatcher for scan root {Id}.", id);
                    }
                }

                // 2. Add or verify watchers for currently enabled roots
                foreach (var root in enabledRoots)
                {
                    // Verify physical drive accessibility (Drive Safety check)
                    var driveRoot = Path.GetPathRoot(root.Path);
                    bool isDriveReady = true;
                    if (!string.IsNullOrEmpty(driveRoot))
                    {
                        try
                        {
                            var dInfo = new DriveInfo(driveRoot);
                            isDriveReady = dInfo.IsReady;
                        }
                        catch
                        {
                            // In case drive property query fails, fall back to Directory.Exists
                        }
                    }

                    if (!isDriveReady || !Directory.Exists(root.Path))
                    {
                        // If drive disconnected or folder missing, remove existing watcher if present
                        if (_watchers.TryGetValue(root.Id, out var staleWatcher))
                        {
                            TeardownWatcher(staleWatcher);
                            _watchers.Remove(root.Id);
                            _logger.LogWarning("Scan root '{Path}' is offline. Suspended watcher without modifying records.", root.Path);
                        }
                        continue;
                    }

                    // Create watcher if not already registered
                    if (!_watchers.ContainsKey(root.Id))
                    {
                        try
                        {
                            var watcher = new FileSystemWatcher(root.Path)
                            {
                                IncludeSubdirectories = true,
                                InternalBufferSize = WatcherBufferSize,
                                NotifyFilter = NotifyFilters.FileName |
                                               NotifyFilters.DirectoryName |
                                               NotifyFilters.LastWrite |
                                               NotifyFilters.Size
                            };

                            watcher.Created += OnFileSystemEvent;
                            watcher.Deleted += OnFileSystemEvent;
                            watcher.Changed += OnFileSystemEvent;
                            watcher.Renamed += OnFileSystemRenamed;
                            watcher.Error += OnWatcherError;

                            watcher.EnableRaisingEvents = true;
                            _watchers[root.Id] = watcher;
                            _logger.LogInformation("Active FileSystemWatcher attached to '{Path}' (RootId: {Id}).", root.Path, root.Id);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to initialize FileSystemWatcher for root '{Path}'.", root.Path);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error refreshing scan root watchers.");
        }
    }

    public Task TriggerSyncAsync()
    {
        ArmDebounceTimer();
        return Task.CompletedTask;
    }

    private void OnFileSystemEvent(object sender, FileSystemEventArgs e)
    {
        if (ShouldProcessEvent(e.FullPath))
        {
            _logger.LogDebug("Filesystem event [{ChangeType}] detected: '{FullPath}'. Arming debounce timer.", e.ChangeType, e.FullPath);
            ArmDebounceTimer();
        }
    }

    private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
    {
        if (ShouldProcessEvent(e.FullPath) || ShouldProcessEvent(e.OldFullPath))
        {
            _logger.LogDebug("Filesystem rename detected: '{OldPath}' -> '{NewPath}'. Arming debounce timer.", e.OldFullPath, e.FullPath);
            ArmDebounceTimer();
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        _logger.LogWarning(ex, "FileSystemWatcher encountered a buffer overflow or operating system error. Scheduling recovery reconciliation.");
        ArmDebounceTimer();
    }

    private static bool ShouldProcessEvent(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        // If it's a directory (or directory event where no extension exists)
        if (Directory.Exists(path)) return true;

        var ext = Path.GetExtension(path);
        if (string.IsNullOrEmpty(ext))
        {
            // Might be a directory being renamed/created/deleted
            return true;
        }

        // Process if the extension matches one of River's supported video types
        return AppConstants.SupportedVideoExtensions.Contains(ext);
    }

    private void ArmDebounceTimer()
    {
        if (_isDisposed || !_isRunning) return;

        try
        {
            // Reset debounce countdown: wait for file writes/copies to settle
            _debounceTimer?.Change(DefaultDebounceMilliseconds, Timeout.Infinite);
        }
        catch (ObjectDisposedException)
        {
            // Shutdown race
        }
    }

    private void OnDebounceTimerElapsed(object? state)
    {
        _ = ExecuteSyncAsync("Debounced Filesystem Watcher");
    }

    private void OnPeriodicTimerElapsed(object? state)
    {
        _ = RunPeriodicCycleAsync();
    }

    private async Task RunPeriodicCycleAsync()
    {
        if (_isDisposed || !_isRunning) return;

        // 1. Periodically re-check drive availability and dynamically register/restore watchers
        await RefreshWatchersAsync().ConfigureAwait(false);

        // 2. Perform periodic reconciliation scan across enabled roots
        await ExecuteSyncAsync("Periodic 60s Reconciliation").ConfigureAwait(false);
    }

    private async Task ExecuteSyncAsync(string triggerSource)
    {
        if (_isDisposed || !_isRunning) return;

        // Non-blocking concurrency gate: if a scan is already running, avoid stacking duplicate scans
        if (!await _syncGate.WaitAsync(0).ConfigureAwait(false))
        {
            _logger.LogDebug("Auto-sync requested via [{Trigger}], but a synchronization operation is already in flight. Skipping duplicate trigger.", triggerSource);
            return;
        }

        try
        {
            if (_importService.IsImporting)
            {
                _logger.LogDebug("Underlying ILibraryImportService is already executing. Bypassing auto-sync.");
                return;
            }

            _logger.LogInformation("Executing automated library synchronization. Trigger: [{Trigger}]...", triggerSource);

            var options = new LibraryImportOptions
            {
                ForceReparse = false
            };

            var result = await _importService.ImportAsync(options, null, _cts?.Token ?? CancellationToken.None).ConfigureAwait(false);

            _logger.LogInformation("Automated library synchronization completed cleanly via [{Trigger}]. Imported: {Imported}, Updated: {Updated}, Missing: {Missing}, Unchanged: {Unchanged}.",
                triggerSource, result.FilesImported, result.FilesUpdated, result.FilesMissing, result.FilesUnchanged);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Automated library synchronization was cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Automated library synchronization encountered an error.");
        }
        finally
        {
            _syncGate.Release();
        }
    }

    private void TeardownWatcher(FileSystemWatcher watcher)
    {
        try
        {
            watcher.EnableRaisingEvents = false;
            watcher.Created -= OnFileSystemEvent;
            watcher.Deleted -= OnFileSystemEvent;
            watcher.Changed -= OnFileSystemEvent;
            watcher.Renamed -= OnFileSystemRenamed;
            watcher.Error -= OnWatcherError;
            watcher.Dispose();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error tearing down FileSystemWatcher.");
        }
    }

    private void DisposeAllWatchers()
    {
        foreach (var watcher in _watchers.Values)
        {
            TeardownWatcher(watcher);
        }
        _watchers.Clear();
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _isRunning = false;

        _cts?.Cancel();
        _cts?.Dispose();

        _debounceTimer?.Dispose();
        _reconciliationTimer?.Dispose();

        DisposeAllWatchers();
        _syncGate.Dispose();
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
