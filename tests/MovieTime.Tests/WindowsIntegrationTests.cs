using System;
using System.IO;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using MovieTime.App.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class WindowsIntegrationTests
{
    [Fact]
    public void WindowsOpenWithRegistrar_RegisterOpenWith_CreatesHKCURegistryEntries()
    {
        if (!OperatingSystem.IsWindows()) return;

        WindowsOpenWithRegistrar.RegisterOpenWith(NullLogger.Instance);

        // Verify Application registration
        using var appKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\Applications\MovieTime.App.exe");
        Assert.NotNull(appKey);
        Assert.Equal("Movie Time", appKey.GetValue("FriendlyAppName"));

        // Verify SupportedTypes
        using var typesKey = appKey.OpenSubKey("SupportedTypes");
        Assert.NotNull(typesKey);
        var subValues = typesKey.GetValueNames();
        Assert.Contains(".mp4", subValues);
        Assert.Contains(".mkv", subValues);

        // Verify ProgId registration
        using var progKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\MovieTime.Video");
        Assert.NotNull(progKey);

        // Verify .mp4 OpenWithProgids
        using var mp4Key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\.mp4\OpenWithProgids");
        Assert.NotNull(mp4Key);
        Assert.Contains("MovieTime.Video", mp4Key.GetValueNames());
    }
}
