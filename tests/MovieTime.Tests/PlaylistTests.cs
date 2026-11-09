using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.ViewModels;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Playback;
using MovieTime.Core.Services;
using Xunit;

namespace MovieTime.Tests;

public sealed class PlaylistTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _dbPath;
    private readonly DatabaseService _dbService;
    private readonly PlaylistRepository _playlistRepo;
    private readonly MovieRepository _movieRepo;
    private readonly MediaFileRepository _mediaFileRepo;

    public PlaylistTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "MovieTime_PlaylistTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _dbPath = Path.Combine(_tempDir, "test_playlists.db");

        var storageMock = new StoragePathService(_tempDir);
        _dbService = new DatabaseService(storageMock, NullLogger<DatabaseService>.Instance, _dbPath);
        _dbService.InitializeAsync().GetAwaiter().GetResult();

        _playlistRepo = new PlaylistRepository(_dbService);
        _movieRepo = new MovieRepository(_dbService);
        _mediaFileRepo = new MediaFileRepository(_dbService);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task PlaylistRepository_CreateRenameDelete_PersistsCorrectly()
    {
        // 1. Create
        var playlist = new Playlist { Name = "Weekend Sci-Fi" };
        var insertResult = await _playlistRepo.InsertAsync(playlist);
        Assert.True(insertResult > 0);
        Assert.True(playlist.Id > 0);

        // 2. Read
        var retrieved = await _playlistRepo.GetByIdAsync(playlist.Id);
        Assert.NotNull(retrieved);
        Assert.Equal("Weekend Sci-Fi", retrieved.Name);

        // 3. Rename
        retrieved.Name = "Classic Sci-Fi";
        await _playlistRepo.UpdateAsync(retrieved);
        var updated = await _playlistRepo.GetByIdAsync(playlist.Id);
        Assert.NotNull(updated);
        Assert.Equal("Classic Sci-Fi", updated.Name);

        // 4. Delete
        await _playlistRepo.DeleteAsync(playlist.Id);
        var deleted = await _playlistRepo.GetByIdAsync(playlist.Id);
        Assert.Null(deleted);
    }

    [Fact]
    public async Task PlaylistRepository_AddRemoveReorder_MaintainsOrder()
    {
        var playlist = new Playlist { Name = "Marvel Marathon" };
        await _playlistRepo.InsertAsync(playlist);

        var movie1 = new Movie { Title = "Iron Man", ReleaseYear = 2008 };
        var movie2 = new Movie { Title = "Thor", ReleaseYear = 2011 };
        var movie3 = new Movie { Title = "The Avengers", ReleaseYear = 2012 };
        await _movieRepo.InsertAsync(movie1);
        await _movieRepo.InsertAsync(movie2);
        await _movieRepo.InsertAsync(movie3);

        // Add items
        var item1Id = await _playlistRepo.AddItemAsync(playlist.Id, movie1.Id);
        var item2Id = await _playlistRepo.AddItemAsync(playlist.Id, movie2.Id);
        var item3Id = await _playlistRepo.AddItemAsync(playlist.Id, movie3.Id);

        var items = await _playlistRepo.GetItemsByPlaylistIdAsync(playlist.Id);
        Assert.Equal(3, items.Count);
        Assert.Equal(movie1.Id, items[0].MovieId);
        Assert.Equal(movie2.Id, items[1].MovieId);
        Assert.Equal(movie3.Id, items[2].MovieId);

        // Reorder (move Avengers to first position)
        await _playlistRepo.UpdateItemOrdersAsync(playlist.Id, new List<int> { item3Id, item1Id, item2Id });

        var reordered = await _playlistRepo.GetItemsByPlaylistIdAsync(playlist.Id);
        Assert.Equal(3, reordered.Count);
        Assert.Equal(movie3.Id, reordered[0].MovieId);
        Assert.Equal(movie1.Id, reordered[1].MovieId);
        Assert.Equal(movie2.Id, reordered[2].MovieId);

        // Remove Thor
        await _playlistRepo.RemoveItemAsync(item2Id);
        var afterRemove = await _playlistRepo.GetItemsByPlaylistIdAsync(playlist.Id);
        Assert.Equal(2, afterRemove.Count);
        Assert.DoesNotContain(afterRemove, i => i.MovieId == movie2.Id);
    }

    [Fact]
    public async Task PlaylistsViewModel_PlayAllSequentially_FiresPlaybackRequestedWithQueue()
    {
        var playlist = new Playlist { Name = "Nolan Trilogy" };
        await _playlistRepo.InsertAsync(playlist);

        var movie1 = new Movie { Title = "Batman Begins", ReleaseYear = 2005 };
        var movie2 = new Movie { Title = "The Dark Knight", ReleaseYear = 2008 };
        await _movieRepo.InsertAsync(movie1);
        await _movieRepo.InsertAsync(movie2);

        var dummyFile1 = Path.Combine(_tempDir, "bb.mkv");
        var dummyFile2 = Path.Combine(_tempDir, "tdk.mkv");
        File.WriteAllText(dummyFile1, "mock");
        File.WriteAllText(dummyFile2, "mock");

        await _mediaFileRepo.InsertAsync(new MediaFile { MovieId = movie1.Id, FilePath = dummyFile1, FileName = "bb.mkv", IsMissing = false });
        await _mediaFileRepo.InsertAsync(new MediaFile { MovieId = movie2.Id, FilePath = dummyFile2, FileName = "tdk.mkv", IsMissing = false });

        await _playlistRepo.AddItemAsync(playlist.Id, movie1.Id);
        await _playlistRepo.AddItemAsync(playlist.Id, movie2.Id);

        var vm = new PlaylistsViewModel(_playlistRepo, _movieRepo, _mediaFileRepo);
        await vm.InitializeAsync();
        vm.SelectedPlaylist = vm.Playlists.First(p => p.Id == playlist.Id);
        await vm.LoadSelectedPlaylistItemsAsync();

        string? requestedFile = null;
        int? requestedMovieId = null;
        List<(string FilePath, int? MovieId, string Title)>? requestedQueue = null;
        int requestedIndex = -1;

        vm.PlaybackRequested += (file, movieId, queue, idx) =>
        {
            requestedFile = file;
            requestedMovieId = movieId;
            requestedQueue = queue;
            requestedIndex = idx;
        };

        vm.PlayAllCommand.Execute(null);

        Assert.Equal(dummyFile1, requestedFile);
        Assert.Equal(movie1.Id, requestedMovieId);
        Assert.NotNull(requestedQueue);
        Assert.Equal(2, requestedQueue.Count);
        Assert.Equal(0, requestedIndex);
        Assert.Equal("Batman Begins", requestedQueue[0].Title);
        Assert.Equal("The Dark Knight", requestedQueue[1].Title);
    }
}
