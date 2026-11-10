using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.Common;
using MovieTime.Core.Artwork;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;

using MovieTime.Core.Library;

namespace MovieTime.App.ViewModels;

/// <summary>
/// ViewModel managing the cinematic Movie Details experience.
/// Presents real local media metadata, genuine file paths, multiple editions, and offline local library recommendations.
/// </summary>
public sealed class MovieDetailsViewModel : ObservableObject
{
    private readonly IMovieRepository _movieRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IPlaylistRepository? _playlistRepository;
    private readonly IArtworkPipelineService? _artworkPipelineService;
    private readonly IDispatcherService? _dispatcher;
    private readonly IMediaRemovalService? _mediaRemovalService;
    private readonly ILogger<MovieDetailsViewModel> _logger;

    public ObservableCollection<Playlist> AvailablePlaylists { get; } = new();
    public ICommand AddToPlaylistCommand { get; }
    public ICommand LoadAvailablePlaylistsCommand { get; }

    private int _movieId;
    private string _title = string.Empty;
    private int? _releaseYear;
    private string? _edition;
    private string? _posterPath;
    private string? _backdropPath;
    private bool _isLoading;
    private string? _playbackStatusMessage;
    private LocalMediaFileItemViewModel? _selectedMediaFile;

    public event Action<string>? NavigationRequested;
    public event Action<string>? PlaybackRequested;

    public int MovieId
    {
        get => _movieId;
        private set
        {
            if (SetProperty(ref _movieId, value))
            {
                OnPropertyChanged(nameof(IsMovieFound));
            }
        }
    }

    public bool IsMovieFound => MovieId > 0;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public int? ReleaseYear
    {
        get => _releaseYear;
        set
        {
            if (SetProperty(ref _releaseYear, value))
            {
                OnPropertyChanged(nameof(YearDisplay));
                OnPropertyChanged(nameof(HasYear));
            }
        }
    }

    public string YearDisplay => ReleaseYear.HasValue && ReleaseYear.Value > 0
        ? ReleaseYear.Value.ToString()
        : string.Empty;

    public bool HasYear => ReleaseYear.HasValue && ReleaseYear.Value > 0;

    public string? Edition
    {
        get => _edition;
        set
        {
            if (SetProperty(ref _edition, value))
            {
                OnPropertyChanged(nameof(HasEdition));
            }
        }
    }

    public bool HasEdition => !string.IsNullOrWhiteSpace(Edition);

    public string? PosterPath
    {
        get => _posterPath;
        set
        {
            if (SetProperty(ref _posterPath, value))
            {
                OnPropertyChanged(nameof(HasPoster));
            }
        }
    }

    public string? BackdropPath
    {
        get => _backdropPath;
        set
        {
            if (SetProperty(ref _backdropPath, value))
            {
                OnPropertyChanged(nameof(HasBackdrop));
                OnPropertyChanged(nameof(NeedsBackdropFallback));
            }
        }
    }

    public bool HasPoster => !string.IsNullOrWhiteSpace(PosterPath) && File.Exists(PosterPath);
    public bool HasBackdrop => !string.IsNullOrWhiteSpace(BackdropPath) && File.Exists(BackdropPath);
    public bool NeedsBackdropFallback => !HasBackdrop;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public string? PlaybackStatusMessage
    {
        get => _playbackStatusMessage;
        set
        {
            if (SetProperty(ref _playbackStatusMessage, value))
            {
                OnPropertyChanged(nameof(HasPlaybackStatus));
            }
        }
    }

    public bool HasPlaybackStatus => !string.IsNullOrWhiteSpace(PlaybackStatusMessage);

    public ObservableCollection<LocalMediaFileItemViewModel> MediaFiles { get; } = new();
    public ObservableCollection<MoviePosterItemViewModel> RecommendedMovies { get; } = new();

    public bool HasMultipleFiles => MediaFiles.Count > 1;
    public bool HasRecommendations => RecommendedMovies.Count > 0;

    public LocalMediaFileItemViewModel? SelectedMediaFile
    {
        get => _selectedMediaFile;
        set
        {
            if (SetProperty(ref _selectedMediaFile, value))
            {
                foreach (var file in MediaFiles)
                {
                    file.IsSelected = _selectedMediaFile != null && file.Id == _selectedMediaFile.Id;
                }
                NotifyDerivedMediaProperties();
            }
        }
    }

    // Derived metadata properties from SelectedMediaFile
    public string? QualityBadge => SelectedMediaFile?.QualityTag;
    public bool HasQualityBadge => !string.IsNullOrWhiteSpace(QualityBadge);

