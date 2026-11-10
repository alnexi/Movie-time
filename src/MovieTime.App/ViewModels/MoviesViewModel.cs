using MovieTime.Core.Library.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.Common;
using MovieTime.Core.Artwork;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Library;

namespace MovieTime.App.ViewModels;

/// <summary>
/// ViewModel managing the Movies Library experience,
/// including SQLite-backed movie catalog, category filtering, search, sorting, and focus states.
/// </summary>
public sealed class MoviesViewModel : ObservableObject
{
    private readonly IDatabaseService _databaseService;
    private readonly IMovieRepository _movieRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IScanRootService _scanRootService;
    private readonly ILibraryImportService _importService;
    private readonly IArtworkPipelineService? _artworkPipelineService;
    private readonly IDispatcherService? _dispatcher;
    private readonly ILogger<MoviesViewModel> _logger;

    private bool _isLoading;
    private bool _isLibraryEmpty = true;
    private bool _isSidebarOpen;
    private bool _isQuickActionOpen;
    private string _searchQuery = string.Empty;
    private string _selectedCategory = "All";
    private string _selectedSort = "Title";
    private string? _statusMessage;
    private MoviePosterItemViewModel? _selectedMovie;
    private string _viewMode = "PosterGrid"; // "PosterGrid" (2:3) or "LandscapeGrid" (16:9)

    public event Action<string>? NavigationRequested;

    public bool IsLoading
    {
        get => _isLoading;
        set => SetProperty(ref _isLoading, value);
    }

