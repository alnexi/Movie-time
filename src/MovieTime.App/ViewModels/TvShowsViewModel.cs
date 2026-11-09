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
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Entities;
using MovieTime.Core.Library;

namespace MovieTime.App.ViewModels;

/// <summary>
/// ViewModel managing the Apple TV-inspired TV Shows library catalog,
/// including SQLite-backed series catalog, search filtering, sorting, empty states, and focus states.
/// </summary>
public sealed class TvShowsViewModel : ObservableObject
{
    private readonly ISeriesRepository _seriesRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IEpisodeRepository _episodeRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly ILibraryImportService _importService;
    private readonly IArtworkPipelineService? _artworkPipelineService;
    private readonly IDispatcherService? _dispatcher;
    private readonly ILogger<TvShowsViewModel> _logger;

    private readonly List<SeriesCardViewModel> _allSeries = new();
    private bool _isLoading;
    private bool _isLibraryEmpty = true;
    private string _searchQuery = string.Empty;
    private string _selectedSort = "Title"; // "Title", "Year", "RecentlyAdded"
    private string? _statusMessage;

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
        set => SetProperty(ref _statusMessage, value);
    }

    public int TotalSeriesCount => SeriesList.Count;

    public ObservableCollection<SeriesCardViewModel> SeriesList { get; } = new();

    public ICommand NavigateHomeCommand { get; }
    public ICommand SetSortCommand { get; }
    public ICommand RefreshLibraryCommand { get; }
    public ICommand SelectSeriesCommand { get; }

    public TvShowsViewModel(
        ISeriesRepository seriesRepository,
        ISeasonRepository seasonRepository,
        IEpisodeRepository episodeRepository,
        IMediaFileRepository mediaFileRepository,
        ILibraryImportService importService,
        IArtworkPipelineService? artworkPipelineService = null,
        IDispatcherService? dispatcher = null,
        ILogger<TvShowsViewModel>? logger = null)
    {
        _seriesRepository = seriesRepository ?? throw new ArgumentNullException(nameof(seriesRepository));
        _seasonRepository = seasonRepository ?? throw new ArgumentNullException(nameof(seasonRepository));
        _episodeRepository = episodeRepository ?? throw new ArgumentNullException(nameof(episodeRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _importService = importService ?? throw new ArgumentNullException(nameof(importService));
        _artworkPipelineService = artworkPipelineService;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<TvShowsViewModel>.Instance;

        NavigateHomeCommand = new RelayCommand(() => NavigationRequested?.Invoke("Home"));
        SetSortCommand = new RelayCommand<string>(sort =>
        {
            if (!string.IsNullOrWhiteSpace(sort))
            {
                SelectedSort = sort;
            }
        });
        RefreshLibraryCommand = new RelayCommand(async () => await RefreshLibraryAsync());
        SelectSeriesCommand = new RelayCommand<SeriesCardViewModel>(series =>
        {
            if (series != null)
            {
                NavigationRequested?.Invoke($"SeriesDetails:{series.Id}");
            }
        });

        if (_artworkPipelineService != null)
        {
            _artworkPipelineService.SeriesArtworkUpdated += OnSeriesArtworkUpdated;
        }
    }

    public async Task InitializeAsync()
    {
        await LoadSeriesAsync();
    }

    public async Task LoadSeriesAsync()
    {
        try
        {
            IsLoading = true;
            _allSeries.Clear();

            var seriesEntities = await _seriesRepository.GetAllAsync();
            var allFiles = await _mediaFileRepository.GetAllAsync();

            foreach (var series in seriesEntities)
            {
                var seasons = await _seasonRepository.GetBySeriesIdAsync(series.Id);
                var episodes = await _episodeRepository.GetBySeriesIdAsync(series.Id);
                var seriesEpIds = episodes.Select(e => e.Id).ToHashSet();
                var seriesFiles = allFiles.Where(f => f.EpisodeId.HasValue && seriesEpIds.Contains(f.EpisodeId.Value)).ToList();

                // B-02: Do not display series in active library if all episode files are confirmed missing
                if (seriesFiles.Count > 0 && seriesFiles.All(f => f.IsMissing))
                {
                    continue;
                }

                int epCount = seriesFiles.Count > 0
                    ? episodes.Count(e => seriesFiles.Any(f => f.EpisodeId == e.Id && !f.IsMissing))
                    : episodes.Count;

                var card = new SeriesCardViewModel
                {
                    Id = series.Id,
                    Title = series.Title,
                    StartYear = series.StartYear,
                    EndYear = series.EndYear,
                    SeasonCount = seasons.Count,
                    EpisodeCount = epCount,
                    PosterPath = series.PosterPath,
                    BackdropPath = series.BackdropPath,
                    SelectCommand = new RelayCommand(() => NavigationRequested?.Invoke($"SeriesDetails:{series.Id}"))
                };

                _allSeries.Add(card);
            }

            IsLibraryEmpty = _allSeries.Count == 0;
            ApplyFiltersAndSorting();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load TV shows catalog.");
            StatusMessage = "Failed to load TV shows from library.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFiltersAndSorting()
    {
        IEnumerable<SeriesCardViewModel> query = _allSeries;

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var search = SearchQuery.Trim();
            query = query.Where(s => s.Title.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        query = SelectedSort switch
        {
            "Year" => query.OrderByDescending(s => s.StartYear ?? 0).ThenBy(s => s.Title),
            "RecentlyAdded" => query.OrderByDescending(s => s.Id),
            _ => query.OrderBy(s => s.Title, StringComparer.OrdinalIgnoreCase)
        };

        SeriesList.Clear();
        foreach (var item in query)
        {
            SeriesList.Add(item);
        }

        OnPropertyChanged(nameof(TotalSeriesCount));
    }

    public async Task RefreshLibraryAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Scanning libraries for TV episodes...";
            await _importService.ImportAsync();
            await LoadSeriesAsync();
            StatusMessage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Library scan failed.");
            StatusMessage = "Failed to scan library.";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void OnSeriesArtworkUpdated(int seriesId, string? posterPath, string? backdropPath)
    {
        void Update()
        {
            var card = _allSeries.FirstOrDefault(s => s.Id == seriesId);
            if (card != null)
            {
                card.UpdateArtwork(posterPath, backdropPath);
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
}
