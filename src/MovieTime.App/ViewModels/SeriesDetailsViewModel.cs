using System;
using System.Collections.Generic;
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

namespace MovieTime.App.ViewModels;

/// <summary>
/// ViewModel managing the cinematic Series Details experience.
/// Presents local series metadata, genuine extracted backdrop/poster artwork,
/// season tabs, episode lists, saved playback progress, and next up resume actions.
/// </summary>
public sealed class SeriesDetailsViewModel : ObservableObject
{
    private readonly ISeriesRepository _seriesRepository;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IEpisodeRepository _episodeRepository;
    private readonly IMediaFileRepository _mediaFileRepository;
    private readonly IPlaybackProgressRepository _progressRepository;
    private readonly IArtworkPipelineService? _artworkPipelineService;
    private readonly IDispatcherService? _dispatcher;
    private readonly ILogger<SeriesDetailsViewModel> _logger;

    private int _seriesId;
    private string _title = string.Empty;
    private int? _startYear;
    private int? _endYear;
    private string? _posterPath;
    private string? _backdropPath;
    private bool _isLoading;
    private SeasonTabViewModel? _selectedSeason;
    private EpisodeItemViewModel? _nextUpEpisode;
    private string _nextUpActionText = "Play S1 E1";

    public event Action<string>? NavigationRequested;
    public event Action<string, int>? PlaybackRequested;

    public int SeriesId
    {
        get => _seriesId;
        private set
        {
            if (SetProperty(ref _seriesId, value))
            {
                OnPropertyChanged(nameof(IsSeriesFound));
            }
        }
    }

    public bool IsSeriesFound => SeriesId > 0;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public int? StartYear
    {
        get => _startYear;
        set
        {
            if (SetProperty(ref _startYear, value))
            {
                OnPropertyChanged(nameof(YearDisplay));
                OnPropertyChanged(nameof(MetadataSubtitle));
            }
        }
    }

    public int? EndYear
    {
        get => _endYear;
        set
        {
            if (SetProperty(ref _endYear, value))
            {
                OnPropertyChanged(nameof(YearDisplay));
                OnPropertyChanged(nameof(MetadataSubtitle));
            }
        }
    }

    public string YearDisplay
    {
        get
        {
            if (StartYear.HasValue && StartYear.Value > 0)
            {
                if (EndYear.HasValue && EndYear.Value > StartYear.Value)
                {
                    return $"{StartYear.Value}\u2013{EndYear.Value}";
                }
                return StartYear.Value.ToString();
            }
            return string.Empty;
        }
    }

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

    public int TotalSeasonsCount => SeasonTabs.Count;
    private int _totalEpisodesCount;
    public int TotalEpisodesCount
    {
        get => _totalEpisodesCount;
        private set
        {
            if (SetProperty(ref _totalEpisodesCount, value))
            {
                OnPropertyChanged(nameof(MetadataSubtitle));
            }
        }
    }

