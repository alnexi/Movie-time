using System;
using System.Collections.ObjectModel;
using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for the prominent hero billboard at the top of the Home screen.
/// Supports dynamic rotating movies, interactive carousel dots, and dual Play/Details navigation.
/// </summary>
public sealed class HeroBannerViewModel : ObservableObject
{
    private string _title = string.Empty;
    private string _genre = string.Empty;
    private string _ratingBadge = string.Empty;
    private string _qualityBadge = string.Empty;
    private string _description = string.Empty;
    private string? _backdropPath;
    private string _actionButtonText = "Play Movie";
    private bool _isSample;
    private int _targetMovieId;
    private string? _targetFilePath;
    private int _activeIndex;
    private int _totalCount;

    private ICommand? _actionCommand;
    private ICommand? _playCommand;
    private ICommand? _detailsCommand;
    private ICommand? _pauseCommand;
    private ICommand? _resumeCommand;
    private ICommand? _nextCommand;
    private ICommand? _previousCommand;

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

    public int TargetMovieId
    {
        get => _targetMovieId;
        set => SetProperty(ref _targetMovieId, value);
    }

    public string? TargetFilePath
    {
        get => _targetFilePath;
        set => SetProperty(ref _targetFilePath, value);
    }

    public int ActiveIndex
    {
        get => _activeIndex;
        set
        {
            if (SetProperty(ref _activeIndex, value))
            {
                UpdateActiveDot();
            }
        }
    }

    public int TotalCount
    {
        get => _totalCount;
        set
        {
            if (SetProperty(ref _totalCount, value))
            {
                OnPropertyChanged(nameof(HasMultipleItems));
            }
        }
    }

    public bool HasMultipleItems => Dots.Count > 1;

    public ObservableCollection<HeroDotViewModel> Dots { get; } = new();

    public ICommand? PlayCommand
    {
        get => _playCommand;
        set
        {
            if (SetProperty(ref _playCommand, value))
            {
                if (_actionCommand == null)
                {
                    OnPropertyChanged(nameof(ActionCommand));
                }
            }
        }
    }

    public ICommand? DetailsCommand
    {
        get => _detailsCommand;
        set => SetProperty(ref _detailsCommand, value);
    }

    public ICommand? ActionCommand
    {
        get => _actionCommand ?? _playCommand ?? _detailsCommand;
        set => SetProperty(ref _actionCommand, value);
    }

    public ICommand? PauseCommand
    {
        get => _pauseCommand;
        set => SetProperty(ref _pauseCommand, value);
    }

    public ICommand? ResumeCommand
    {
        get => _resumeCommand;
        set => SetProperty(ref _resumeCommand, value);
    }

    public ICommand? NextCommand
    {
        get => _nextCommand;
        set => SetProperty(ref _nextCommand, value);
    }

    public ICommand? PreviousCommand
    {
        get => _previousCommand;
        set => SetProperty(ref _previousCommand, value);
    }

    private void UpdateActiveDot()
    {
        for (int i = 0; i < Dots.Count; i++)
        {
            Dots[i].IsActive = (i == _activeIndex);
        }
    }

    public void SetupDots(int count, Action<int> onSelectIndex)
    {
        Dots.Clear();
        TotalCount = count;

        for (int i = 0; i < count; i++)
        {
            int index = i;
            Dots.Add(new HeroDotViewModel
            {
                Index = index,
                IsActive = (index == _activeIndex),
                SelectCommand = new RelayCommand(() => onSelectIndex(index))
            });
        }

        OnPropertyChanged(nameof(HasMultipleItems));
    }
}
