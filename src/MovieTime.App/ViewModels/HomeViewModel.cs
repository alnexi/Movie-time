using MovieTime.Core.Library.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
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
/// Main ViewModel for the Home experience.
/// Orchestrates dynamic rotating hero billboard showcase, horizontal content rows, floating acrylic sidebar,
/// silent hover micro-previews, genuine artwork pipeline synchronization, and local media playback.
/// </summary>
public sealed class HomeViewModel : ObservableObject, IDisposable
{
    private readonly IDatabaseService _databaseService;
    private readonly IMovieRepository _movieRepository;
    private readonly ISeriesRepository _seriesRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IEpisodeRepository _episodeRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IScanRootService _scanRootService;
    private readonly ILibraryImportService _importService;
    private readonly IArtworkPipelineService? _artworkPipelineService;
    private readonly IHoverPreviewService? _hoverPreviewService;
    private readonly IArtworkStorageService? _artworkStorageService;
    private readonly IDispatcherService? _dispatcher;
    private readonly ILogger<HomeViewModel> _logger;

    private readonly List<HeroBannerItemViewModel> _heroItems = new();
    private readonly System.Timers.Timer _heroRotationTimer;
    private int _currentHeroIndex;
    private bool _isRotationPaused;
    private bool _isDisposed;

    private HeroBannerViewModel _hero = new();
    private bool _isSidebarOpen;
    private bool _isLibraryEmpty = true;
    private bool _hasActualLibraryData;
    private string _searchText = string.Empty;
    private string _currentTimeText = DateTime.Now.ToString("h:mm tt");
    private string _activeCategory = "Home";
    private int _heroDotIndex = 0;

    public event Action<string>? NavigationRequested;
    public event Action<string, int?>? PlaybackRequested;
    public event Action<string, int?, int?>? MediaPlaybackRequested;

    private void RaisePlaybackRequested(string filePath, int? movieId, int? episodeId = null)
    {
        PlaybackRequested?.Invoke(filePath, movieId);
        MediaPlaybackRequested?.Invoke(filePath, movieId, episodeId);
    }

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

    public IReadOnlyList<HeroBannerItemViewModel> HeroItems => _heroItems;
    public int CurrentHeroIndex => _currentHeroIndex;
    public bool IsRotationPaused => _isRotationPaused;

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
        : this(
            databaseService,
            movieRepository,
            seriesRepository,
            seasonRepository,
            episodeRepository,
            mediaFileRepository,
            scanRootService,
            importService,
            null,
            null,
            null,
            null,
            logger)
    {
    }

    public HomeViewModel(
        IDatabaseService databaseService,
        IMovieRepository movieRepository,
        ISeriesRepository seriesRepository,
        ISeasonRepository seasonRepository,
        IEpisodeRepository episodeRepository,
        IMediaFileRepository mediaFileRepository,
        IScanRootService scanRootService,
        ILibraryImportService importService,
        IArtworkPipelineService? artworkPipelineService,
        IDispatcherService? dispatcher = null,
        ILogger<HomeViewModel>? logger = null)
        : this(
            databaseService,
            movieRepository,
            seriesRepository,
            seasonRepository,
            episodeRepository,
            mediaFileRepository,
            scanRootService,
            importService,
            artworkPipelineService,
            null,
            null,
            dispatcher,
            logger)
    {
    }

    public HomeViewModel(
        IDatabaseService databaseService,
        IMovieRepository movieRepository,
        ISeriesRepository seriesRepository,
        ISeasonRepository seasonRepository,
        IEpisodeRepository episodeRepository,
        IMediaFileRepository mediaFileRepository,
        IScanRootService scanRootService,
        ILibraryImportService importService,
        IArtworkPipelineService? artworkPipelineService,
        IHoverPreviewService? hoverPreviewService,
        IArtworkStorageService? artworkStorageService,
        IDispatcherService? dispatcher = null,
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
        _artworkPipelineService = artworkPipelineService;
        _hoverPreviewService = hoverPreviewService;
        _artworkStorageService = artworkStorageService;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<HomeViewModel>.Instance;

        if (_artworkPipelineService != null)
        {
            _artworkPipelineService.ArtworkUpdated += OnArtworkUpdated;
        }

        _importService.ImportCompleted += OnImportCompleted;

        // Initialize 7.5s rotation timer for hero billboard
        _heroRotationTimer = new System.Timers.Timer(7500) { AutoReset = true };
        _heroRotationTimer.Elapsed += OnHeroRotationTimerElapsed;

        ToggleSidebarCommand = new RelayCommand(() => IsSidebarOpen = !IsSidebarOpen);
        OpenSidebarCommand = new RelayCommand(() => IsSidebarOpen = true);
        CloseSidebarCommand = new RelayCommand(() => IsSidebarOpen = false);
        RefreshLibraryCommand = new AsyncRelayCommand(RefreshLibraryAsync);
        SelectCategoryCommand = new RelayCommand<string>(OnSelectCategory);
        ManageLibrariesCommand = new RelayCommand(() => OnSelectCategory("Settings"));

        InitializeSidebarItems();
        SetupEmptyLibraryState();
    }

