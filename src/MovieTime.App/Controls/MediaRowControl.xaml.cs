using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Controls;

public sealed partial class MediaRowControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MediaRowViewModel),
            typeof(MediaRowControl),
            new PropertyMetadata(null));

    public MediaRowViewModel? ViewModel
    {
        get => (MediaRowViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MediaRowControl()
    {
        this.InitializeComponent();
    }
}