    public string MetadataSubtitle
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(YearDisplay))
            {
                parts.Add(YearDisplay);
            }
            if (TotalSeasonsCount > 0)
            {
                parts.Add(TotalSeasonsCount == 1 ? "1 Season" : $"{TotalSeasonsCount} Seasons");
            }
            if (TotalEpisodesCount > 0)
            {
                parts.Add(TotalEpisodesCount == 1 ? "1 Episode" : $"{TotalEpisodesCount} Episodes");
            }
            return parts.Count > 0 ? string.Join(" \u2022 ", parts) : "TV Series";
        }
    }

    public SeasonTabViewModel? SelectedSeason
    {
        get => _selectedSeason;
        set
        {
            if (SetProperty(ref _selectedSeason, value))
            {
                foreach (var tab in SeasonTabs)
                {
                    tab.IsSelected = tab == _selectedSeason;
                }
                _ = LoadEpisodesForSeasonAsync(_selectedSeason?.SeasonId ?? 0);
            }
        }
    }

    public EpisodeItemViewModel? NextUpEpisode
    {
        get => _nextUpEpisode;
        private set
        {
            if (SetProperty(ref _nextUpEpisode, value))
            {
                OnPropertyChanged(nameof(HasNextUpEpisode));
            }
        }
    }

    public bool HasNextUpEpisode => NextUpEpisode != null && !string.IsNullOrWhiteSpace(NextUpEpisode.FilePath);

    public string NextUpActionText
    {
        get => _nextUpActionText;
        private set => SetProperty(ref _nextUpActionText, value);
    }

    public ObservableCollection<SeasonTabViewModel> SeasonTabs { get; } = new();
    public ObservableCollection<EpisodeItemViewModel> Episodes { get; } = new();

    public ICommand BackCommand { get; }
    public ICommand PlayNextUpCommand { get; }
    public ICommand SelectSeasonCommand { get; }

    public SeriesDetailsViewModel(
        ISeriesRepository seriesRepository,
        ISeasonRepository seasonRepository,
        IEpisodeRepository episodeRepository,
        IMediaFileRepository mediaFileRepository,
        IPlaybackProgressRepository progressRepository,
        IArtworkPipelineService? artworkPipelineService = null,
        IDispatcherService? dispatcher = null,
        ILogger<SeriesDetailsViewModel>? logger = null)
    {
        _seriesRepository = seriesRepository ?? throw new ArgumentNullException(nameof(seriesRepository));
        _seasonRepository = seasonRepository ?? throw new ArgumentNullException(nameof(seasonRepository));
        _episodeRepository = episodeRepository ?? throw new ArgumentNullException(nameof(episodeRepository));
        _mediaFileRepository = mediaFileRepository ?? throw new ArgumentNullException(nameof(mediaFileRepository));
        _progressRepository = progressRepository ?? throw new ArgumentNullException(nameof(progressRepository));
        _artworkPipelineService = artworkPipelineService;
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<SeriesDetailsViewModel>.Instance;

        BackCommand = new RelayCommand(OnBack);
        PlayNextUpCommand = new RelayCommand(OnPlayNextUp);
        SelectSeasonCommand = new RelayCommand<SeasonTabViewModel>(tab =>
        {
            if (tab != null)
            {
                SelectedSeason = tab;
            }
        });

        if (_artworkPipelineService != null)
        {
            _artworkPipelineService.SeriesArtworkUpdated += OnSeriesArtworkUpdated;
        }
    }

    public async Task LoadSeriesAsync(int? seriesId)
    {
        if (!seriesId.HasValue || seriesId.Value <= 0)
        {
            ClearSeries();
            return;
        }

        try
        {
            IsLoading = true;
            SeriesId = seriesId.Value;

            var series = await _seriesRepository.GetByIdAsync(seriesId.Value);
            if (series == null)
            {
                ClearSeries();
                return;
            }

            Title = series.Title;
            StartYear = series.StartYear;
            EndYear = series.EndYear;
            PosterPath = series.PosterPath;
            BackdropPath = series.BackdropPath;

            var seasons = await _seasonRepository.GetBySeriesIdAsync(series.Id);
            var allEpisodes = await _episodeRepository.GetBySeriesIdAsync(series.Id);
            TotalEpisodesCount = allEpisodes.Count;

            SeasonTabs.Clear();
            var orderedSeasons = seasons.OrderBy(s => s.SeasonNumber == 0 ? 9999 : s.SeasonNumber).ToList();

            foreach (var season in orderedSeasons)
            {
                var seasonEpCount = allEpisodes.Count(e => e.SeasonId == season.Id);
                var tab = new SeasonTabViewModel
                {
                    SeasonId = season.Id,
                    SeriesId = series.Id,
                    SeasonNumber = season.SeasonNumber,
                    Title = string.IsNullOrWhiteSpace(season.Title)
                        ? (season.SeasonNumber == 0 ? "Specials" : $"Season {season.SeasonNumber}")
                        : season.Title,
                    EpisodeCount = seasonEpCount
                };
                tab.SelectCommand = new RelayCommand(() => SelectedSeason = tab);
                SeasonTabs.Add(tab);
            }

            OnPropertyChanged(nameof(TotalSeasonsCount));
            OnPropertyChanged(nameof(MetadataSubtitle));

            // Select initial season (prefer Season 1, or first available season)
            var initialSeason = SeasonTabs.FirstOrDefault(s => s.SeasonNumber == 1) ?? SeasonTabs.FirstOrDefault();
            if (initialSeason != null)
            {
                _selectedSeason = initialSeason;
                foreach (var tab in SeasonTabs)
                {
                    tab.IsSelected = tab == _selectedSeason;
                }
                OnPropertyChanged(nameof(SelectedSeason));
                await LoadEpisodesForSeasonAsync(initialSeason.SeasonId);
            }

            // Determine Next Up episode across the entire series
            await DetermineNextUpEpisodeAsync(series.Id, allEpisodes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Series Details for series ID {SeriesId}", seriesId);
            ClearSeries();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadEpisodesForSeasonAsync(int seasonId)
    {
        if (seasonId <= 0)
        {
            Episodes.Clear();
            return;
        }

        try
        {
            var seasonEpisodes = await _episodeRepository.GetBySeasonIdAsync(seasonId);
            var orderedEpisodes = seasonEpisodes.OrderBy(e => e.EpisodeNumber).ToList();

            Episodes.Clear();
            foreach (var ep in orderedEpisodes)
            {
                var mediaFiles = await _mediaFileRepository.GetByEpisodeIdAsync(ep.Id);
                var primaryFile = mediaFiles.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath));

                var progress = await _progressRepository.GetByEpisodeIdAsync(ep.Id);

                string quality = string.Empty;
                string durationDisplay = string.Empty;
                if (primaryFile != null)
                {
                    if (primaryFile.VideoHeight >= 2160 || primaryFile.VideoWidth >= 3840) quality = "4K UHD";
                    else if (primaryFile.VideoHeight >= 1080 || primaryFile.VideoWidth >= 1920) quality = "1080p";
                    else if (primaryFile.VideoHeight >= 720 || primaryFile.VideoWidth >= 1280) quality = "720p";
                }

                if (progress != null && progress.DurationTicks > 0)
                {
                    var span = TimeSpan.FromTicks(progress.DurationTicks);
                    durationDisplay = span.TotalHours >= 1 ? $"{(int)span.TotalHours}h {span.Minutes}m" : $"{span.Minutes}m";
                }

                var epVm = new EpisodeItemViewModel
                {
                    EpisodeId = ep.Id,
                    SeriesId = ep.SeriesId,
                    SeasonId = ep.SeasonId,
                    EpisodeNumber = ep.EpisodeNumber,
                    Title = ep.Title,
                    ThumbnailPath = ep.ThumbnailPath,
                    QualityBadge = quality,
                    DurationDisplay = durationDisplay,
                    FilePath = primaryFile?.FilePath,
                    ProgressPercentage = progress?.PositionPercentage ?? 0.0
                };

                epVm.PlayCommand = new RelayCommand(() =>
                {
                    if (!string.IsNullOrWhiteSpace(epVm.FilePath) && File.Exists(epVm.FilePath))
                    {
                        PlaybackRequested?.Invoke(epVm.FilePath, epVm.EpisodeId);
                    }
                });

                Episodes.Add(epVm);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load episodes for season {SeasonId}", seasonId);
        }
    }

    private async Task DetermineNextUpEpisodeAsync(int seriesId, List<Episode> allEpisodes)
    {
        EpisodeItemViewModel? candidate = null;
        bool isResume = false;

        var orderedAll = allEpisodes.OrderBy(e => e.SeasonId).ThenBy(e => e.EpisodeNumber).ToList();

        // 1. Check if there is an in-progress episode
        foreach (var ep in orderedAll)
        {
            var prog = await _progressRepository.GetByEpisodeIdAsync(ep.Id);
            if (prog != null && !prog.IsFinished && prog.PositionPercentage > 0.02 && prog.PositionPercentage < 0.92)
            {
                var files = await _mediaFileRepository.GetByEpisodeIdAsync(ep.Id);
                var file = files.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath));
                if (file != null)
                {
                    candidate = new EpisodeItemViewModel
                    {
                        EpisodeId = ep.Id,
                        SeriesId = ep.SeriesId,
                        SeasonId = ep.SeasonId,
                        EpisodeNumber = ep.EpisodeNumber,
                        Title = ep.Title,
                        FilePath = file.FilePath,
                        ProgressPercentage = prog.PositionPercentage
                    };
                    isResume = true;
                    break;
                }
            }
        }

        // 2. Otherwise pick first uncompleted episode
        if (candidate == null)
        {
            foreach (var ep in orderedAll)
            {
                var prog = await _progressRepository.GetByEpisodeIdAsync(ep.Id);
                if (prog == null || !prog.IsFinished)
                {
                    var files = await _mediaFileRepository.GetByEpisodeIdAsync(ep.Id);
                    var file = files.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath));
                    if (file != null)
                    {
                        candidate = new EpisodeItemViewModel
                        {
                            EpisodeId = ep.Id,
                            SeriesId = ep.SeriesId,
                            SeasonId = ep.SeasonId,
                            EpisodeNumber = ep.EpisodeNumber,
                            Title = ep.Title,
                            FilePath = file.FilePath
                        };
                        break;
                    }
                }
            }
        }

        // 3. Fallback to first episode overall
        if (candidate == null && orderedAll.Count > 0)
        {
            var ep = orderedAll[0];
            var files = await _mediaFileRepository.GetByEpisodeIdAsync(ep.Id);
            var file = files.FirstOrDefault(f => !f.IsMissing && File.Exists(f.FilePath)) ?? files.FirstOrDefault();
            if (file != null)
            {
                candidate = new EpisodeItemViewModel
                {
                    EpisodeId = ep.Id,
                    SeriesId = ep.SeriesId,
                    SeasonId = ep.SeasonId,
                    EpisodeNumber = ep.EpisodeNumber,
                    Title = ep.Title,
                    FilePath = file.FilePath
                };
            }
        }

        NextUpEpisode = candidate;
        if (candidate != null)
        {
            var season = await _seasonRepository.GetByIdAsync(candidate.SeasonId);
            var sNum = season?.SeasonNumber ?? 1;
            NextUpActionText = isResume
                ? $"Resume S{sNum} E{candidate.EpisodeNumber}"
                : $"Play S{sNum} E{candidate.EpisodeNumber}";
        }
        else
        {
            NextUpActionText = "Play";
        }
    }

    private void OnBack()
    {
        NavigationRequested?.Invoke("TVShows");
    }

    private void OnPlayNextUp()
    {
        if (NextUpEpisode != null && !string.IsNullOrWhiteSpace(NextUpEpisode.FilePath))
        {
            PlaybackRequested?.Invoke(NextUpEpisode.FilePath, NextUpEpisode.EpisodeId);
        }
    }

    private void OnSeriesArtworkUpdated(int seriesId, string? posterPath, string? backdropPath)
    {
        if (seriesId != SeriesId) return;

        void Update()
        {
            PosterPath = posterPath;
            BackdropPath = backdropPath;
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

    private void ClearSeries()
    {
        SeriesId = 0;
        Title = string.Empty;
        StartYear = null;
        EndYear = null;
        PosterPath = null;
        BackdropPath = null;
        TotalEpisodesCount = 0;
        SeasonTabs.Clear();
        Episodes.Clear();
        NextUpEpisode = null;
        NextUpActionText = "Play";
        OnPropertyChanged(nameof(TotalSeasonsCount));
        OnPropertyChanged(nameof(MetadataSubtitle));
    }
}
