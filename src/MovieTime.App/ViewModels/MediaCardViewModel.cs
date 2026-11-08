using System;
using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for an individual 16:9 cinematic media card in a content row.
/// </summary>
public sealed class MediaCardViewModel : ObservableObject
{
    private bool _isFocused;
    private double? _progressPercentage;
    private string? _posterPath;
    private string? _backdropPath;

    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;

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
                OnPropertyChanged(nameof(NeedsFallback));
            }
        }
    }

    public bool HasPoster => !string.IsNullOrWhiteSpace(PosterPath);
    public bool HasBackdrop => !string.IsNullOrWhiteSpace(BackdropPath);
    public bool NeedsFallback => !HasBackdrop && !HasPoster;

    public void UpdateArtwork(string? posterPath, string? backdropPath)
    {
        PosterPath = posterPath;
        BackdropPath = backdropPath;
    }

    public double? ProgressPercentage
    {
        get => _progressPercentage;
        set
        {
            if (SetProperty(ref _progressPercentage, value))
            {
                OnPropertyChanged(nameof(HasProgress));
                OnPropertyChanged(nameof(ProgressWidthPercentage));
            }
        }
    }

    public bool HasProgress => ProgressPercentage.HasValue && ProgressPercentage.Value > 0;
    public double ProgressWidthPercentage => Math.Clamp(ProgressPercentage ?? 0, 0, 1) * 100;

    public string? QualityTag { get; set; }
    public bool HasQualityTag => !string.IsNullOrWhiteSpace(QualityTag);

    public bool IsFocused
    {
        get => _isFocused;
        set => SetProperty(ref _isFocused, value);
    }

    public bool IsMovie { get; set; }
    public bool IsEpisode { get; set; }
    public bool IsSample { get; set; }

    public ICommand? SelectCommand { get; set; }
}
