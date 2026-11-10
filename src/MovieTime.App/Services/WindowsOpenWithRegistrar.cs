using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace MovieTime.App.Services;

/// <summary>
/// Safely registers Movie Time in Windows Explorer "Open With" menu for supported video extensions.
/// Uses HKCU (current user) registry hive to require zero elevation / admin rights,
/// and never overrides system defaults without user selection.
/// </summary>
public static class WindowsOpenWithRegistrar
{
    private static readonly string[] SupportedExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".flv", ".3gp", ".ogv"
    };

    public static void RegisterOpenWith(ILogger? logger = null)
    {
        if (!OperatingSystem.IsWindows()) return;

        try
        {
            var exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "MovieTime.App.exe");
            }

            var appDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;
            var iconPath = Path.Combine(appDir, "Assets", "app.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = exePath;
            }

            const string progId = "MovieTime.Video";
            const string appName = "River";

            // 1. Register Application in HKCU\Software\Classes\Applications\MovieTime.App.exe
            using (var appKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\Applications\MovieTime.App.exe"))
            {
                appKey.SetValue("FriendlyAppName", appName);
                using (var iconKey = appKey.CreateSubKey("DefaultIcon"))
                {
                    iconKey.SetValue(string.Empty, $"\"{iconPath}\",0");
                }
                using (var cmdKey = appKey.CreateSubKey(@"shell\open\command"))
                {
                    cmdKey.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
                }
                using (var supportedTypesKey = appKey.CreateSubKey("SupportedTypes"))
                {
                    foreach (var ext in SupportedExtensions)
                    {
                        supportedTypesKey.SetValue(ext, string.Empty);
                    }
                }
            }

            // 2. Register ProgId in HKCU\Software\Classes\MovieTime.Video
            using (var progKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + progId))
            {
                progKey.SetValue(string.Empty, "River Video File");
                progKey.SetValue("FriendlyTypeName", "Video file (River)");
                using (var iconKey = progKey.CreateSubKey("DefaultIcon"))
                {
                    iconKey.SetValue(string.Empty, $"\"{iconPath}\",0");
                }
                using (var cmdKey = progKey.CreateSubKey(@"shell\open\command"))
                {
                    cmdKey.SetValue(string.Empty, $"\"{exePath}\" \"%1\"");
                }
            }

            // 3. Register in OpenWithProgids & OpenWithList for each extension
            foreach (var ext in SupportedExtensions)
            {
                using var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ext);
                using var openWithProgids = extKey.CreateSubKey("OpenWithProgids");
                openWithProgids.SetValue(progId, string.Empty);

                using var openWithList = extKey.CreateSubKey(@"OpenWithList\MovieTime.App.exe");
            }

            logger?.LogInformation("Successfully registered River for Windows Open With across {Count} video formats.", SupportedExtensions.Length);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Could not complete Windows Open With registration.");
        }
    }
}
