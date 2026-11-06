using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.Common;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Library;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Main ViewModel for the Apple TV-inspired Home experience.
/// Orchestrates hero billboard showcase, horizontal content rows, floating acrylic sidebar,
/// and bidirectional synchronization with the local SQLite media library.
/// </summary>
public sealed class HomeViewModel : ObservableObject
{
    private readonly IDatabaseService _databaseService;
    private readonly IMovieRepository _movieRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IEpisodeRepository _episodeRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IScanRootService _scanRootService;
    private readonly ILibraryImportService _importService;
    private readonly ILogger<HomeViewModel> _logger;

    private HeroBannerViewModel _hero = new();
    private bool _isSidebarOpen;
    private bool _isLibraryEmpty = true;
    private bool _hasActualLibraryData;
    private string _searchText = string.Empty;
    private string _currentTimeText = DateTime.Now.ToString("h:mm tt");
    private string _activeCategory = "Home";
    private int _heroDotIndex = 0;

    public event Action<string>? NavigationRequested;

    public HeroBannerViewModel Hero
    {
        get => _hero;
        set => SetProperty(ref _hero, value);
    }

    public int HeroDotIndex
    {
        get => _heroDotIndex;
        set => SetProperty(ref _heroDotIndex, value);
    }

    public bool IsSidebarOpen
    {
        get => _isSidebarOpen;
        set => SetProperty(ref _isSidebarOpen, value);
    }

    public bool IsLibraryEmpty
    {
        get => _isLibraryEmpty;
        set => SetProperty(ref _isLibraryEmpty, value);
    }

    public bool HasActualLibraryData
    {
        get => _hasActualLibraryData;
        set => SetProperty(ref _hasActualLibraryData, value);
    }

    public string SearchText
    {
        get => _searchText;
        set => SetProperty(ref _searchText, value);
    }

    public string CurrentTimeText
    {
        get => _currentTimeText;
        set => SetProperty(ref _currentTimeText, value);
    }

    public string ActiveCategory
    {
        get => _activeCategory;
        set => SetProperty(ref _activeCategory, value);
    }

    public ObservableCollection<MediaRowViewModel> Rows { get; } = new();
    public ObservableCollection<SidebarItemViewModel> SidebarItems { get; } = new();

    public ICommand ToggleSidebarCommand { get; }
    public ICommand OpenSidebarCommand { get; }
    public ICommand CloseSidebarCommand { get; }
    public ICommand RefreshLibraryCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand ManageLibrariesCommand { get; }

