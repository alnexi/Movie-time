using MovieTime.Core;
using Xunit;

namespace MovieTime.Tests;

public class AppConstantsTests
{
    [Fact]
    public void ApplicationName_ShouldBeRiver()
    {
        Assert.Equal("River", AppConstants.ApplicationName);
    }

    [Fact]
    public void ApplicationVersion_ShouldNotBeNullOrEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppConstants.ApplicationVersion));
        Assert.Equal("1.2.2", AppConstants.ApplicationVersion);
    }

    [Fact]
    public void LocalDataFolder_ShouldBeMovieTime()
    {
        Assert.Equal("MovieTime", AppConstants.LocalDataFolder);
    }
}