    private void OnImportCompleted(object? sender, LibraryImportResult e)
    {
        RunOnUi(() =>
        {
            _ = InitializeAsync();
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
            // Update backdrop on hero if active or in items
            for (int i = 0; i < _heroItems.Count; i++)
            {
                if (_heroItems[i].MovieId == movieId)
                {
                    _heroItems[i].BackdropPath = backdropPath ?? posterPath;
                }
            }

            if (Hero.TargetMovieId == movieId)
            {
                Hero.BackdropPath = backdropPath ?? posterPath;
            }

            // Update matching cards in all content rows
            foreach (var row in Rows)
            {
                var card = row.Items.FirstOrDefault(c => c.IsMovie && (c.MovieId == movieId || c.Id == movieId));
                if (card != null)
                {
                    card.UpdateArtwork(posterPath, backdropPath);
                }
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
            Title = "Playlists",
            IconGlyph = "\uE8D6",
            Command = new RelayCommand(() => OnSelectCategory("Playlists"))
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
                await PopulateFromDatabaseAsync(movies, series, progressList);
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

    private async Task PopulateFromDatabaseAsync(
        List<Movie> movies,
        List<Series> series,
        List<PlaybackProgress> progressList)
    {
        _heroRotationTimer.Stop();
        Rows.Clear();
        _heroItems.Clear();

        // Query media files once to avoid N+1 queries
        var allFiles = await _mediaFileRepository.GetAllAsync();
        var filesByMovieId = allFiles
            .Where(f => f.MovieId.HasValue)
            .GroupBy(f => f.MovieId!.Value)
            .ToDictionary(g => g.Key, g => g.ToList());

        // 1. Dynamic Rotating Hero Billboard (up to 7 eligible real movies)
        var eligibleMovies = movies
            .Where(m => filesByMovieId.ContainsKey(m.Id) && filesByMovieId[m.Id].Any(f => !f.IsMissing))
            .Take(7)
            .ToList();

        if (eligibleMovies.Count == 0 && movies.Count > 0)
        {
            eligibleMovies = movies.Take(7).ToList();
        }

        foreach (var m in eligibleMovies)
        {
            filesByMovieId.TryGetValue(m.Id, out var mFiles);
            var primaryFile = mFiles?.FirstOrDefault(f => !f.IsMissing) ?? mFiles?.FirstOrDefault();

            string quality = "1080p";
            if (primaryFile != null)
            {
                if (primaryFile.VideoHeight >= 2160) quality = "4K";
                else if (primaryFile.VideoHeight >= 1080) quality = "1080p";
                else if (primaryFile.VideoHeight >= 720) quality = "720p";
            }

            _heroItems.Add(new HeroBannerItemViewModel
            {
                MovieId = m.Id,
                FilePath = primaryFile?.FilePath,
                Title = m.Title.ToUpperInvariant(),
                Genre = "Feature Film",
                RatingBadge = m.ReleaseYear?.ToString() ?? "HD",
                QualityBadge = quality,
                Description = string.IsNullOrWhiteSpace(m.Edition)
                    ? "Available in your local library."
                    : $"{m.Edition} • Local Media",
                BackdropPath = m.BackdropPath ?? m.PosterPath,
                ActionButtonText = "Play Movie"
            });
        }

        if (_heroItems.Count > 0)
        {
            _currentHeroIndex = 0;
            ApplyHeroItem(_heroItems[0], 0);

            Hero.SetupDots(_heroItems.Count, index => SwitchToHeroIndex(index));

            Hero.PlayCommand = new RelayCommand(() =>
            {
                if (!string.IsNullOrEmpty(Hero.TargetFilePath))
                {
                    RaisePlaybackRequested(Hero.TargetFilePath, Hero.TargetMovieId);
                }
                else if (Hero.TargetMovieId > 0)
                {
                    NavigationRequested?.Invoke($"MovieDetails:{Hero.TargetMovieId}");
                }
            });

            Hero.DetailsCommand = new RelayCommand(() =>
            {
                if (Hero.TargetMovieId > 0)
                {
                    NavigationRequested?.Invoke($"MovieDetails:{Hero.TargetMovieId}");
                }
            });

            Hero.PauseCommand = new RelayCommand(PauseHeroRotation);
            Hero.ResumeCommand = new RelayCommand(ResumeHeroRotation);
            Hero.NextCommand = new RelayCommand(NextHeroItem);
            Hero.PreviousCommand = new RelayCommand(PreviousHeroItem);

            if (_heroItems.Count > 1 && !_isRotationPaused)
            {
                _heroRotationTimer.Start();
            }
        }
        else
        {
            SetupEmptyLibraryState();
        }

        // 2. Continue Watching Row (genuine extracted frame artwork & progress)
        if (progressList.Count > 0)
        {
            var continueRow = new MediaRowViewModel { Title = "Continue Watching" };
            foreach (var prog in progressList.Take(10))
            {
                if (prog.MovieId.HasValue)
                {
                    var m = movies.FirstOrDefault(x => x.Id == prog.MovieId.Value);
                    if (m == null) continue;

                    filesByMovieId.TryGetValue(m.Id, out var mFiles);
                    if (mFiles != null && mFiles.Count > 0 && mFiles.All(f => f.IsMissing))
                    {
                        continue;
                    }

                    var primaryFile = mFiles?.FirstOrDefault(f => f.Id == prog.MediaFileId && !f.IsMissing) ??
                                      mFiles?.FirstOrDefault(f => !f.IsMissing) ??
                                      mFiles?.FirstOrDefault();

                    if (primaryFile == null) continue;

                    long posSec = (long)TimeSpan.FromTicks(prog.PositionTicks).TotalSeconds;

                    // Check for cached position thumbnail
                    string? cachedThumb = null;
                    if (_artworkStorageService != null)
                    {
                        var fileHash = _artworkStorageService.ComputeMediaHash(
                            primaryFile.FilePath,
                            primaryFile.SizeBytes,
                            primaryFile.FileModifiedAt);

                        if (_artworkStorageService.HasValidPositionThumbnail(fileHash, posSec, out var validPath))
                        {
                            cachedThumb = validPath;
                        }
                    }

                    var card = new MediaCardViewModel
                    {
                        Id = m.Id,
                        MovieId = m.Id,
                        MediaFileId = prog.MediaFileId,
                        FilePath = primaryFile.FilePath,
                        FileSizeBytes = primaryFile.SizeBytes,
                        FileModifiedUtc = primaryFile.FileModifiedAt,
                        PositionTicks = prog.PositionTicks,
                        DurationTicks = prog.DurationTicks,
                        Title = m.Title,
                        Subtitle = $"Continue \u2022 {Math.Round(prog.PositionPercentage * 100)}%",
                        ProgressPercentage = prog.PositionPercentage,
                        PositionThumbnailPath = cachedThumb,
                        BackdropPath = m.BackdropPath,
                        PosterPath = m.PosterPath,
                        QualityTag = (primaryFile.VideoHeight >= 2160) ? "4K" : "1080p",
                        IsMovie = true,
                        IsSample = false,
                        SelectCommand = new RelayCommand(() => NavigationRequested?.Invoke($"MovieDetails:{m.Id}")),
                        DetailsCommand = new RelayCommand(() => NavigationRequested?.Invoke($"MovieDetails:{m.Id}")),
                        PlayCommand = new RelayCommand(() => RaisePlaybackRequested(primaryFile.FilePath, m.Id, null))
                    };

                    // Asynchronously extract and cache position thumbnail if not yet cached
                    if (cachedThumb == null && _hoverPreviewService != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var extracted = await _hoverPreviewService.GetOrExtractPositionThumbnailAsync(
                                    primaryFile.FilePath,
                                    primaryFile.SizeBytes,
                                    primaryFile.FileModifiedAt,
                                    posSec);

                                if (!string.IsNullOrEmpty(extracted))
                                {
                                    void SetThumb() => card.PositionThumbnailPath = extracted;
                                    if (_dispatcher != null) _dispatcher.RunOnUi(SetThumb);
                                    else SetThumb();
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Background position thumbnail extraction skipped for {Path}", primaryFile.FilePath);
                            }
                        });
                    }

                    continueRow.Items.Add(card);
                }
                else if (prog.EpisodeId.HasValue)
                {
                    var ep = await _episodeRepository.GetByIdAsync(prog.EpisodeId.Value);
                    if (ep == null) continue;

                    var s = series.FirstOrDefault(x => x.Id == ep.SeriesId) ?? await _seriesRepository.GetByIdAsync(ep.SeriesId);
                    var season = await _seasonRepository.GetByIdAsync(ep.SeasonId);
                    var epFiles = allFiles.Where(f => f.EpisodeId == ep.Id).ToList();
                    if (epFiles.Count > 0 && epFiles.All(f => f.IsMissing))
                    {
                        continue;
                    }

                    var primaryFile = epFiles.FirstOrDefault(f => f.Id == prog.MediaFileId && !f.IsMissing) ??
                                      epFiles.FirstOrDefault(f => !f.IsMissing) ??
                                      epFiles.FirstOrDefault();

                    if (primaryFile == null) continue;

                    long posSec = (long)TimeSpan.FromTicks(prog.PositionTicks).TotalSeconds;

                    string? cachedThumb = null;
                    if (_artworkStorageService != null)
                    {
                        var fileHash = _artworkStorageService.ComputeMediaHash(
                            primaryFile.FilePath,
                            primaryFile.SizeBytes,
                            primaryFile.FileModifiedAt);

                        if (_artworkStorageService.HasValidPositionThumbnail(fileHash, posSec, out var validPath))
                        {
                            cachedThumb = validPath;
                        }
                    }

                    var sNum = season?.SeasonNumber ?? 1;
                    var epTitle = string.IsNullOrWhiteSpace(ep.Title) ? $"Episode {ep.EpisodeNumber}" : ep.Title;
                    var showTitle = s?.Title ?? "TV Show";

                    var card = new MediaCardViewModel
                    {
                        Id = ep.Id,
                        EpisodeId = ep.Id,
                        MediaFileId = prog.MediaFileId,
                        FilePath = primaryFile.FilePath,
                        FileSizeBytes = primaryFile.SizeBytes,
                        FileModifiedUtc = primaryFile.FileModifiedAt,
                        PositionTicks = prog.PositionTicks,
                        DurationTicks = prog.DurationTicks,
                        Title = showTitle,
                        Subtitle = $"S{sNum}E{ep.EpisodeNumber} \u2022 {epTitle} ({Math.Round(prog.PositionPercentage * 100)}%)",
                        ProgressPercentage = prog.PositionPercentage,
                        PositionThumbnailPath = cachedThumb,
                        BackdropPath = s?.BackdropPath ?? ep.ThumbnailPath,
                        PosterPath = ep.ThumbnailPath ?? s?.PosterPath,
                        QualityTag = (primaryFile.VideoHeight >= 2160) ? "4K" : "1080p",
                        IsEpisode = true,
                        IsSample = false,
                        SelectCommand = new RelayCommand(() => NavigationRequested?.Invoke($"SeriesDetails:{s?.Id}")),
                        DetailsCommand = new RelayCommand(() => NavigationRequested?.Invoke($"SeriesDetails:{s?.Id}")),
                        PlayCommand = new RelayCommand(() => RaisePlaybackRequested(primaryFile.FilePath, null, ep.Id))
                    };

                    if (cachedThumb == null && _hoverPreviewService != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var extracted = await _hoverPreviewService.GetOrExtractPositionThumbnailAsync(
                                    primaryFile.FilePath,
                                    primaryFile.SizeBytes,
                                    primaryFile.FileModifiedAt,
                                    posSec);

                                if (!string.IsNullOrEmpty(extracted))
                                {
                                    void SetThumb() => card.PositionThumbnailPath = extracted;
                                    if (_dispatcher != null) _dispatcher.RunOnUi(SetThumb);
                                    else SetThumb();
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogDebug(ex, "Background position thumbnail extraction skipped for {Path}", primaryFile.FilePath);
                            }
                        });
                    }

                    continueRow.Items.Add(card);
                }
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
                filesByMovieId.TryGetValue(movie.Id, out var mFiles);
                if (mFiles != null && mFiles.Count > 0 && mFiles.All(f => f.IsMissing))
                {
                    continue;
                }

                var primaryFile = mFiles?.FirstOrDefault(f => !f.IsMissing) ?? mFiles?.FirstOrDefault();
                if (mFiles != null && mFiles.Count > 0 && primaryFile == null) continue;

                var card = new MediaCardViewModel
                {
                    Id = movie.Id,
                    MovieId = movie.Id,
                    MediaFileId = primaryFile?.Id,
                    FilePath = primaryFile?.FilePath,
                    FileSizeBytes = primaryFile?.SizeBytes ?? 0,
                    FileModifiedUtc = primaryFile?.FileModifiedAt,
                    Title = movie.Title,
                    Subtitle = movie.ReleaseYear?.ToString() ?? "Movie",
                    PosterPath = movie.PosterPath,
                    BackdropPath = movie.BackdropPath,
                    QualityTag = primaryFile != null && primaryFile.VideoHeight >= 2160 ? "4K" : "1080p",
                    IsMovie = true,
                    IsSample = false,
                    SelectCommand = new RelayCommand(() => NavigationRequested?.Invoke($"MovieDetails:{movie.Id}")),
                    DetailsCommand = new RelayCommand(() => NavigationRequested?.Invoke($"MovieDetails:{movie.Id}")),
                    PlayCommand = primaryFile != null
                        ? new RelayCommand(() => RaisePlaybackRequested(primaryFile.FilePath, movie.Id, null))
                        : null
                };

                moviesRow.Items.Add(card);
            }
            Rows.Add(moviesRow);
        }

        // 4. TV Series Row
        if (series.Count > 0)
        {
            var tvRow = new MediaRowViewModel { Title = "TV Shows" };
            foreach (var show in series)
            {
                var card = new MediaCardViewModel
                {
                    Id = show.Id,
                    Title = show.Title,
                    Subtitle = show.StartYear.HasValue ? $"{show.StartYear.Value} \u2022 TV Series" : "TV Series",
                    PosterPath = show.PosterPath,
                    BackdropPath = show.BackdropPath,
                    IsEpisode = true,
                    IsSample = false,
                    SelectCommand = new RelayCommand(() => NavigationRequested?.Invoke($"SeriesDetails:{show.Id}")),
                    DetailsCommand = new RelayCommand(() => NavigationRequested?.Invoke($"SeriesDetails:{show.Id}"))
                };

                tvRow.Items.Add(card);
            }
            Rows.Add(tvRow);
        }
    }

