using System;
using System.IO;
using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model representing an individual episode item inside a season's episode list
/// on the Series Details view.
/// </summary>
public sealed class EpisodeItemViewModel : ObservableObject
{
    private string? _thumbnailPath;
    private double _progressPercentage;
    private bool _hasProgress;

    public int EpisodeId { get; init; }
    public int SeriesId { get; init; }
    public int SeasonId { get; init; }
    public int EpisodeNumber { get; init; }
    public string Title { get; init; } = string.Empty;
    public string? FilePath { get; init; }
    public long SizeBytes { get; init; }
    public string? QualityBadge { get; init; }
    public string DurationDisplay { get; init; } = string.Empty;

    public string FileSizeDisplay
    {
        get
        {
            if (SizeBytes <= 0) return string.Empty;
            double gb = SizeBytes / (1024.0 * 1024.0 * 1024.0);
            return gb >= 1.0 ? $"{gb:F1} GB" : $"{SizeBytes / (1024.0 * 1024.0):F0} MB";
        }
    }

    public bool HasDuration => !string.IsNullOrWhiteSpace(DurationDisplay);
    public bool HasQualityBadge => !string.IsNullOrWhiteSpace(QualityBadge);
    public bool IsAvailable => !string.IsNullOrWhiteSpace(FilePath) && File.Exists(FilePath);

    public string DisplayTitle
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Title) || Title.Equals($"Episode {EpisodeNumber}", StringComparison.OrdinalIgnoreCase))
            {
                return $"Episode {EpisodeNumber}";
            }
            return $"{EpisodeNumber}. {Title}";
        }
    }

    public string? ThumbnailPath
    {
        get => _thumbnailPath;
        set
        {
            if (SetProperty(ref _thumbnailPath, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
                OnPropertyChanged(nameof(NeedsFallback));
            }
        }
    }

    public bool HasThumbnail => !string.IsNullOrWhiteSpace(ThumbnailPath) && File.Exists(ThumbnailPath);
    public bool NeedsFallback => !HasThumbnail;

    public double ProgressPercentage
    {
        get => _progressPercentage;
        set
        {
            if (SetProperty(ref _progressPercentage, value))
            {
                HasProgress = _progressPercentage > 0.02 && _progressPercentage < 0.95;
            }
        }
    }

    public bool HasProgress
    {
        get => _hasProgress;
        private set => SetProperty(ref _hasProgress, value);
    }

    public ICommand? PlayCommand { get; set; }
    public ICommand? RemoveFromLibraryCommand { get; set; }
    public ICommand? DeleteFromDiskCommand { get; set; }

    public void UpdateThumbnail(string? path)
    {
        ThumbnailPath = path;
    }
}
