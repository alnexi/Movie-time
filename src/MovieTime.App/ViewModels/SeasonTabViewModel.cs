using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model representing an individual season selector tab
/// in the Series Details view.
/// </summary>
public sealed class SeasonTabViewModel : ObservableObject
{
    private bool _isSelected;

    public int SeasonId { get; init; }
    public int SeriesId { get; init; }
    public int SeasonNumber { get; init; }
    public string Title { get; init; } = string.Empty;
    public int EpisodeCount { get; init; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string DisplayText => EpisodeCount > 0
        ? $"{Title} ({EpisodeCount})"
        : Title;

    public ICommand? SelectCommand { get; set; }
}
