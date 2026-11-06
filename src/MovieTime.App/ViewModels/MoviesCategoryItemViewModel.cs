using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for a category or filter item in the Movies Library left panel.
/// </summary>
public sealed class MoviesCategoryItemViewModel : ObservableObject
{
    private bool _isSelected;
    private string _title = string.Empty;
    private string _iconGlyph = string.Empty;
    private string _categoryKey = string.Empty;
    private int _count;

    public string Title
    {
        get => _title;
        set => SetProperty(ref _title, value);
    }

    public string IconGlyph
    {
        get => _iconGlyph;
        set => SetProperty(ref _iconGlyph, value);
    }

    public string CategoryKey
    {
        get => _categoryKey;
        set => SetProperty(ref _categoryKey, value);
    }

    public int Count
    {
        get => _count;
        set
        {
            if (SetProperty(ref _count, value))
            {
                OnPropertyChanged(nameof(HasCount));
                OnPropertyChanged(nameof(CountDisplay));
            }
        }
    }

    public bool HasCount => Count > 0;
    public string CountDisplay => Count > 0 ? Count.ToString() : string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public bool IsHeader { get; set; }

    public ICommand? Command { get; set; }
}