    private void ApplyHeroItem(HeroBannerItemViewModel item, int index)
    {
        Hero.Title = item.Title;
        Hero.Genre = item.Genre;
        Hero.RatingBadge = item.RatingBadge;
        Hero.QualityBadge = item.QualityBadge;
        Hero.Description = item.Description;
        Hero.BackdropPath = item.BackdropPath;
        Hero.TargetMovieId = item.MovieId;
        Hero.TargetFilePath = item.FilePath;
        Hero.ActionButtonText = item.ActionButtonText;
        Hero.ActiveIndex = index;
        HeroDotIndex = index;
    }

    public void SwitchToHeroIndex(int index)
    {
        if (_heroItems.Count == 0) return;

        _currentHeroIndex = Math.Clamp(index, 0, _heroItems.Count - 1);
        ApplyHeroItem(_heroItems[_currentHeroIndex], _currentHeroIndex);
    }

    public void NextHeroItem()
    {
        if (_heroItems.Count <= 1) return;

        var nextIndex = (_currentHeroIndex + 1) % _heroItems.Count;
        SwitchToHeroIndex(nextIndex);
    }

    public void PreviousHeroItem()
    {
        if (_heroItems.Count <= 1) return;

        var prevIndex = (_currentHeroIndex - 1 + _heroItems.Count) % _heroItems.Count;
        SwitchToHeroIndex(prevIndex);
    }

