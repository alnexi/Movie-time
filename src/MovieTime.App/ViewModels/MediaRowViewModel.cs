using System.Collections.ObjectModel;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for a horizontal content row containing 16:9 media cards.
/// </summary>
public sealed class MediaRowViewModel : ObservableObject
{
    private string _title = string.Empty;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public ObservableCollection<MediaCardViewModel> Items { get; } = new();

    public bool HasItems => Items.Count > 0;
}
