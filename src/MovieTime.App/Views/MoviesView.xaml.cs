using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Views;

public sealed partial class MoviesView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MoviesViewModel),
            typeof(MoviesView),
            new PropertyMetadata(null, OnViewModelChanged));

    public MoviesViewModel? ViewModel
    {
        get => (MoviesViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MoviesView()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
        this.KeyDown += OnKeyDown;
    }

    private static async void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MoviesView view && e.NewValue is MoviesViewModel vm)
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

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is TextBox tb && ViewModel != null)
        {
            ViewModel.SearchQuery = tb.Text;
        }
    }

    private void OnMovieItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is MoviePosterItemViewModel item && ViewModel != null)
        {
            ViewModel.SelectMovieCommand.Execute(item);
        }
    }

    private void OnBackdropPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel != null && ViewModel.IsQuickActionOpen)
        {
            ViewModel.DismissQuickActionCommand.Execute(null);
        }
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape && ViewModel != null && ViewModel.IsQuickActionOpen)
        {
            ViewModel.DismissQuickActionCommand.Execute(null);
            e.Handled = true;
        }
    }
}