    public void PauseHeroRotation()
    {
        _isRotationPaused = true;
        _heroRotationTimer.Stop();
    }

    public void ResumeHeroRotation()
    {
        _isRotationPaused = false;
        if (_heroItems.Count > 1)
        {
            _heroRotationTimer.Stop();
            _heroRotationTimer.Start();
        }
    }

    private void OnHeroRotationTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        if (_isRotationPaused || _heroItems.Count <= 1) return;

        void Advance()
        {
            if (_isRotationPaused || _heroItems.Count <= 1) return;
            NextHeroItem();
        }

        if (_dispatcher != null)
        {
            _dispatcher.RunOnUi(Advance);
        }
        else
        {
            Advance();
        }
    }

    public void SetupEmptyLibraryState()
    {
        _heroRotationTimer.Stop();
        Rows.Clear();
        _heroItems.Clear();

        Hero = new HeroBannerViewModel
        {
            Title = "RIVER",
            Genre = "Local Media Library",
            RatingBadge = "Offline",
            QualityBadge = "Ready",
            Description = "Connect your storage drives and add an MLibrary folder in Settings to start cataloging and enjoying your local media collection.",
            ActionButtonText = "Manage Libraries",
            ActionCommand = ManageLibrariesCommand,
            PlayCommand = ManageLibrariesCommand,
            DetailsCommand = ManageLibrariesCommand,
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

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _heroRotationTimer.Stop();
        _heroRotationTimer.Dispose();

        if (_artworkPipelineService != null)
        {
            _artworkPipelineService.ArtworkUpdated -= OnArtworkUpdated;
        }
    }
}
