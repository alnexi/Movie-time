using System;
using System.IO;
using Serilog;
using Serilog.Events;

namespace MovieTime.Core.Logging;

/// <summary>
/// Configures and bootstraps the local Serilog logging engine for Movie Time.
/// Ensures logs remain strictly offline and localized to the designated application directory.
/// </summary>
public static class LoggingBootstrapper
{
    public const string DefaultOutputTemplate =
        "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz}] [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}";

    public const long DefaultMaxFileSize = 10 * 1024 * 1024; // 10 MB per file
    public const int DefaultRetainedFileCount = 14;           // Retain 14 days of logs

    /// <summary>
    /// Creates a configured Serilog Logger instance targeted to the local storage path.
    /// </summary>
    /// <param name="logsDirectory">The directory where logs will be stored.</param>
    /// <param name="isDevelopment">Whether to log at Debug level instead of Information.</param>
    /// <returns>A configured <see cref="Serilog.ILogger"/> instance.</returns>
    public static Serilog.ILogger CreateLogger(string logsDirectory, bool isDevelopment = false)
    {
        // Ensure directory exists
        Directory.CreateDirectory(logsDirectory);

        var logFilePath = Path.Combine(logsDirectory, "movietime-.log");
        var minimumLevel = isDevelopment ? LogEventLevel.Debug : LogEventLevel.Information;

        return new LoggerConfiguration()
            .MinimumLevel.Is(minimumLevel)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                path: logFilePath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: DefaultRetainedFileCount,
                fileSizeLimitBytes: DefaultMaxFileSize,
                rollOnFileSizeLimit: true,
                outputTemplate: DefaultOutputTemplate,
                flushToDiskInterval: TimeSpan.FromSeconds(1)
            )
            .CreateLogger();
    }
}
