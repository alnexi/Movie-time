using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for a navigation item within the floating Apple TV-inspired sidebar.
/// </summary>
public sealed class SidebarItemViewModel : ObservableObject
{
    private bool _isSelected;
    private string _title = string.Empty;
    private string _iconGlyph = string.Empty;
    private string? _badgeText;

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

    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string? BadgeText
    {
        get => _badgeText;
        set
        {
            if (SetProperty(ref _badgeText, value))
            {
                OnPropertyChanged(nameof(HasBadge));
            }
        }
    }

    public bool HasBadge => !string.IsNullOrWhiteSpace(BadgeText);

    public ICommand? Command { get; set; }
}