    public string? ResolutionDisplay => SelectedMediaFile?.ResolutionDisplay;
    public bool HasResolution => !string.IsNullOrWhiteSpace(ResolutionDisplay);

    public string? VideoCodecDisplay => SelectedMediaFile?.VideoCodec;
    public bool HasVideoCodec => !string.IsNullOrWhiteSpace(VideoCodecDisplay);

    public string? AudioCodecDisplay => SelectedMediaFile?.AudioCodec;
    public bool HasAudioCodec => !string.IsNullOrWhiteSpace(AudioCodecDisplay);

    public string? FormatDisplay => SelectedMediaFile?.ContainerFormat?.ToUpperInvariant();
    public bool HasFormat => !string.IsNullOrWhiteSpace(FormatDisplay);

    public string? FileSizeDisplay => SelectedMediaFile?.FileSizeDisplay;
    public bool HasFileSize => !string.IsNullOrWhiteSpace(FileSizeDisplay);

    public string? LocalFilePath => SelectedMediaFile?.FilePath;
    public string? LocalFileName => SelectedMediaFile?.FileName;
    public bool HasLocalFile => !string.IsNullOrWhiteSpace(LocalFilePath);

    public ICommand PlayCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SelectMediaFileCommand { get; }
    public ICommand SelectRecommendedMovieCommand { get; }

    public ICommand NavigateHomeCommand { get; }

    public ICommand RemoveMovieFromLibraryCommand { get; }

    public ICommand DeleteMovieFromDiskCommand { get; }

    public MovieDetailsViewModel(
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        ILogger<MovieDetailsViewModel>? logger = null)
        : this(movieRepository, mediaFileRepository, null, null, null, null, logger)
    {
    }

