using System;
using System.IO;
using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for a movie poster item in the Movies Library grid.
/// </summary>
public sealed class MoviePosterItemViewModel : ObservableObject
{
    private bool _isFocused;
    private bool _isSelected;

    private string? _posterPath;
    private string? _backdropPath;

    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? ReleaseYear { get; set; }
    public string? Edition { get; set; }

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

    public string? QualityTag { get; set; }
    public string? VideoCodec { get; set; }
    public string? ResolutionDisplay { get; set; }
    public string? FilePath { get; set; }
    public string? FileSizeDisplay { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string YearDisplay => ReleaseYear.HasValue && ReleaseYear.Value > 0
        ? ReleaseYear.Value.ToString()
        : string.Empty;

    public string MetadataSubtitle
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(YearDisplay) && !string.IsNullOrWhiteSpace(QualityTag))
            {
                return $"{YearDisplay} \u2022 {QualityTag}";
            }
            if (!string.IsNullOrWhiteSpace(YearDisplay))
            {
                return YearDisplay;
            }
            if (!string.IsNullOrWhiteSpace(QualityTag))
            {
                return QualityTag;
            }
            return !string.IsNullOrWhiteSpace(Edition) ? Edition : "Movie";
        }
    }

    public bool HasPoster => !string.IsNullOrWhiteSpace(PosterPath) && File.Exists(PosterPath);
    public bool NeedsFallback => !HasPoster;
    public bool HasBackdrop => !string.IsNullOrWhiteSpace(BackdropPath) && File.Exists(BackdropPath);

    public void UpdateArtwork(string? posterPath, string? backdropPath)
    {
        PosterPath = posterPath;
        BackdropPath = backdropPath;
    }
    public bool HasQualityTag => !string.IsNullOrWhiteSpace(QualityTag);
    public bool HasYear => ReleaseYear.HasValue && ReleaseYear.Value > 0;

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
    public ICommand? PlayCommand { get; set; }
    public ICommand? DetailsCommand { get; set; }
}
