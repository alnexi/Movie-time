using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace MovieTime.Installer;

public sealed class InstallationOptions
{
    public string InstallPath { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        "River");

    public bool CreateDesktopShortcut { get; set; } = true;
    public bool CreateStartMenuShortcut { get; set; } = true;
    public bool RegisterFileAssociations { get; set; } = true;
    public bool LaunchAfterInstall { get; set; } = true;
}

public sealed class InstallationService
{
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".webm", ".m4v", ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".flv", ".3gp", ".ogv"
    };

    public async Task InstallAsync(
        InstallationOptions options,
        IProgress<(double Progress, string Status, string Detail)> progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        // 1. Close any running instances of River / MovieTime.App
        progress.Report((0.02, "Preparing installation environment...", "Checking running processes"));
        await CloseRunningProcessesAsync(cancellationToken);

        // 2. Ensure target directory exists
        progress.Report((0.05, "Preparing target directory...", options.InstallPath));
        if (!Directory.Exists(options.InstallPath))
        {
            Directory.CreateDirectory(options.InstallPath);
        }

        // 3. Extract Embedded River Payload Archive
        progress.Report((0.08, "Extracting River engine...", "Reading embedded archive"));
        await ExtractPayloadAsync(options.InstallPath, progress, cancellationToken);

        // 4. Create Shortcuts
        var mainExe = Path.Combine(options.InstallPath, "MovieTime.App.exe");
        var appIcon = Path.Combine(options.InstallPath, "Assets", "app.ico");
        if (!File.Exists(appIcon))
        {
            appIcon = mainExe;
        }

        if (options.CreateDesktopShortcut)
        {
            progress.Report((0.88, "Creating shortcuts...", "Desktop shortcut"));
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                if (string.IsNullOrEmpty(desktopPath) || !Directory.Exists(desktopPath))
                {
                    desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                }

                var shortcutPath = Path.Combine(desktopPath, "River.lnk");
                ShortcutHelper.CreateShortcut(
                    shortcutPath,
                    mainExe,
                    options.InstallPath,
                    "River — Modern Local Media Player",
                    appIcon);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create desktop shortcut: {ex.Message}");
            }
        }

        if (options.CreateStartMenuShortcut)
        {
            progress.Report((0.91, "Creating shortcuts...", "Start Menu shortcut"));
            try
            {
                var programsPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);
                if (string.IsNullOrEmpty(programsPath) || !Directory.Exists(programsPath))
                {
                    programsPath = Environment.GetFolderPath(Environment.SpecialFolder.Programs);
                }

                var shortcutPath = Path.Combine(programsPath, "River.lnk");
                ShortcutHelper.CreateShortcut(
                    shortcutPath,
                    mainExe,
                    options.InstallPath,
                    "River — Modern Local Media Player",
                    appIcon);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to create start menu shortcut: {ex.Message}");
            }
        }

        // 5. Deploy Uninstaller
        progress.Report((0.94, "Configuring uninstaller...", "Writing uninstaller"));
        var uninstallerPath = Path.Combine(options.InstallPath, "Uninstall.exe");
        try
        {
            var currentExe = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrEmpty(currentExe) && File.Exists(currentExe))
            {
                File.Copy(currentExe, uninstallerPath, true);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to copy uninstaller executable: {ex.Message}");
        }

        // 6. Register in Windows Add/Remove Programs
        progress.Report((0.96, "Registering with Windows...", "Updating Add/Remove Programs"));
        RegisterUninstallInfo(options.InstallPath, uninstallerPath, appIcon);

        // 7. Register Open With File Associations
        if (options.RegisterFileAssociations)
        {
            progress.Report((0.98, "Registering media file types...", "Explorer Open With menu"));
            RegisterFileAssociations(mainExe, appIcon);
        }

        progress.Report((1.0, "Installation completed successfully!", "River is ready to launch"));
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
                    // Continue best effort
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }

    private static async Task ExtractPayloadAsync(
        string targetDir,
        IProgress<(double Progress, string Status, string Detail)> progress,
        CancellationToken cancellationToken)
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream("RiverPayload.zip")
            ?? assembly.GetManifestResourceStream("MovieTime.Installer.RiverPayload.zip");

        if (stream == null)
        {
            throw new FileNotFoundException("The embedded installation payload 'RiverPayload.zip' was not found in the installer binary.");
        }

        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        int totalEntries = archive.Entries.Count;
        if (totalEntries == 0) return;

        int processed = 0;
        const double startProgress = 0.10;
        const double endProgress = 0.85;
        double progressRange = endProgress - startProgress;

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var destinationPath = Path.GetFullPath(Path.Combine(targetDir, entry.FullName));
            if (!destinationPath.StartsWith(targetDir, StringComparison.OrdinalIgnoreCase))
            {
                // Prevent ZipSlip vulnerability
                continue;
            }

            if (string.IsNullOrEmpty(entry.Name))
            {
                // Directory entry
                Directory.CreateDirectory(destinationPath);
            }
            else
            {
                var parentDir = Path.GetDirectoryName(destinationPath);
                if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                {
                    Directory.CreateDirectory(parentDir);
                }

                // Extract with overwrite support
                entry.ExtractToFile(destinationPath, overwrite: true);
            }

            processed++;
            if (processed % 15 == 0 || processed == totalEntries)
            {
                double current = startProgress + (progressRange * ((double)processed / totalEntries));
                progress.Report((current, "Extracting application binaries and codecs...", entry.Name));
                await Task.Yield();
            }
        }
    }

    private static void RegisterUninstallInfo(string installDir, string uninstallerPath, string appIcon)
    {
        try
        {
            const string subKeyName = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\River";
            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = baseKey.CreateSubKey(subKeyName, writable: true);

            key.SetValue("DisplayName", "River");
            key.SetValue("DisplayVersion", "1.2.2");
            key.SetValue("Publisher", "Ali Nematollahi");
            key.SetValue("DisplayIcon", $"\"{appIcon}\",0");
            key.SetValue("InstallLocation", installDir);
            key.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
            key.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --silent");
            key.SetValue("InstallDate", DateTime.UtcNow.ToString("yyyyMMdd"));
            key.SetValue("NoModify", 1, RegistryValueKind.DWord);
            key.SetValue("NoRepair", 1, RegistryValueKind.DWord);

            // Calculate estimated size in KB
            try
            {
                var dirInfo = new DirectoryInfo(installDir);
                long bytes = 0;
                foreach (var file in dirInfo.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    bytes += file.Length;
                }
                key.SetValue("EstimatedSize", (int)(bytes / 1024), RegistryValueKind.DWord);
            }
            catch
            {
                key.SetValue("EstimatedSize", 310 * 1024, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to write HKLM uninstall registry entry: {ex.Message}");
            // Fallback to HKCU if non-admin or HKLM restricted
            try
            {
                using var cuKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\River", writable: true);
                cuKey.SetValue("DisplayName", "River");
                cuKey.SetValue("DisplayVersion", "1.2.2");
                cuKey.SetValue("Publisher", "Ali Nematollahi");
                cuKey.SetValue("DisplayIcon", $"\"{appIcon}\",0");
                cuKey.SetValue("InstallLocation", installDir);
                cuKey.SetValue("UninstallString", $"\"{uninstallerPath}\" --uninstall");
                cuKey.SetValue("QuietUninstallString", $"\"{uninstallerPath}\" --uninstall --silent");
                cuKey.SetValue("InstallDate", DateTime.UtcNow.ToString("yyyyMMdd"));
                cuKey.SetValue("NoModify", 1, RegistryValueKind.DWord);
                cuKey.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
            catch
            {
                // Ignore fallback error
            }
        }
    }

    private static void RegisterFileAssociations(string exePath, string iconPath)
    {
        try
        {
            const string progId = "River.Video";
            const string appName = "River";

            // 1. Applications entry
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
                    foreach (var ext in VideoExtensions)
                    {
                        supportedTypesKey.SetValue(ext, string.Empty);
                    }
                }
            }

            // 2. ProgID entry
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

            // 3. Extensions
            foreach (var ext in VideoExtensions)
            {
                using var extKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\" + ext);
                using var openWithProgids = extKey.CreateSubKey("OpenWithProgids");
                openWithProgids.SetValue(progId, string.Empty);

                using var openWithList = extKey.CreateSubKey(@"OpenWithList\MovieTime.App.exe");
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to register file associations: {ex.Message}");
        }
    }
}