    public bool IsLibraryEmpty
    {
        get => _isLibraryEmpty;
        set => SetProperty(ref _isLibraryEmpty, value);
    }

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        set => SetProperty(ref _isSidebarOpen, value);
    }

    public bool IsQuickActionOpen
    {
        get => _isQuickActionOpen;
        set => SetProperty(ref _isQuickActionOpen, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyFiltersAndSorting();
                OnPropertyChanged(nameof(HasSearchQuery));
            }
        }
    }

    public bool HasSearchQuery => !string.IsNullOrWhiteSpace(SearchQuery);

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                UpdateCategorySelectionStates();
                ApplyFiltersAndSorting();
            }
        }
    }

    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (SetProperty(ref _selectedSort, value))
            {
                ApplyFiltersAndSorting();
            }
        }
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public MoviePosterItemViewModel? SelectedMovie
    {
        get => _selectedMovie;
        set
        {
            if (SetProperty(ref _selectedMovie, value))
            {
                OnPropertyChanged(nameof(HasSelectedMovie));
            }
        }
    }

    public bool HasSelectedMovie => SelectedMovie != null;

    public string ViewMode
    {
        get => _viewMode;
        set
        {
            if (SetProperty(ref _viewMode, value))
            {
                OnPropertyChanged(nameof(IsPosterMode));
                OnPropertyChanged(nameof(IsLandscapeMode));
            }
        }
    }

    public bool IsPosterMode => string.Equals(ViewMode, "PosterGrid", StringComparison.OrdinalIgnoreCase);
    public bool IsLandscapeMode => string.Equals(ViewMode, "LandscapeGrid", StringComparison.OrdinalIgnoreCase);

    public int TotalMovieCount => AllMovies.Count;
    public int DisplayedMovieCount => DisplayedMovies.Count;

    public string MovieCountSummary
    {
        get
        {
            if (IsLibraryEmpty) return "No Movies";
            if (HasSearchQuery || !string.Equals(SelectedCategory, "All", StringComparison.OrdinalIgnoreCase))
            {
                return $"{DisplayedMovieCount} of {TotalMovieCount} Movies";
            }
            return TotalMovieCount == 1 ? "1 Movie" : $"{TotalMovieCount} Movies";
        }
    }

    public ObservableCollection<MoviePosterItemViewModel> AllMovies { get; } = new();
    public ObservableCollection<MoviePosterItemViewModel> DisplayedMovies { get; } = new();
    public ObservableCollection<MoviesCategoryItemViewModel> Categories { get; } = new();

    public ICommand ToggleSidebarCommand { get; }
    public ICommand OpenSidebarCommand { get; }
    public ICommand CloseSidebarCommand { get; }
    public ICommand NavigateHomeCommand { get; }
    public ICommand ClearSearchCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand SetSortCommand { get; }
    public ICommand ToggleViewModeCommand { get; }
    public ICommand SelectMovieCommand { get; }
    public ICommand PlayMovieCommand { get; }
    public ICommand GoToDetailsCommand { get; }
    public ICommand DismissQuickActionCommand { get; }
    public ICommand NavigatePlaylistsCommand { get; }
    public ICommand RefreshLibraryCommand { get; }
    public ICommand ManageLibrariesCommand { get; }

    public MoviesViewModel(
        IDatabaseService databaseService,
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        IScanRootService scanRootService,
        ILibraryImportService importService,
        ILogger<MoviesViewModel>? logger = null)
        : this(databaseService, movieRepository, mediaFileRepository, scanRootService, importService, null, null, logger)
    {
    }

    public MoviesViewModel(
        IDatabaseService databaseService,
        IMovieRepository movieRepository,
        IMediaFileRepository mediaFileRepository,
        IScanRootService scanRootService,
        ILibraryImportService importService,
        IArtworkPipelineService? artworkPipelineService,
        IDispatcherService? dispatcher = null,
        ILogger<MoviesViewModel>? logger = null)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _scanRootService = scanRootService ?? throw new ArgumentNullException(nameof(scanRootService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _artworkPipelineService = artworkPipelineService;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<MoviesViewModel>.Instance;

        if (_artworkPipelineService != null)
        {
            _artworkPipelineService.ArtworkUpdated += OnArtworkUpdated;
        }

        _importService.ImportCompleted += OnImportCompleted;

        ToggleSidebarCommand = new RelayCommand(() => IsSidebarOpen = !IsSidebarOpen);
        OpenSidebarCommand = new RelayCommand(() => IsSidebarOpen = true);
        CloseSidebarCommand = new RelayCommand(() => IsSidebarOpen = false);
        NavigateHomeCommand = new RelayCommand(() => NavigationRequested?.Invoke("Home"));
        ClearSearchCommand = new RelayCommand(() => SearchQuery = string.Empty);
        SelectCategoryCommand = new RelayCommand<string>(categoryKey => SelectedCategory = categoryKey ?? "All");
        SetSortCommand = new RelayCommand<string>(sortKey => SelectedSort = sortKey ?? "Title");
        ToggleViewModeCommand = new RelayCommand(() => ViewMode = IsPosterMode ? "LandscapeGrid" : "PosterGrid");
        SelectMovieCommand = new RelayCommand<MoviePosterItemViewModel>(OnMovieSelected);
        PlayMovieCommand = new RelayCommand(OnPlaySelectedMovie);
        GoToDetailsCommand = new RelayCommand(OnGoToMovieDetails);
        DismissQuickActionCommand = new RelayCommand(OnDismissQuickAction);
        RefreshLibraryCommand = new AsyncRelayCommand(RefreshLibraryAsync);
        ManageLibrariesCommand = new RelayCommand(() => NavigationRequested?.Invoke("Settings"));
        NavigatePlaylistsCommand = new RelayCommand(() => NavigationRequested?.Invoke("Playlists"));

        InitializeCategories();
    }

    private void OnImportCompleted(object? sender, LibraryImportResult e)
    {
        RunOnUi(() =>
        {
            _ = LoadMoviesAsync();
        });
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

    private void OnArtworkUpdated(int movieId, string? posterPath, string? backdropPath)
    {
        void Update()
        {
            var match = AllMovies.FirstOrDefault(m => m.Id == movieId);
            if (match != null)
            {
                match.UpdateArtwork(posterPath, backdropPath);
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

    private void InitializeCategories()
    {
        Categories.Clear();

        // 1. Primary Filter Categories
        Categories.Add(new MoviesCategoryItemViewModel
        {
            Title = "All Movies",
            CategoryKey = "All",
            IconGlyph = "\uE714",
            IsSelected = true,
            Command = new RelayCommand(() => SelectedCategory = "All")
        });

        Categories.Add(new MoviesCategoryItemViewModel
        {
            Title = "Recently Added",
            CategoryKey = "Recent",
            IconGlyph = "\uE823",
            Command = new RelayCommand(() => SelectedCategory = "Recent")
        });

        Categories.Add(new MoviesCategoryItemViewModel
        {
            Title = "4K & High Definition",
            CategoryKey = "4K",
            IconGlyph = "\uE790",
            Command = new RelayCommand(() => SelectedCategory = "4K")
        });

        // 2. Genre Categories
        Categories.Add(new MoviesCategoryItemViewModel
        {
            Title = "Action & Adventure",
            CategoryKey = "Action",
            IconGlyph = "\uE735",
            Command = new RelayCommand(() => SelectedCategory = "Action")
        });

        Categories.Add(new MoviesCategoryItemViewModel
        {
            Title = "Comedy",
            CategoryKey = "Comedy",
            IconGlyph = "\uE76E",
            Command = new RelayCommand(() => SelectedCategory = "Comedy")
        });

        Categories.Add(new MoviesCategoryItemViewModel
        {
            Title = "Drama",
            CategoryKey = "Drama",
            IconGlyph = "\uE7C1",
            Command = new RelayCommand(() => SelectedCategory = "Drama")
        });

        Categories.Add(new MoviesCategoryItemViewModel
        {
            Title = "Kids & Family",
            CategoryKey = "Kids",
            IconGlyph = "\uE776",
            Command = new RelayCommand(() => SelectedCategory = "Kids")
        });

        Categories.Add(new MoviesCategoryItemViewModel
        {
            Title = "Sci-Fi & Fantasy",
            CategoryKey = "SciFi",
            IconGlyph = "\uE781",
            Command = new RelayCommand(() => SelectedCategory = "SciFi")
        });
    }

    private void UpdateCategorySelectionStates()
    {
        foreach (var category in Categories)
        {
            category.IsSelected = string.Equals(category.CategoryKey, SelectedCategory, StringComparison.OrdinalIgnoreCase);
        }
    }

    public async Task InitializeAsync()
    {
        await LoadMoviesAsync();
    }

    public async Task LoadMoviesAsync()
    {
        try
        {
            IsLoading = true;
            await _databaseService.InitializeAsync();

            var movies = await _movieRepository.GetAllAsync();
            var allFiles = await _mediaFileRepository.GetAllAsync();
            var filesByMovieId = allFiles
                .Where(f => f.MovieId.HasValue)
                .GroupBy(f => f.MovieId!.Value)
                .ToDictionary(g => g.Key, g => g.ToList());

            AllMovies.Clear();

            foreach (var movie in movies)
            {
                filesByMovieId.TryGetValue(movie.Id, out var files);

                // B-02: Exclude confirmed missing media from active library view
                if (files != null && files.Count > 0 && files.All(f => f.IsMissing))
                {
                    continue;
                }

                var primaryFile = files?.FirstOrDefault(f => !f.IsMissing) ?? files?.FirstOrDefault();

                string? qualityTag = null;
                string? resolutionDisplay = null;
                string? videoCodec = primaryFile?.VideoCodec;
                string? fileSizeDisplay = null;

                if (primaryFile != null)
                {
                    if (primaryFile.VideoHeight >= 2160 || primaryFile.VideoWidth >= 3840)
                    {
                        qualityTag = "4K HDR";
                        resolutionDisplay = "3840 x 2160 (4K UHD)";
                    }
                    else if (primaryFile.VideoHeight >= 1080 || primaryFile.VideoWidth >= 1920)
                    {
                        qualityTag = "1080p HD";
                        resolutionDisplay = "1920 x 1080 (Full HD)";
                    }
                    else if (primaryFile.VideoHeight >= 720 || primaryFile.VideoWidth >= 1280)
                    {
                        qualityTag = "720p HD";
                        resolutionDisplay = "1280 x 720 (HD)";
                    }

                    if (primaryFile.SizeBytes > 0)
                    {
                        double gb = primaryFile.SizeBytes / (1024.0 * 1024.0 * 1024.0);
                        fileSizeDisplay = $"{gb:F1} GB";
                    }
                }

                var posterVm = new MoviePosterItemViewModel
                {
                    Id = movie.Id,
                    Title = movie.Title,
                    ReleaseYear = movie.ReleaseYear,
                    Edition = movie.Edition,
                    PosterPath = movie.PosterPath,
                    BackdropPath = movie.BackdropPath,
                    QualityTag = qualityTag,
                    ResolutionDisplay = resolutionDisplay,
                    VideoCodec = videoCodec,
                    FilePath = primaryFile?.FilePath,
                    FileSizeDisplay = fileSizeDisplay,
                    CreatedAt = movie.CreatedAt,
                    SelectCommand = new RelayCommand<MoviePosterItemViewModel>(OnMovieSelected),
                    PlayCommand = new RelayCommand<MoviePosterItemViewModel>(_ => OnPlaySelectedMovie()),
                    DetailsCommand = new RelayCommand<MoviePosterItemViewModel>(_ => OnGoToMovieDetails())
                };

                AllMovies.Add(posterVm);
            }

            if (AllMovies.Count == 0)
            {
                IsLibraryEmpty = true;
                DisplayedMovies.Clear();
                UpdateCategoryCounts();
                OnPropertyChanged(nameof(TotalMovieCount));
                OnPropertyChanged(nameof(DisplayedMovieCount));
                OnPropertyChanged(nameof(MovieCountSummary));
                return;
            }

            IsLibraryEmpty = false;

            UpdateCategoryCounts();
            ApplyFiltersAndSorting();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load movies from SQLite repository.");
            IsLibraryEmpty = true;
            DisplayedMovies.Clear();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateCategoryCounts()
    {
        foreach (var category in Categories)
        {
            category.Count = category.CategoryKey switch
            {
                "All" => AllMovies.Count,
                "Recent" => AllMovies.Count,
                "4K" => AllMovies.Count(m => m.QualityTag != null && (m.QualityTag.Contains("4K") || m.QualityTag.Contains("1080"))),
                "Action" => AllMovies.Count(m => MatchesGenreKeyword(m.Title, "action", "adventure", "war", "battle")),
                "Comedy" => AllMovies.Count(m => MatchesGenreKeyword(m.Title, "comedy", "funny", "laugh")),
                "Drama" => AllMovies.Count(m => MatchesGenreKeyword(m.Title, "drama", "life", "story")),
                "Kids" => AllMovies.Count(m => MatchesGenreKeyword(m.Title, "kids", "family", "animated", "animation")),
                "SciFi" => AllMovies.Count(m => MatchesGenreKeyword(m.Title, "sci-fi", "star", "matrix", "dune", "space", "alien", "future")),
                _ => AllMovies.Count
            };
        }
    }

    private static bool MatchesGenreKeyword(string title, params string[] keywords)
    {
        if (string.IsNullOrWhiteSpace(title)) return false;
        foreach (var kw in keywords)
        {
            if (title.Contains(kw, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    public void ApplyFiltersAndSorting()
    {
        IEnumerable<MoviePosterItemViewModel> query = AllMovies;

        // 1. Category Filter
        query = SelectedCategory switch
        {
            "Recent" => query.OrderByDescending(m => m.CreatedAt),
            "4K" => query.Where(m => m.QualityTag != null && (m.QualityTag.Contains("4K") || m.QualityTag.Contains("1080"))),
            "Action" => query.Where(m => MatchesGenreKeyword(m.Title, "action", "adventure", "war", "battle")),
            "Comedy" => query.Where(m => MatchesGenreKeyword(m.Title, "comedy", "funny", "laugh")),
            "Drama" => query.Where(m => MatchesGenreKeyword(m.Title, "drama", "life", "story")),
            "Kids" => query.Where(m => MatchesGenreKeyword(m.Title, "kids", "family", "animated", "animation")),
            "SciFi" => query.Where(m => MatchesGenreKeyword(m.Title, "sci-fi", "star", "matrix", "dune", "space", "alien", "future")),
            _ => query
        };

        // 2. Search Filter
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            query = query.Where(m => m.Title.Contains(SearchQuery, StringComparison.OrdinalIgnoreCase));
        }

        // 3. Sorting
        query = SelectedSort switch
        {
            "Year" => query.OrderByDescending(m => m.ReleaseYear ?? 0).ThenBy(m => m.Title),
            "RecentlyAdded" => query.OrderByDescending(m => m.CreatedAt).ThenBy(m => m.Title),
            _ => query.OrderBy(m => m.Title)
        };

        DisplayedMovies.Clear();
        foreach (var item in query)
        {
            DisplayedMovies.Add(item);
        }

        OnPropertyChanged(nameof(TotalMovieCount));
        OnPropertyChanged(nameof(DisplayedMovieCount));
        OnPropertyChanged(nameof(MovieCountSummary));
    }

    private void OnMovieSelected(MoviePosterItemViewModel? movie)
    {
        if (movie == null) return;

        foreach (var m in DisplayedMovies)
        {
            m.IsSelected = m.Id == movie.Id;
        }

        SelectedMovie = movie;
        IsQuickActionOpen = true;
        StatusMessage = null;
    }

    private void OnPlaySelectedMovie()
    {
        if (SelectedMovie != null)
        {
            StatusMessage = $"Playback for '{SelectedMovie.Title}' will be available in Phase 4 (Hardware-Accelerated Player).";
        }
    }

    private void OnGoToMovieDetails()
    {
        if (SelectedMovie != null)
        {
            IsQuickActionOpen = false;
            NavigationRequested?.Invoke($"MovieDetails:{SelectedMovie.Id}");
        }
    }

    private void OnDismissQuickAction()
    {
        IsQuickActionOpen = false;
        if (SelectedMovie != null)
        {
            SelectedMovie.IsSelected = false;
            SelectedMovie = null;
        }
    }

    public async Task RefreshLibraryAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Scanning local roots and synchronizing library...";
            var result = await _importService.ImportAsync();
            await LoadMoviesAsync();
            StatusMessage = $"Library updated: {TotalMovieCount} movies displayed ({result.FilesImported} new, {result.FilesUpdated} updated).";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh library in MoviesViewModel.");
            StatusMessage = "Scan failed. Please verify your folder permissions.";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
