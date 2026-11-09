using System;
using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for an individual 16:9 cinematic media card in a content row.
/// Supports genuine backdrop/poster/position artwork, hover preview metadata, and navigation commands.
/// </summary>
public sealed class MediaCardViewModel : ObservableObject
{
    private bool _isFocused;
    private double? _progressPercentage;
    private string? _posterPath;
    private string? _backdropPath;
    private string? _positionThumbnailPath;

    public int Id { get; set; }
    public int? MovieId { get; set; }
    public int? EpisodeId { get; set; }
    public int? MediaFileId { get; set; }
    public string? FilePath { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime? FileModifiedUtc { get; set; }
    public long? PositionTicks { get; set; }
    public long? DurationTicks { get; set; }
    public long PositionSeconds => PositionTicks.HasValue ? (long)TimeSpan.FromTicks(PositionTicks.Value).TotalSeconds : 0;

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
                OnPropertyChanged(nameof(DisplayArtworkPath));
                OnPropertyChanged(nameof(HasArtwork));
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
                OnPropertyChanged(nameof(DisplayArtworkPath));
                OnPropertyChanged(nameof(HasArtwork));
                OnPropertyChanged(nameof(NeedsFallback));
            }
        }
    }

    public string? PositionThumbnailPath
    {
        get => _positionThumbnailPath;
        set
        {
            if (SetProperty(ref _positionThumbnailPath, value))
            {
                OnPropertyChanged(nameof(HasPositionThumbnail));
                OnPropertyChanged(nameof(DisplayArtworkPath));
                OnPropertyChanged(nameof(HasArtwork));
                OnPropertyChanged(nameof(NeedsFallback));
            }
        }
    }

    public bool HasPoster => !string.IsNullOrWhiteSpace(PosterPath);
    public bool HasBackdrop => !string.IsNullOrWhiteSpace(BackdropPath);
    public bool HasPositionThumbnail => !string.IsNullOrWhiteSpace(PositionThumbnailPath);

    /// <summary>
    /// Prefer genuine position thumbnail (for Continue Watching), then backdrop, then poster.
    /// </summary>
    public string? DisplayArtworkPath => PositionThumbnailPath ?? BackdropPath ?? PosterPath;
    public bool HasArtwork => !string.IsNullOrWhiteSpace(DisplayArtworkPath);
    public bool NeedsFallback => !HasArtwork;

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

    /// <summary>
    /// Invoked when the card is clicked or selected (opens Movie Details for that movie).
    /// </summary>
    public ICommand? SelectCommand { get; set; }

    /// <summary>
    /// Invoked when the user triggers direct playback/resume from Home (resumes at saved position).
    /// </summary>
    public ICommand? PlayCommand { get; set; }

    /// <summary>
    /// Explicit command to navigate to Movie Details.
    /// </summary>
    public ICommand? DetailsCommand { get; set; }
}