    public HomeViewModel(
        IDatabaseService databaseService,
        IMovieRepository movieRepository,
        ISeriesRepository seriesRepository,
        ISeasonRepository seasonRepository,
        IEpisodeRepository episodeRepository,
        IMediaFileRepository mediaFileRepository,
        IScanRootService scanRootService,
        ILibraryImportService importService,
        ILogger<HomeViewModel>? logger = null)
    {
        _databaseService = databaseService ?? throw new ArgumentNullException(nameof(databaseService));
        _movieRepository = movieRepository ?? throw new ArgumentNullException(nameof(movieRepository));
        _seriesRepository = seriesRepository ?? throw new ArgumentNullException(nameof(seriesRepository));
        _seasonRepository = seasonRepository ?? throw new ArgumentNullException(nameof(seasonRepository));
        _episodeRepository = episodeRepository ?? throw new ArgumentNullException(nameof(episodeRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _scanRootService = scanRootService ?? throw new ArgumentNullException(nameof(scanRootService));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _logger = logger ?? NullLogger<HomeViewModel>.Instance;

        ToggleSidebarCommand = new RelayCommand(() => IsSidebarOpen = !IsSidebarOpen);
        OpenSidebarCommand = new RelayCommand(() => IsSidebarOpen = true);
        CloseSidebarCommand = new RelayCommand(() => IsSidebarOpen = false);
        RefreshLibraryCommand = new AsyncRelayCommand(RefreshLibraryAsync);
        SelectCategoryCommand = new RelayCommand<string>(OnSelectCategory);
        ManageLibrariesCommand = new RelayCommand(() => OnSelectCategory("Settings"));

        InitializeSidebarItems();
        SetupEmptyLibraryState();
    }

    private void InitializeSidebarItems()
    {
        SidebarItems.Clear();

        SidebarItems.Add(new SidebarItemViewModel
        {
            Title = "Search",
            IconGlyph = "\uE721",
            Command = new RelayCommand(() => OnSelectCategory("Search"))
        });

        SidebarItems.Add(new SidebarItemViewModel
        {
            Title = "Home",
            IconGlyph = "\uE80F",
            IsSelected = true,
            Command = new RelayCommand(() => OnSelectCategory("Home"))
        });

        SidebarItems.Add(new SidebarItemViewModel
        {
            Title = "Movies",
            IconGlyph = "\uE714",
            Command = new RelayCommand(() => OnSelectCategory("Movies"))
        });

        SidebarItems.Add(new SidebarItemViewModel
        {
            Title = "TV Shows",
            IconGlyph = "\uE7F4",
            Command = new RelayCommand(() => OnSelectCategory("TV Shows"))
        });

        SidebarItems.Add(new SidebarItemViewModel
        {
            Title = "Library",
            IconGlyph = "\uE838",
            Command = new RelayCommand(() => OnSelectCategory("Library"))
        });

        SidebarItems.Add(new SidebarItemViewModel
        {
            Title = "Settings",
            IconGlyph = "\uE713",
            Command = new RelayCommand(() => OnSelectCategory("Settings"))
        });
    }

    public void OnSelectCategory(string? category)
    {
        if (string.IsNullOrWhiteSpace(category)) return;

        ActiveCategory = category;
        foreach (var item in SidebarItems)
        {
            item.IsSelected = string.Equals(item.Title, category, StringComparison.OrdinalIgnoreCase);
        }

        // Close sidebar on item selection
        IsSidebarOpen = false;

        NavigationRequested?.Invoke(category);
    }

    public async Task InitializeAsync()
    {
        CurrentTimeText = DateTime.Now.ToString("h:mm tt");
        await LoadLibraryDataAsync();
    }

    public async Task LoadLibraryDataAsync()
    {
        try
        {
            await _databaseService.InitializeAsync();

            var movies = await _movieRepository.GetAllAsync();
            var series = await _seriesRepository.GetAllAsync();
            var progressList = await _databaseService.Table<PlaybackProgress>()
                .OrderByDescending(p => p.LastWatchedAt)
                .ToListAsync();

            if (movies.Count > 0 || series.Count > 0)
            {
                HasActualLibraryData = true;
                IsLibraryEmpty = false;
                PopulateFromDatabase(movies, series, progressList);
            }
            else
            {
                HasActualLibraryData = false;
                IsLibraryEmpty = true;
                SetupEmptyLibraryState();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load library data for HomeViewModel.");
            SetupEmptyLibraryState();
        }
    }

    private void PopulateFromDatabase(
        System.Collections.Generic.List<Movie> movies,
        System.Collections.Generic.List<Series> series,
        System.Collections.Generic.List<PlaybackProgress> progressList)
    {
        Rows.Clear();

        // 1. Hero billboard from primary movie
        var featuredMovie = movies.FirstOrDefault();
        if (featuredMovie != null)
        {
            Hero = new HeroBannerViewModel
            {
                Title = featuredMovie.Title.ToUpperInvariant(),
                Genre = "Feature Film",
                RatingBadge = featuredMovie.ReleaseYear?.ToString() ?? "HD",
                QualityBadge = "1080p",
                Description = string.IsNullOrWhiteSpace(featuredMovie.Edition)
                    ? "Available in your local library."
                    : $"{featuredMovie.Edition} • Local Media",
                BackdropPath = featuredMovie.BackdropPath ?? featuredMovie.PosterPath,
                ActionButtonText = "Play Movie",
                TargetMovieId = featuredMovie.Id,
                ActionCommand = new RelayCommand(() => NavigationRequested?.Invoke($"MovieDetails:{featuredMovie.Id}")),
                IsSample = false
            };
        }

        // 2. Continue Watching Row (if any items have playback progress)
        if (progressList.Count > 0)
        {
            var continueRow = new MediaRowViewModel { Title = "Continue Watching" };
            foreach (var prog in progressList.Take(10))
            {
                string title = "Media Item";
                string subtitle = $"Continue • {Math.Round(prog.PositionPercentage * 100)}%";

                if (prog.MovieId.HasValue)
                {
                    var m = movies.FirstOrDefault(x => x.Id == prog.MovieId.Value);
                    if (m != null) title = m.Title;
                }

                continueRow.Items.Add(new MediaCardViewModel
                {
                    Id = prog.Id,
                    Title = title,
                    Subtitle = subtitle,
                    ProgressPercentage = prog.PositionPercentage,
                    IsSample = false
                });
            }

            if (continueRow.Items.Count > 0)
            {
                Rows.Add(continueRow);
            }
        }

        // 3. Movies Row
        if (movies.Count > 0)
        {
            var moviesRow = new MediaRowViewModel { Title = "Movies" };
            foreach (var movie in movies)
            {
                moviesRow.Items.Add(new MediaCardViewModel
                {
                    Id = movie.Id,
                    Title = movie.Title,
                    Subtitle = movie.ReleaseYear?.ToString() ?? "Movie",
                    PosterPath = movie.PosterPath,
                    BackdropPath = movie.BackdropPath,
                    IsMovie = true,
                    IsSample = false
                });
            }
            Rows.Add(moviesRow);
        }

        // 4. TV Series Row
        if (series.Count > 0)
        {
            var tvRow = new MediaRowViewModel { Title = "TV Shows" };
            foreach (var show in series)
            {
                tvRow.Items.Add(new MediaCardViewModel
                {
                    Id = show.Id,
                    Title = show.Title,
                    Subtitle = "TV Series",
                    PosterPath = show.PosterPath,
                    BackdropPath = show.BackdropPath,
                    IsEpisode = true,
                    IsSample = false
                });
            }
            Rows.Add(tvRow);
        }
    }

    public void SetupEmptyLibraryState()
    {
        Rows.Clear();

        Hero = new HeroBannerViewModel
        {
            Title = "MOVIE TIME",
            Genre = "Local Media Library",
            RatingBadge = "Offline",
            QualityBadge = "Ready",
            Description = "Connect your storage drives and add an MLibrary folder in Settings to start cataloging and enjoying your local media collection.",
            ActionButtonText = "Manage Libraries",
            ActionCommand = ManageLibrariesCommand,
            IsSample = false
        };

        HasActualLibraryData = false;
        IsLibraryEmpty = true;
    }

    private async Task RefreshLibraryAsync()
    {
        try
        {
            await _importService.ImportAsync();
            await LoadLibraryDataAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh library.");
        }
    }
}
