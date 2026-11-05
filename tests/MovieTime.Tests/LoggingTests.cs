using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MovieTime.Core.Extensions;
using MovieTime.Core.Logging;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public class LoggingTests : IDisposable
{
    private readonly string _tempTestDir;
    private readonly StoragePathService _storagePathService;

    public LoggingTests()
    {
        _tempTestDir = Path.Combine(Path.GetTempPath(), "MovieTime_LogTests_" + Guid.NewGuid().ToString("N"));
        _storagePathService = new StoragePathService(_tempTestDir);
    }

    [Fact]
    public void AddMovieTimeLogging_ShouldResolveLoggersFromDI()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMovieTimeCore();
        services.AddMovieTimeLogging(_storagePathService, isDevelopment: true);
        var provider = services.BuildServiceProvider();

        // Act
        var logger = provider.GetService<ILogger<LoggingTests>>();
        var loggerFactory = provider.GetService<ILoggerFactory>();
        var serilogLogger = provider.GetService<Serilog.ILogger>();

        // Assert
        Assert.NotNull(logger);
        Assert.NotNull(loggerFactory);
        Assert.NotNull(serilogLogger);
    }

    [Fact]
    public void LocalLogger_ShouldWriteLogMessageToDiskInDesignatedLogsFolder()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMovieTimeCore();
        services.AddMovieTimeLogging(_storagePathService, isDevelopment: true);
        var provider = services.BuildServiceProvider();

        var logger = provider.GetRequiredService<ILogger<LoggingTests>>();
        var uniqueMarker = "TEST_LOG_EVENT_" + Guid.NewGuid().ToString("N");

        // Act
        logger.LogInformation("Integration test verification: {Marker}", uniqueMarker);
        Serilog.Log.CloseAndFlush();

        // Assert
        Assert.True(Directory.Exists(_storagePathService.LogsDirectory));
        var logFiles = Directory.GetFiles(_storagePathService.LogsDirectory, "movietime-*.log");
        Assert.NotEmpty(logFiles);

        var logContent = File.ReadAllText(logFiles.First());
        Assert.Contains(uniqueMarker, logContent);
        Assert.Contains("[INF]", logContent);
        Assert.Contains("MovieTime.Tests.LoggingTests", logContent);
    }

    [Fact]
    public void ProductionLogging_ShouldExcludeDebugEvents()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMovieTimeCore();
        services.AddMovieTimeLogging(_storagePathService, isDevelopment: false);
        var provider = services.BuildServiceProvider();

        var logger = provider.GetRequiredService<ILogger<LoggingTests>>();
        var debugMarker = "DEBUG_ONLY_MARKER_" + Guid.NewGuid().ToString("N");
        var infoMarker = "INFO_MARKER_" + Guid.NewGuid().ToString("N");

        // Act
        logger.LogDebug("This should NOT be recorded in production: {Marker}", debugMarker);
        logger.LogInformation("This SHOULD be recorded in production: {Marker}", infoMarker);
        Serilog.Log.CloseAndFlush();

        // Assert
        var logFiles = Directory.GetFiles(_storagePathService.LogsDirectory, "movietime-*.log");
        Assert.NotEmpty(logFiles);

        var logContent = File.ReadAllText(logFiles.First());
        Assert.DoesNotContain(debugMarker, logContent);
        Assert.Contains(infoMarker, logContent);
    }

    public void Dispose()
    {
        Serilog.Log.CloseAndFlush();

        if (Directory.Exists(_tempTestDir))
        {
            try
            {
                Directory.Delete(_tempTestDir, recursive: true);
            }
            catch
            {
                // Ignore transient cleanup errors in temp path
            }
        }
    }
}