    public MovieDetailsViewModel(
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        IArtworkPipelineService? artworkPipelineService,
        IDispatcherService? dispatcher = null,
        IPlaylistRepository? playlistRepository = null,
        IMediaRemovalService? mediaRemovalService = null,
        ILogger<MovieDetailsViewModel>? logger = null)
    {
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _artworkPipelineService = artworkPipelineService;
        _dispatcher = dispatcher;
        _playlistRepository = playlistRepository;
        _mediaRemovalService = mediaRemovalService;
        _logger = logger ?? NullLogger<MovieDetailsViewModel>.Instance;

        if (_artworkPipelineService != null)
        {
            _artworkPipelineService.ArtworkUpdated += OnArtworkUpdated;
        }

        PlayCommand = new RelayCommand(OnPlay);
        BackCommand = new RelayCommand(OnBack);
        NavigateHomeCommand = new RelayCommand(() => NavigationRequested?.Invoke("Home"));
        RemoveMovieFromLibraryCommand = new AsyncRelayCommand(RemoveMovieFromLibraryAsync);
        DeleteMovieFromDiskCommand = new AsyncRelayCommand(DeleteMovieFromDiskAsync);
        SelectMediaFileCommand = new RelayCommand<LocalMediaFileItemViewModel>(SelectMediaFile);
        SelectRecommendedMovieCommand = new AsyncRelayCommand<MoviePosterItemViewModel>(async m =>
        {
            if (m != null)
            {
                await LoadMovieAsync(m.Id);
            }
        });

        LoadAvailablePlaylistsCommand = new AsyncRelayCommand(LoadAvailablePlaylistsAsync);
        AddToPlaylistCommand = new AsyncRelayCommand<Playlist>(AddToPlaylistAsync);
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

    public async Task LoadAvailablePlaylistsAsync()
    {
        if (_playlistRepository == null) return;
        var list = await _playlistRepository.GetAllAsync().ConfigureAwait(false);
        RunOnUi(() =>
        {
            AvailablePlaylists.Clear();
            foreach (var p in list)
            {
                AvailablePlaylists.Add(p);
            }
        });
    }

    public async Task AddToPlaylistAsync(Playlist? playlist)
    {
        if (playlist == null || _playlistRepository == null || MovieId <= 0) return;
        await _playlistRepository.AddItemAsync(playlist.Id, MovieId).ConfigureAwait(false);
        RunOnUi(() =>
        {
            PlaybackStatusMessage = $"Added to \"{playlist.Name}\"";
        });
    }

    private void OnArtworkUpdated(int movieId, string? posterPath, string? backdropPath)
    {
        void Update()
        {
            if (MovieId == movieId)
            {
                PosterPath = posterPath;
                BackdropPath = backdropPath;
            }

            var rec = RecommendedMovies.FirstOrDefault(m => m.Id == movieId);
            if (rec != null)
            {
                rec.UpdateArtwork(posterPath, backdropPath);
            }
        }

        if (_dispatcher != null)
        {
            _dispatcher.RunOnUi(Update);
        }
        else
        {
            Update();
        }
    }

    public async Task LoadMovieAsync(int? movieId)
    {
        if (!movieId.HasValue || movieId.Value <= 0)
        {
            ResetState();
            return;
        }

        try
        {
            IsLoading = true;
            PlaybackStatusMessage = null;

            var movie = await _movieRepository.GetByIdAsync(movieId.Value);
            if (movie == null)
            {
                _logger.LogWarning("Movie with ID {MovieId} was not found in SQLite repository.", movieId.Value);
                ResetState();
                return;
            }

            MovieId = movie.Id;
            Title = movie.Title;
            ReleaseYear = movie.ReleaseYear;
            Edition = movie.Edition;
            PosterPath = movie.PosterPath;
            BackdropPath = movie.BackdropPath;

            // Load associated local physical media files
            var files = await _mediaFileRepository.GetByMovieIdAsync(movie.Id);
            MediaFiles.Clear();

            foreach (var f in files)
            {
                var fileVm = LocalMediaFileItemViewModel.FromEntity(f);
                fileVm.SelectCommand = new RelayCommand(() => SelectMediaFile(fileVm));
                fileVm.RemoveFromLibraryCommand = new AsyncRelayCommand(async () => await RemoveMediaFileFromLibraryAsync(fileVm));
                fileVm.DeleteFromDiskCommand = new AsyncRelayCommand(async () => await DeleteMediaFileFromDiskAsync(fileVm));
                MediaFiles.Add(fileVm);
            }

            SelectedMediaFile = MediaFiles.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath)) ?? MediaFiles.FirstOrDefault();
            OnPropertyChanged(nameof(HasMultipleFiles));

            // Load local recommendations ("More from your library" - real local movies only)
            await LoadRecommendationsAsync(movie.Id);

            // Refresh available playlists for quick add
            await LoadAvailablePlaylistsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load movie details for ID {MovieId}.", movieId.Value);
            ResetState();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadRecommendationsAsync(int currentMovieId)
    {
        RecommendedMovies.Clear();
        try
        {
            var allMovies = await _movieRepository.GetAllAsync();
            var allFiles = await _mediaFileRepository.GetAllAsync();
            var presentMovieIds = allFiles
                .Where(f => f.MovieId.HasValue && !f.IsMissing)
                .Select(f => f.MovieId!.Value)
                .ToHashSet();

            var candidates = allMovies
                .Where(m => m.Id != currentMovieId && (allFiles.Count == 0 || presentMovieIds.Contains(m.Id)))
                .ToList();

            foreach (var rec in candidates)
            {
                var files = allFiles.Where(f => f.MovieId == rec.Id).ToList();
                var primaryFile = files.FirstOrDefault(f => !f.IsMissing) ?? files.FirstOrDefault();

                string? quality = null;
                if (primaryFile != null)
                {
                    if (primaryFile.VideoHeight >= 2160 || primaryFile.VideoWidth >= 3840) quality = "4K HDR";
                    else if (primaryFile.VideoHeight >= 1080 || primaryFile.VideoWidth >= 1920) quality = "1080p HD";
                    else if (primaryFile.VideoHeight >= 720 || primaryFile.VideoWidth >= 1280) quality = "720p HD";
                }

                RecommendedMovies.Add(new MoviePosterItemViewModel
                {
                    Id = rec.Id,
                    Title = rec.Title,
                    ReleaseYear = rec.ReleaseYear,
                    Edition = rec.Edition,
                    PosterPath = rec.PosterPath,
                    BackdropPath = rec.BackdropPath,
                    QualityTag = quality,
                    CreatedAt = rec.CreatedAt,
                    SelectCommand = new RelayCommand<MoviePosterItemViewModel>(async m =>
                    {
                        if (m != null) await LoadMovieAsync(m.Id);
                    })
                });
            }

            OnPropertyChanged(nameof(HasRecommendations));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load recommendations from local library.");
        }
    }

    public void SelectMediaFile(LocalMediaFileItemViewModel? file)
    {
        if (file == null) return;
        SelectedMediaFile = file;
    }

    public void OnPlay()
    {
        if (SelectedMediaFile != null && !string.IsNullOrWhiteSpace(SelectedMediaFile.FilePath))
        {
            if (SelectedMediaFile.IsMissing)
            {
                PlaybackStatusMessage = "This media file is currently unavailable or missing from disk.";
                return;
            }

            _logger.LogInformation("Playback initiated for movie '{Title}' using file '{FilePath}'.", Title, SelectedMediaFile.FilePath);
            PlaybackStatusMessage = $"Ready for playback: '{SelectedMediaFile.FileName}'.";
            PlaybackRequested?.Invoke(SelectedMediaFile.FilePath);
        }
        else
        {
            PlaybackStatusMessage = "No local media file available for this title.";
        }
    }

    public void OnBack()
    {
        NavigationRequested?.Invoke("Movies");
    }



    public async Task RemoveMovieFromLibraryAsync()
    {
        if (_mediaRemovalService == null) return;

        if (MovieId > 0)
        {
            await _mediaRemovalService.RemoveMovieFromLibraryAsync(MovieId);
        }
        else
        {
            foreach (var file in MediaFiles.ToList())
            {
                await _mediaRemovalService.RemoveFromLibraryOnlyAsync(file.FilePath);
            }
        }

        NavigationRequested?.Invoke("Movies");
    }

    public async Task DeleteMovieFromDiskAsync()
    {
        if (_mediaRemovalService == null) return;

        foreach (var file in MediaFiles.ToList())
        {
            if (!string.IsNullOrWhiteSpace(file.FilePath) && File.Exists(file.FilePath))
            {
                await _mediaRemovalService.DeleteFileToRecycleBinAsync(file.FilePath);
            }
        }

        if (MovieId > 0)
        {
            await _mediaRemovalService.RemoveMovieFromLibraryAsync(MovieId, "DeletedFromDisk");
        }

        NavigationRequested?.Invoke("Movies");
    }



    public async Task RemoveMediaFileFromLibraryAsync(LocalMediaFileItemViewModel? file)

    {

        if (file == null || string.IsNullOrWhiteSpace(file.FilePath) || _mediaRemovalService == null) return;

        await _mediaRemovalService.RemoveFromLibraryOnlyAsync(file.FilePath);

        MediaFiles.Remove(file);

        if (MediaFiles.Count == 0)

        {

            NavigationRequested?.Invoke("Movies");

        }

        else

        {

            SelectedMediaFile = MediaFiles.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath)) ?? MediaFiles.FirstOrDefault();

            OnPropertyChanged(nameof(HasMultipleFiles));

        }

    }



    public async Task DeleteMediaFileFromDiskAsync(LocalMediaFileItemViewModel? file)

    {

        if (file == null || string.IsNullOrWhiteSpace(file.FilePath) || _mediaRemovalService == null) return;

        await _mediaRemovalService.DeleteFileToRecycleBinAsync(file.FilePath);

        MediaFiles.Remove(file);

        if (MediaFiles.Count == 0)

        {

            NavigationRequested?.Invoke("Movies");

        }

        else

        {

            SelectedMediaFile = MediaFiles.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath)) ?? MediaFiles.FirstOrDefault();

            OnPropertyChanged(nameof(HasMultipleFiles));

        }

    }



    private void ResetState()
    {
        MovieId = 0;
        Title = string.Empty;
        ReleaseYear = null;
        Edition = null;
        PosterPath = null;
        BackdropPath = null;
        SelectedMediaFile = null;
        MediaFiles.Clear();
        RecommendedMovies.Clear();
        PlaybackStatusMessage = null;

        OnPropertyChanged(nameof(HasMultipleFiles));
        OnPropertyChanged(nameof(HasRecommendations));
        OnPropertyChanged(nameof(NeedsBackdropFallback));
        NotifyDerivedMediaProperties();
    }

    private void NotifyDerivedMediaProperties()
    {
        OnPropertyChanged(nameof(QualityBadge));
        OnPropertyChanged(nameof(HasQualityBadge));
        OnPropertyChanged(nameof(ResolutionDisplay));
        OnPropertyChanged(nameof(HasResolution));
        OnPropertyChanged(nameof(VideoCodecDisplay));
        OnPropertyChanged(nameof(HasVideoCodec));
        OnPropertyChanged(nameof(AudioCodecDisplay));
        OnPropertyChanged(nameof(HasAudioCodec));
        OnPropertyChanged(nameof(FormatDisplay));
        OnPropertyChanged(nameof(HasFormat));
        OnPropertyChanged(nameof(FileSizeDisplay));
        OnPropertyChanged(nameof(HasFileSize));
        OnPropertyChanged(nameof(LocalFilePath));
        OnPropertyChanged(nameof(LocalFileName));
        OnPropertyChanged(nameof(HasLocalFile));
    }
}
