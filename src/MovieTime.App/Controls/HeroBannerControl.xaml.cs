using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Controls;

public sealed partial class HeroBannerControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(HeroBannerViewModel),
            typeof(HeroBannerControl),
            new PropertyMetadata(null));

    public HeroBannerViewModel? ViewModel
    {
        get => (HeroBannerViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public HeroBannerControl()
    {
        this.InitializeComponent();
    }
}
