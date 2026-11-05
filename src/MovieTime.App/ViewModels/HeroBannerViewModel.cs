using System;
using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for the prominent hero billboard at the top of the Apple TV Home screen.
/// </summary>
public sealed class HeroBannerViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _genre = string.Empty;
    private string _ratingBadge = string.Empty;
    private string _qualityBadge = string.Empty;
    private string _description = string.Empty;
    private string? _backdropPath;
    private string _actionButtonText = "Go to Movie";
    private bool _isSample;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string Genre
    {
        get => _genre;
        set => SetProperty(ref _genre, value);
    }

    public string RatingBadge
    {
        get => _ratingBadge;
        set => SetProperty(ref _ratingBadge, value);
    }

    public bool HasRatingBadge => !string.IsNullOrWhiteSpace(RatingBadge);

    public string QualityBadge
    {
        get => _qualityBadge;
        set => SetProperty(ref _qualityBadge, value);
    }

    public bool HasQualityBadge => !string.IsNullOrWhiteSpace(QualityBadge);

    public string Description
    {
        get => _description;
        set => SetProperty(ref _description, value);
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

    public bool HasBackdrop => !string.IsNullOrWhiteSpace(BackdropPath);

    public string ActionButtonText
    {
        get => _actionButtonText;
        set => SetProperty(ref _actionButtonText, value);
    }

    public bool IsSample
    {
        get => _isSample;
        set => SetProperty(ref _isSample, value);
    }

    public int TargetMovieId { get; set; }
    public ICommand? ActionCommand { get; set; }
}
