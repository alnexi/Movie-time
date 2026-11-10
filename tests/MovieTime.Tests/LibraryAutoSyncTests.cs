using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Entities;
using MovieTime.Core.Library;
using MovieTime.Core.Library.Models;
using Xunit;

namespace MovieTime.Tests;

public class LibraryAutoSyncTests
{
    private sealed class MockScanRootService : IScanRootService
    {
        public List<ScanRoot> Roots { get; } = new();

        public Task<IReadOnlyList<ScanRoot>> GetRootsAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<ScanRoot>>(Roots);
        }

        public Task<ScanRoot> AddRootAsync(string path, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> RemoveRootAsync(int id, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<bool> SetRootEnabledAsync(int id, bool isEnabled, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task UpdateLastScannedAsync(int id, DateTime scannedAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public bool ValidatePath(string path, out string normalizedPath, out string? errorMessage)
        {
            normalizedPath = Path.GetFullPath(path);
            errorMessage = null;
            return true;
        }
    }

    private sealed class MockImportService : ILibraryImportService
    {
        public bool IsImporting { get; set; }
        public int ImportCallCount { get; private set; }
        public event EventHandler<LibraryImportResult>? ImportCompleted;

        public Task<LibraryImportResult> ImportAsync(
            LibraryImportOptions? options = null,
            IProgress<LibraryImportProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            ImportCallCount++;
            var res = new LibraryImportResult
            {
                FilesDiscovered = 10,
                FilesImported = 2,
                FilesUpdated = 1,
                FilesUnchanged = 7
            };
            ImportCompleted?.Invoke(this, res);
            return Task.FromResult(res);
        }
    }

    [Fact]
    public async Task StartAsync_InitializesServiceAndSetsIsRunning()
    {
        var scanRootService = new MockScanRootService();
        var importService = new MockImportService();
        var autoSync = new LibraryAutoSyncService(scanRootService, importService, NullLogger<LibraryAutoSyncService>.Instance);

        Assert.False(autoSync.IsRunning);

        await autoSync.StartAsync();
        Assert.True(autoSync.IsRunning);

        await autoSync.StopAsync();
        Assert.False(autoSync.IsRunning);
    }

    [Fact]
    public void ReconciliationInterval_ClampsToMinimumFiveSeconds()
    {
        var scanRootService = new MockScanRootService();
        var importService = new MockImportService();
        var autoSync = new LibraryAutoSyncService(scanRootService, importService);

        Assert.Equal(60, autoSync.ReconciliationIntervalSeconds);

        autoSync.ReconciliationIntervalSeconds = 120;
        Assert.Equal(120, autoSync.ReconciliationIntervalSeconds);

        // Clamping check: values below 5 are clamped to 5
        autoSync.ReconciliationIntervalSeconds = 2;
        Assert.Equal(5, autoSync.ReconciliationIntervalSeconds);
    }

    [Fact]
    public async Task TriggerSyncAsync_RelaysImportCompletedViaSyncCompleted()
    {
        var scanRootService = new MockScanRootService();
        var importService = new MockImportService();
        var autoSync = new LibraryAutoSyncService(scanRootService, importService);

        await autoSync.StartAsync();

        LibraryImportResult? receivedResult = null;
        autoSync.SyncCompleted += (s, e) =>
        {
            receivedResult = e;
        };

        // Simulate an import event from the underlying import service
        await importService.ImportAsync();

        Assert.NotNull(receivedResult);
        Assert.Equal(10, receivedResult.FilesDiscovered);
        Assert.Equal(2, receivedResult.FilesImported);

        await autoSync.StopAsync();
    }

    [Fact]
    public async Task RefreshWatchersAsync_SafeguardsOfflineDrivesWithoutCrashing()
    {
        var scanRootService = new MockScanRootService();
        // Add non-existent directory on imaginary Z: drive
        scanRootService.Roots.Add(new ScanRoot
        {
            Id = 99,
            Path = "Z:\\NonExistent\\MLibrary",
            NormalizedPath = "Z:\\NonExistent\\MLibrary",
            IsEnabled = true
        });

        var importService = new MockImportService();
        var autoSync = new LibraryAutoSyncService(scanRootService, importService);

        await autoSync.StartAsync();

        // Refreshing watchers must handle the offline/missing drive safely
        await autoSync.RefreshWatchersAsync();

        Assert.True(autoSync.IsRunning);
        await autoSync.StopAsync();
    }
}
