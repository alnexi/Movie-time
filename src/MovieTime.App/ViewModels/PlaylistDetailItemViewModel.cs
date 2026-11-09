using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

public sealed class PlaylistDetailItemViewModel : ObservableObject
{
    private int _sortOrder;

    public int PlaylistItemId { get; set; }
    public int MovieId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? ReleaseYear { get; set; }
    public string FormattedDuration { get; set; } = string.Empty;
    public string? PosterPath { get; set; }
    public bool HasPoster => !string.IsNullOrWhiteSpace(PosterPath);
    public string FilePath { get; set; } = string.Empty;
    public bool IsMissing { get; set; }

    public int SortOrder
    {
        get => _sortOrder;
        set => SetProperty(ref _sortOrder, value);
    }

    public string YearDisplay => ReleaseYear.HasValue ? $"({ReleaseYear.Value})" : string.Empty;
}
