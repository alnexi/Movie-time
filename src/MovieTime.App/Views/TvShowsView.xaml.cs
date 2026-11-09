using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Views;

public sealed partial class TvShowsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(TvShowsViewModel),
            typeof(TvShowsView),
            new PropertyMetadata(null, OnViewModelChanged));

    public TvShowsViewModel? ViewModel
    {
        get => (TvShowsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public TvShowsView()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private static async void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is TvShowsView view && e.NewValue is TvShowsViewModel vm)
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

    private void OnSeriesItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is SeriesCardViewModel item)
        {
            item.SelectCommand?.Execute(null);
        }
    }
}
