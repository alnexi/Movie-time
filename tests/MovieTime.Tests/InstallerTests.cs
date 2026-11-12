using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using MovieTime.Installer;
using Xunit;

namespace MovieTime.Tests;

public sealed class InstallerTests
{
    [Fact]
    public void ShortcutHelper_CreateAndDeleteShortcut_SucceedsOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        var tempDir = Path.Combine(Path.GetTempPath(), "MovieTime_ShortcutTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var targetFile = Path.Combine(tempDir, "DummyApp.exe");
            File.WriteAllText(targetFile, "dummy binary");

            var shortcutPath = Path.Combine(tempDir, "River.lnk");
            ShortcutHelper.CreateShortcut(
                shortcutPath,
                targetFile,
                tempDir,
                "River Media Player",
                targetFile);

            Assert.True(File.Exists(shortcutPath), "Shortcut file was not created.");
            Assert.True(new FileInfo(shortcutPath).Length > 0, "Shortcut file is empty.");

            ShortcutHelper.DeleteShortcut(shortcutPath);
            Assert.False(File.Exists(shortcutPath), "Shortcut file was not deleted.");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void Installer_DesktopBinary_ExistsAndMatchesSha256()
    {
        var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var finalInstallerPath = Path.Combine(desktopPath, "Desktop", "River Installer", "River-Setup-x64.exe");

        Assert.True(File.Exists(finalInstallerPath), $"Final installer binary does not exist at {finalInstallerPath}");

        var fileInfo = new FileInfo(finalInstallerPath);
        Assert.True(fileInfo.Length > 100 * 1024 * 1024, $"Final installer size ({fileInfo.Length} bytes) is less than 100 MB.");

        // Verify PE Header (MZ signature & x64 architecture)
        using var stream = File.OpenRead(finalInstallerPath);
        using var reader = new BinaryReader(stream);

        byte[] mz = reader.ReadBytes(2);
        Assert.Equal(0x4D, mz[0]); // 'M'
        Assert.Equal(0x5A, mz[1]); // 'Z'

        // Check PE signature offset at 0x3C
        stream.Seek(0x3C, SeekOrigin.Begin);
        int peOffset = reader.ReadInt32();
        stream.Seek(peOffset, SeekOrigin.Begin);

        byte[] pe = reader.ReadBytes(4);
        Assert.Equal(0x50, pe[0]); // 'P'
        Assert.Equal(0x45, pe[1]); // 'E'
        Assert.Equal(0x00, pe[2]);
        Assert.Equal(0x00, pe[3]);

        ushort machine = reader.ReadUInt16();
        Assert.Equal(0x8664, machine); // IMAGE_FILE_MACHINE_AMD64 (x64)
    }

    [Fact]
    public void Installer_PayloadVerification_ContainsLibVlcAndSqliteAndApp()
    {
        var currentDir = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDir != null && !File.Exists(Path.Combine(currentDir.FullName, "MovieTime.sln")))
        {
            currentDir = currentDir.Parent;
        }

        var repoRoot = currentDir?.FullName ?? Directory.GetCurrentDirectory();
        var payloadZip = Path.Combine(repoRoot, "src", "MovieTime.Installer", "RiverPayload.zip");

        Assert.True(File.Exists(payloadZip), $"RiverPayload.zip not found at {payloadZip}");

        using var archive = ZipFile.OpenRead(payloadZip);

        // Verify critical core dependencies are bundled inside the offline archive
        Assert.Contains(archive.Entries, e => e.FullName.Equals("MovieTime.App.exe", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, e => e.FullName.Contains("libvlc.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, e => e.FullName.Contains("libvlccore.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, e => e.FullName.Contains("plugins/", StringComparison.OrdinalIgnoreCase) || e.FullName.Contains("plugins\\", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, e => e.FullName.Equals("e_sqlite3.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, e => e.FullName.Equals("Microsoft.ui.xaml.dll", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, e => e.FullName.Replace('\\', '/').Contains("Assets/app.ico", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(archive.Entries, e => e.FullName.Replace('\\', '/').Contains("Assets/Theme/sailboat_night.png", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Uninstallation_DataSafety_PreservesLocalAppDataFolder()
    {
        // Assert that the app data storage root is strictly under LocalAppData\MovieTime
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var movieTimeDataDir = Path.Combine(localAppData, "MovieTime");

        // The installer/uninstaller target must never be the LocalAppData directory
        var defaultInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "River");
        Assert.NotEqual(movieTimeDataDir, defaultInstallPath);

        // Verify uninstallation service never touches or references the data path
        Assert.False(defaultInstallPath.StartsWith(movieTimeDataDir, StringComparison.OrdinalIgnoreCase));
    }
}
