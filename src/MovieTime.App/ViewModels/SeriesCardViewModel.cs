using System;
using System.IO;
using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for a TV show card in the Apple TV-inspired TV Shows library grid.
/// </summary>
public sealed class SeriesCardViewModel : ObservableObject
{
    private bool _isFocused;
    private bool _isSelected;
    private string? _posterPath;
    private string? _backdropPath;

    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? StartYear { get; set; }
    public int? EndYear { get; set; }
    public int SeasonCount { get; set; }
    public int EpisodeCount { get; set; }

    public string? PosterPath
    {
        get => _posterPath;
        set
        {
            if (SetProperty(ref _posterPath, value))
            {
                OnPropertyChanged(nameof(HasPoster));
                OnPropertyChanged(nameof(NeedsFallback));
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
            }
        }
    }

    public bool HasPoster => !string.IsNullOrWhiteSpace(PosterPath) && File.Exists(PosterPath);
    public bool NeedsFallback => !HasPoster;
    public bool HasBackdrop => !string.IsNullOrWhiteSpace(BackdropPath) && File.Exists(BackdropPath);

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

    public string MetadataSubtitle
    {
        get
        {
            var seasonsText = SeasonCount == 1 ? "1 Season" : $"{SeasonCount} Seasons";
            var episodesText = EpisodeCount == 1 ? "1 Episode" : $"{EpisodeCount} Episodes";

            if (SeasonCount > 0 && EpisodeCount > 0)
            {
                return $"{seasonsText} \u2022 {episodesText}";
            }
            if (SeasonCount > 0)
            {
                return seasonsText;
            }
            if (!string.IsNullOrWhiteSpace(YearDisplay))
            {
                return YearDisplay;
            }
            return "TV Series";
        }
    }

    public bool HasYear => !string.IsNullOrWhiteSpace(YearDisplay);

    public bool IsFocused
    {
        get => _isFocused;
        set => SetProperty(ref _isFocused, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public ICommand? SelectCommand { get; set; }

    public void UpdateArtwork(string? posterPath, string? backdropPath)
    {
        PosterPath = posterPath;
        BackdropPath = backdropPath;
    }
}
