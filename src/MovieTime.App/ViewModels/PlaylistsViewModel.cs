using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.Common;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;

namespace MovieTime.App.ViewModels;

public sealed class PlaylistsViewModel : ObservableObject
{
    private readonly IPlaylistRepository _playlistRepository;
    private readonly IMovieRepository _movieRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IDispatcherService? _dispatcher;
    private readonly ILogger<PlaylistsViewModel> _logger;

    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private Playlist? _selectedPlaylist;
    private bool _isLoading;
    private bool _isCreateDialogOpen;
    private bool _isRenameDialogOpen;
    private string _createName = string.Empty;
    private string _renameName = string.Empty;
    private string? _statusMessage;

    public event Action<string>? NavigationRequested;
    public event Action<string, int?, List<(string FilePath, int? MovieId, string Title)>, int>? PlaybackRequested;

    public ObservableCollection<Playlist> Playlists { get; } = new();
    public ObservableCollection<PlaylistDetailItemViewModel> CurrentItems { get; } = new();

    public Playlist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            if (SetProperty(ref _selectedPlaylist, value))
            {
                OnPropertyChanged(nameof(HasSelectedPlaylist));
                OnPropertyChanged(nameof(SelectedPlaylistName));
                _ = LoadSelectedPlaylistItemsAsync();
            }
        }
    }

    public bool HasSelectedPlaylist => SelectedPlaylist != null;
    public string SelectedPlaylistName => SelectedPlaylist?.Name ?? string.Empty;
    public bool HasPlaylists => Playlists.Count > 0;
    public bool IsEmptyPlaylist => HasSelectedPlaylist && CurrentItems.Count == 0;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsCreateDialogOpen
    {
        get => _isCreateDialogOpen;
        set => SetProperty(ref _isCreateDialogOpen, value);
    }

    public bool IsRenameDialogOpen
    {
        get => _isRenameDialogOpen;
        set => SetProperty(ref _isRenameDialogOpen, value);
    }

    public string CreateName
    {
        get => _createName;
        set => SetProperty(ref _createName, value);
    }

    public string RenameName
    {
        get => _renameName;
        set => SetProperty(ref _renameName, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public ICommand NavigateHomeCommand { get; }
    public ICommand OpenCreateDialogCommand { get; }
    public ICommand CloseCreateDialogCommand { get; }
    public ICommand ConfirmCreateCommand { get; }
    public ICommand OpenRenameDialogCommand { get; }
    public ICommand CloseRenameDialogCommand { get; }
    public ICommand ConfirmRenameCommand { get; }
    public ICommand DeleteSelectedCommand { get; }
    public ICommand PlayAllCommand { get; }
    public ICommand MoveItemUpCommand { get; }
    public ICommand MoveItemDownCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand PlayItemCommand { get; }

    public PlaylistsViewModel(
        IPlaylistRepository playlistRepository,
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        IDispatcherService? dispatcher = null,
        ILogger<PlaylistsViewModel>? logger = null)
    {
        _playlistRepository = playlistRepository ?? throw new ArgumentNullException(nameof(playlistRepository));
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<PlaylistsViewModel>.Instance;

        NavigateHomeCommand = new RelayCommand(() => NavigationRequested?.Invoke("Home"));
        OpenCreateDialogCommand = new RelayCommand(() =>
        {
            CreateName = string.Empty;
            IsCreateDialogOpen = true;
        });
        CloseCreateDialogCommand = new RelayCommand(() => IsCreateDialogOpen = false);
        ConfirmCreateCommand = new AsyncRelayCommand(CreatePlaylistAsync);

        OpenRenameDialogCommand = new RelayCommand(() =>
        {
            if (SelectedPlaylist != null)
            {
                RenameName = SelectedPlaylist.Name;
                IsRenameDialogOpen = true;
            }
        });
        CloseRenameDialogCommand = new RelayCommand(() => IsRenameDialogOpen = false);
        ConfirmRenameCommand = new AsyncRelayCommand(RenamePlaylistAsync);

        DeleteSelectedCommand = new AsyncRelayCommand(DeleteSelectedPlaylistAsync);
        PlayAllCommand = new RelayCommand(PlayAllSequentially);
        MoveItemUpCommand = new AsyncRelayCommand<PlaylistDetailItemViewModel>(MoveItemUpAsync);
        MoveItemDownCommand = new AsyncRelayCommand<PlaylistDetailItemViewModel>(MoveItemDownAsync);
        RemoveItemCommand = new AsyncRelayCommand<PlaylistDetailItemViewModel>(RemoveItemAsync);
        PlayItemCommand = new RelayCommand<PlaylistDetailItemViewModel>(PlayItem);
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher != null)
        {
            _dispatcher.RunOnUi(action);
        }
        else
        {
            action();
        }
    }

    public async Task InitializeAsync()
    {
        IsLoading = true;
        try
        {
            var playlists = await _playlistRepository.GetAllAsync().ConfigureAwait(false);

            RunOnUi(() =>
            {
                Playlists.Clear();
                foreach (var p in playlists)
                {
                    Playlists.Add(p);
                }

                OnPropertyChanged(nameof(HasPlaylists));

                if (SelectedPlaylist == null || !Playlists.Any(p => p.Id == SelectedPlaylist.Id))
                {
                    SelectedPlaylist = Playlists.FirstOrDefault();
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load playlists.");
        }
        finally
        {
            RunOnUi(() => IsLoading = false);
        }
    }

    public async Task LoadSelectedPlaylistItemsAsync()
    {
        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (SelectedPlaylist == null)
            {
                RunOnUi(() =>
                {
                    CurrentItems.Clear();
                    OnPropertyChanged(nameof(IsEmptyPlaylist));
                });
                return;
            }

            var items = await _playlistRepository.GetItemsByPlaylistIdAsync(SelectedPlaylist.Id).ConfigureAwait(false);
            var detailItems = new List<PlaylistDetailItemViewModel>();

            foreach (var item in items)
            {
                var movie = await _movieRepository.GetByIdAsync(item.MovieId).ConfigureAwait(false);
                if (movie == null) continue;

                var files = await _mediaFileRepository.GetByMovieIdAsync(movie.Id).ConfigureAwait(false);
                var activeFile = files.FirstOrDefault(f => !f.IsMissing) ?? files.FirstOrDefault();
                var durationMinutes = (activeFile != null && activeFile.DurationTicks > 0)
                    ? (int)TimeSpan.FromTicks(activeFile.DurationTicks).TotalMinutes
                    : 0;

                detailItems.Add(new PlaylistDetailItemViewModel
                {
                    PlaylistItemId = item.Id,
                    MovieId = movie.Id,
                    Title = movie.Title,
                    ReleaseYear = movie.ReleaseYear,
                    FormattedDuration = durationMinutes > 0 ? $"{durationMinutes} min" : string.Empty,
                    PosterPath = movie.PosterPath,
                    FilePath = activeFile?.FilePath ?? string.Empty,
                    IsMissing = activeFile?.IsMissing ?? false,
                    SortOrder = item.SortOrder
                });
            }

            RunOnUi(() =>
            {
                CurrentItems.Clear();
                foreach (var di in detailItems)
                {
                    CurrentItems.Add(di);
                }
                OnPropertyChanged(nameof(IsEmptyPlaylist));
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load items for playlist {Id}", SelectedPlaylist?.Id);
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task CreatePlaylistAsync()
    {
        if (string.IsNullOrWhiteSpace(CreateName)) return;

        var playlist = new Playlist
        {
            Name = CreateName.Trim()
        };

        await _playlistRepository.InsertAsync(playlist).ConfigureAwait(false);
        IsCreateDialogOpen = false;

        await InitializeAsync().ConfigureAwait(false);
        SelectedPlaylist = Playlists.FirstOrDefault(p => p.Id == playlist.Id);
    }

    private async Task RenamePlaylistAsync()
    {
        if (SelectedPlaylist == null || string.IsNullOrWhiteSpace(RenameName)) return;

        SelectedPlaylist.Name = RenameName.Trim();
        await _playlistRepository.UpdateAsync(SelectedPlaylist).ConfigureAwait(false);
        IsRenameDialogOpen = false;

        OnPropertyChanged(nameof(SelectedPlaylistName));
        await InitializeAsync().ConfigureAwait(false);
    }

    private async Task DeleteSelectedPlaylistAsync()
    {
        if (SelectedPlaylist == null) return;

        var idToDelete = SelectedPlaylist.Id;
        await _playlistRepository.DeleteAsync(idToDelete).ConfigureAwait(false);

        await InitializeAsync().ConfigureAwait(false);
    }

    public async Task AddMovieToPlaylistAsync(int playlistId, int movieId)
    {
        await _playlistRepository.AddItemAsync(playlistId, movieId).ConfigureAwait(false);
        if (SelectedPlaylist?.Id == playlistId)
        {
            await LoadSelectedPlaylistItemsAsync().ConfigureAwait(false);
        }
    }

    private async Task RemoveItemAsync(PlaylistDetailItemViewModel? item)
    {
        if (item == null) return;

        await _playlistRepository.RemoveItemAsync(item.PlaylistItemId).ConfigureAwait(false);
        await LoadSelectedPlaylistItemsAsync().ConfigureAwait(false);
    }

    private async Task MoveItemUpAsync(PlaylistDetailItemViewModel? item)
    {
        if (item == null || SelectedPlaylist == null) return;

        var index = CurrentItems.IndexOf(item);
        if (index <= 0) return;

        CurrentItems.Move(index, index - 1);
        var itemIdsInOrder = CurrentItems.Select(ci => ci.PlaylistItemId).ToList();
        await _playlistRepository.UpdateItemOrdersAsync(SelectedPlaylist.Id, itemIdsInOrder).ConfigureAwait(false);
    }

    private async Task MoveItemDownAsync(PlaylistDetailItemViewModel? item)
    {
        if (item == null || SelectedPlaylist == null) return;

        var index = CurrentItems.IndexOf(item);
        if (index < 0 || index >= CurrentItems.Count - 1) return;

        CurrentItems.Move(index, index + 1);
        var itemIdsInOrder = CurrentItems.Select(ci => ci.PlaylistItemId).ToList();
        await _playlistRepository.UpdateItemOrdersAsync(SelectedPlaylist.Id, itemIdsInOrder).ConfigureAwait(false);
    }

    private void PlayAllSequentially()
    {
        if (CurrentItems.Count == 0) return;

        var validItems = CurrentItems.Where(i => i != null && !string.IsNullOrEmpty(i.FilePath) && !i.IsMissing).ToList();
        if (validItems.Count == 0) return;

        var queue = validItems.Select(i => (i.FilePath, (int?)i.MovieId, i.Title)).ToList();
        var first = validItems[0];
        PlaybackRequested?.Invoke(first.FilePath, first.MovieId, queue, 0);
    }

    private void PlayItem(PlaylistDetailItemViewModel? item)
    {
        if (item == null || string.IsNullOrEmpty(item.FilePath) || item.IsMissing) return;

        var validItems = CurrentItems.Where(i => i != null && !string.IsNullOrEmpty(i.FilePath) && !i.IsMissing).ToList();
        var index = validItems.IndexOf(item);
        if (index < 0) index = 0;

        var queue = validItems.Select(i => (i.FilePath, (int?)i.MovieId, i.Title)).ToList();
        PlaybackRequested?.Invoke(item.FilePath, item.MovieId, queue, index);
    }
}
