using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Views;

/// <summary>
/// View representing the cinematic Apple TV-inspired Movie Details screen.
/// </summary>
public sealed partial class MovieDetailsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MovieDetailsViewModel),
            typeof(MovieDetailsView),
            new PropertyMetadata(null));

    public MovieDetailsViewModel? ViewModel
    {
        get => (MovieDetailsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MovieDetailsView()
    {
        this.InitializeComponent();
    }
}
