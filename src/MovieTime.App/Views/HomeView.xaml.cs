using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Views;

public sealed partial class HomeView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(HomeViewModel),
            typeof(HomeView),
            new PropertyMetadata(null, OnViewModelChanged));

    public HomeViewModel? ViewModel
    {
        get => (HomeViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public HomeView()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
        this.KeyDown += OnKeyDown;
    }

    private static async void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HomeView view && e.NewValue is HomeViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.InitializeAsync();
        }
    }

    private void OnBackdropPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.IsSidebarOpen = false;
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && ViewModel != null && ViewModel.IsSidebarOpen)
        {
            ViewModel.IsSidebarOpen = false;
            e.Handled = true;
        }
    }
}
