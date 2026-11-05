using System;
using MovieTime.Core;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public class AppEnvironmentTests
{
    private readonly AppEnvironment _environment = new();

    [Fact]
    public void ApplicationMetadata_ShouldMatchConstants()
    {
        Assert.Equal(AppConstants.ApplicationName, _environment.ApplicationName);
        Assert.Equal(AppConstants.ApplicationVersion, _environment.ApplicationVersion);
    }

    [Fact]
    public void EnvironmentDiagnostics_ShouldBePopulated()
    {
        Assert.False(string.IsNullOrWhiteSpace(_environment.FrameworkDescription));
        Assert.False(string.IsNullOrWhiteSpace(_environment.OperatingSystem));
        Assert.False(string.IsNullOrWhiteSpace(_environment.ProcessArchitecture));
    }

    [Fact]
    public void ProcessArchitecture_ShouldBe64Bit()
    {
        Assert.True(_environment.Is64BitProcess);
    }
}
