using System.Windows.Input;
using MovieTime.App.Common;

namespace MovieTime.App.ViewModels;

/// <summary>
/// Presentation model for an individual carousel indicator dot in the Hero Banner.
/// </summary>
public sealed class HeroDotViewModel : ObservableObject
{
    private bool _isActive;

    public int Index { get; set; }

    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (SetProperty(ref _isActive, value))
            {
                OnPropertyChanged(nameof(Width));
                OnPropertyChanged(nameof(Opacity));
            }
        }
    }

    public double Width => IsActive ? 18.0 : 4.0;
    public double Opacity => IsActive ? 1.0 : 0.35;

    public ICommand? SelectCommand { get; set; }
}
