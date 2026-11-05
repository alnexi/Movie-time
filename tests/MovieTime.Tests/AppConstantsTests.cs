using MovieTime.Core;
using Xunit;

namespace MovieTime.Tests;

public class AppConstantsTests
{
    [Fact]
    public void ApplicationName_ShouldBeMovieTime()
    {
        Assert.Equal("Movie Time", AppConstants.ApplicationName);
    }

    [Fact]
    public void ApplicationVersion_ShouldNotBeNullOrEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppConstants.ApplicationVersion));
    }

    [Fact]
    public void LocalDataFolder_ShouldBeMovieTime()
    {
        Assert.Equal("MovieTime", AppConstants.LocalDataFolder);
    }
}
