using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MovieTime.Installer;

public sealed class UninstallationService
{
    public async Task UninstallAsync(
        string installDir,
        IProgress<(double Progress, string Status, string Detail)> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(installDir);
        ArgumentNullException.ThrowIfNull(progress);

        // 1. Close running processes
        progress.Report((0.10, "Closing running River instances...", "Closing application"));
        await CloseRunningProcessesAsync(cancellationToken);

        // 2. Remove shortcuts
        progress.Report((0.30, "Removing shortcuts...", "Desktop and Start Menu"));
        RemoveShortcuts();

        // 3. Remove registry entries
        progress.Report((0.50, "Removing Windows registration...", "Uninstall keys"));
        RemoveRegistryEntries();

        // 4. Remove installed program files (NEVER touch %LocalAppData%\MovieTime or user media!)
        progress.Report((0.70, "Removing installed files...", installDir));
        RemoveInstalledFiles(installDir);

        // 5. Schedule self-cleanup of uninstaller
        progress.Report((0.95, "Finalizing uninstallation...", "Cleaning up"));
        ScheduleSelfCleanup(installDir);

        progress.Report((1.0, "River has been successfully uninstalled.", "Your personal library data has been preserved."));
    }

    private static async Task CloseRunningProcessesAsync(CancellationToken cancellationToken)
    {
        string[] targetProcessNames = { "MovieTime.App", "River" };
        foreach (var procName in targetProcessNames)
        {
            var processes = Process.GetProcessesByName(procName);
            foreach (var p in processes)
            {
                try
                {
                    p.CloseMainWindow();
                    if (!p.WaitForExit(2000))
                    {
                        p.Kill();
                        await p.WaitForExitAsync(cancellationToken);
                    }
                }
                catch
                {
                    // Ignore
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }

    private static void RemoveShortcuts()
    {
        try
        {
            // Desktop
            var commonDesktop = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            if (!string.IsNullOrEmpty(commonDesktop))
            {
                ShortcutHelper.DeleteShortcut(Path.Combine(commonDesktop, "River.lnk"));
            }

            var userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (!string.IsNullOrEmpty(userDesktop))
            {
                ShortcutHelper.DeleteShortcut(Path.Combine(userDesktop, "River.lnk"));
            }

            // Start Menu
            var commonPrograms = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
            if (!string.IsNullOrEmpty(commonPrograms))
            {
                ShortcutHelper.DeleteShortcut(Path.Combine(commonPrograms, "River.lnk"));
            }

            var userPrograms = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
            if (!string.IsNullOrEmpty(userPrograms))
            {
                ShortcutHelper.DeleteShortcut(Path.Combine(userPrograms, "River.lnk"));
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to remove shortcuts: {ex.Message}");
        }
    }

    private static void RemoveRegistryEntries()
    {
        try
        {
            // HKLM Uninstall key
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            baseKey.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\River", throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignore
        }

        try
        {
            // HKCU Uninstall key
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\River", throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignore
        }

        try
        {
            // File associations
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\Applications\MovieTime.App.exe", throwOnMissingSubKey: false);
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\River.Video", throwOnMissingSubKey: false);
        }
        catch
        {
            // Ignore
        }
    }

    private static void RemoveInstalledFiles(string installDir)
    {
        if (!Directory.Exists(installDir)) return;

        var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;

        try
        {
            var dir = new DirectoryInfo(installDir);

            // Delete all subdirectories
            foreach (var subDir in dir.EnumerateDirectories())
            {
                try
                {
                    subDir.Delete(true);
                }
                catch
                {
                    // Ignore in-use files
                }
            }

            // Delete all files except currently running uninstaller
            foreach (var file in dir.EnumerateFiles())
            {
                if (currentExe != null && file.FullName.Equals(currentExe, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    file.Attributes = FileAttributes.Normal;
                    file.Delete();
                }
                catch
                {
                    // Ignore locked files
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error removing installed files: {ex.Message}");
        }
    }

    private static void ScheduleSelfCleanup(string installDir)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c timeout /t 2 /nobreak > NUL & rmdir /s /q \"{installDir}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process.Start(psi);
        }
        catch
        {
            // Best effort
        }
    }
}
