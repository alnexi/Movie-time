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

        InitializeSidebarItems();
        InitializeSampleShowcase();
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
                InitializeSampleShowcase();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load library data for HomeViewModel.");
            InitializeSampleShowcase();
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

    public void InitializeSampleShowcase()
    {
        Rows.Clear();

        // 1. Hero Billboard: Killers of the Flower Moon (matching Reference Screenshot 1 & 2)
        Hero = new HeroBannerViewModel
        {
            Title = "KILLERS OF THE FLOWER MOON",
            Genre = "Crime",
            RatingBadge = "R",
            QualityBadge = "4K HDR",
            Description = "Leonardo DiCaprio. Robert De Niro. Lily Gladstone. A film by Martin Scorsese.",
            ActionButtonText = "Go to Movie",
            IsSample = true
        };

        // 2. Continue Watching Row (Reference Screenshot 1 & 2)
        var continueRow = new MediaRowViewModel { Title = "Continue Watching" };
        continueRow.Items.Add(new MediaCardViewModel
        {
            Title = "Slow Horses",
            Subtitle = "Recently Added",
            QualityTag = "tv+",
            IsSample = true
        });
        continueRow.Items.Add(new MediaCardViewModel
        {
            Title = "Snowpiercer",
            Subtitle = "Recently Added",
            QualityTag = "STARZ",
            IsSample = true
        });
        continueRow.Items.Add(new MediaCardViewModel
        {
            Title = "Dark Matter",
            Subtitle = "Continue • S1, E7",
            ProgressPercentage = 0.65,
            IsSample = true
        });
        continueRow.Items.Add(new MediaCardViewModel
        {
            Title = "The Walking Dead: Daryl Dixon",
            Subtitle = "Recently Added",
            QualityTag = "STARZ ON",
            IsSample = true
        });
        continueRow.Items.Add(new MediaCardViewModel
        {
            Title = "The Penguin",
            Subtitle = "Recently Added",
            QualityTag = "HBO",
            IsSample = true
        });
        Rows.Add(continueRow);

        // 3. Top Movies Row (Reference Screenshot 3)
        var topMoviesRow = new MediaRowViewModel { Title = "Top Movies" };
        topMoviesRow.Items.Add(new MediaCardViewModel
        {
            Title = "Pokémon Detective Pikachu",
            Subtitle = "4K HDR • Action",
            QualityTag = "4K HDR",
            IsSample = true
        });
        topMoviesRow.Items.Add(new MediaCardViewModel
        {
            Title = "Spider-Man: Into the Spider-Verse",
            Subtitle = "Animation • Action",
            IsSample = true
        });
        topMoviesRow.Items.Add(new MediaCardViewModel
        {
            Title = "VICE",
            Subtitle = "Drama • Biography",
            IsSample = true
        });
        topMoviesRow.Items.Add(new MediaCardViewModel
        {
            Title = "Ocean's 8",
            Subtitle = "Comedy • Crime",
            IsSample = true
        });
        topMoviesRow.Items.Add(new MediaCardViewModel
        {
            Title = "Dune: Part Two",
            Subtitle = "Recently Added",
            IsSample = true
        });
        Rows.Add(topMoviesRow);

        // 4. Weekly Spotlight Row
        var spotlightRow = new MediaRowViewModel { Title = "Weekly Spotlight" };
        spotlightRow.Items.Add(new MediaCardViewModel
        {
            Title = "Hijack",
            Subtitle = "Continue • S1, E1",
            ProgressPercentage = 0.35,
            IsSample = true
        });
        spotlightRow.Items.Add(new MediaCardViewModel
        {
            Title = "Severance",
            Subtitle = "Sci-Fi • Thriller",
            IsSample = true
        });
        spotlightRow.Items.Add(new MediaCardViewModel
        {
            Title = "Ted Lasso",
            Subtitle = "Comedy • Sport",
            IsSample = true
        });
        spotlightRow.Items.Add(new MediaCardViewModel
        {
            Title = "The Morning Show",
            Subtitle = "Drama",
            IsSample = true
        });
        Rows.Add(spotlightRow);
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
