using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Views;

public sealed partial class PlaylistsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(PlaylistsViewModel),
            typeof(PlaylistsView),
            new PropertyMetadata(null));

    public PlaylistsViewModel? ViewModel
    {
        get => (PlaylistsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public PlaylistsView()
    {
        this.InitializeComponent();
    }
}
