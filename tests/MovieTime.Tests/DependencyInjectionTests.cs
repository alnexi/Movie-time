using Microsoft.Extensions.DependencyInjection;
using MovieTime.Core.Extensions;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public class DependencyInjectionTests
{
    [Fact]
    public void AddMovieTimeCore_ShouldRegisterStoragePathService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMovieTimeCore();
        var provider = services.BuildServiceProvider();

        // Act
        var storage = provider.GetService<IStoragePathService>();

        // Assert
        Assert.NotNull(storage);
        Assert.IsType<StoragePathService>(storage);
    }

    [Fact]
    public void AddMovieTimeCore_ShouldRegisterAppEnvironment()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMovieTimeCore();
        var provider = services.BuildServiceProvider();

        // Act
        var environment = provider.GetService<IAppEnvironment>();

        // Assert
        Assert.NotNull(environment);
        Assert.IsType<AppEnvironment>(environment);
    }

    [Fact]
    public void AddMovieTimeCore_ShouldRegisterDatabaseService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMovieTimeCore();
        var provider = services.BuildServiceProvider();

        // Act
        var db = provider.GetService<MovieTime.Core.Database.IDatabaseService>();

        // Assert
        Assert.NotNull(db);
        Assert.IsType<MovieTime.Core.Database.DatabaseService>(db);
    }

    [Fact]
    public void AddMovieTimeCore_ShouldRegisterFilenameParserEngine()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMovieTimeCore();
        var provider = services.BuildServiceProvider();

        // Act
        var parser = provider.GetService<MovieTime.Core.Parsing.IFilenameParserEngine>();

        // Assert
        Assert.NotNull(parser);
        Assert.IsType<MovieTime.Core.Parsing.FilenameParserEngine>(parser);
    }

    [Fact]
    public void AddMovieTimeCore_ShouldRegisterServicesAsSingletons()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddMovieTimeCore();
        var provider = services.BuildServiceProvider();

        // Act
        var storage1 = provider.GetRequiredService<IStoragePathService>();
        var storage2 = provider.GetRequiredService<IStoragePathService>();

        var env1 = provider.GetRequiredService<IAppEnvironment>();
        var env2 = provider.GetRequiredService<IAppEnvironment>();

        var db1 = provider.GetRequiredService<MovieTime.Core.Database.IDatabaseService>();
        var db2 = provider.GetRequiredService<MovieTime.Core.Database.IDatabaseService>();

        var scanner1 = provider.GetRequiredService<MovieTime.Core.Scanning.IMediaScannerService>();
        var scanner2 = provider.GetRequiredService<MovieTime.Core.Scanning.IMediaScannerService>();

        var parser1 = provider.GetRequiredService<MovieTime.Core.Parsing.IFilenameParserEngine>();
        var parser2 = provider.GetRequiredService<MovieTime.Core.Parsing.IFilenameParserEngine>();

        var rootService1 = provider.GetRequiredService<MovieTime.Core.Library.IScanRootService>();
        var rootService2 = provider.GetRequiredService<MovieTime.Core.Library.IScanRootService>();

        var importService1 = provider.GetRequiredService<MovieTime.Core.Library.ILibraryImportService>();
        var importService2 = provider.GetRequiredService<MovieTime.Core.Library.ILibraryImportService>();

        var movieRepo1 = provider.GetRequiredService<MovieTime.Core.Database.Repositories.IMovieRepository>();
        var movieRepo2 = provider.GetRequiredService<MovieTime.Core.Database.Repositories.IMovieRepository>();

        // Assert
        Assert.Same(storage1, storage2);
        Assert.Same(env1, env2);
        Assert.Same(db1, db2);
        Assert.Same(scanner1, scanner2);
        Assert.Same(parser1, parser2);
        Assert.Same(rootService1, rootService2);
        Assert.Same(importService1, importService2);
        Assert.Same(movieRepo1, movieRepo2);
    }
}
