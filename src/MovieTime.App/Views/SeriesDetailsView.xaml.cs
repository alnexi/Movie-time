using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Views;

/// <summary>
/// View representing the cinematic Apple TV-inspired Series Details screen.
/// </summary>
public sealed partial class SeriesDetailsView : UserControl
{
    private SeriesDetailsViewModel? _subscribedVm;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(SeriesDetailsViewModel),
            typeof(SeriesDetailsView),
            new PropertyMetadata(null, OnViewModelPropertyChanged));

    public SeriesDetailsViewModel? ViewModel
    {
        get => (SeriesDetailsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public SeriesDetailsView()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SeriesDetailsView view)
        {
            view.UpdateViewModelSubscription(e.NewValue as SeriesDetailsViewModel);
            view.UpdateArtworkImages();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateViewModelSubscription(ViewModel);
        UpdateArtworkImages();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UpdateViewModelSubscription(null);
    }

    private void UpdateViewModelSubscription(SeriesDetailsViewModel? newVm)
    {
        if (_subscribedVm != null)
        {
            _subscribedVm.PropertyChanged -= OnViewModelPropertyChangedInternal;
            _subscribedVm = null;
        }

        if (newVm != null)
        {
            _subscribedVm = newVm;
            _subscribedVm.PropertyChanged += OnViewModelPropertyChangedInternal;
        }
    }

    private void OnViewModelPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SeriesDetailsViewModel.BackdropPath) ||
            e.PropertyName == nameof(SeriesDetailsViewModel.PosterPath) ||
            e.PropertyName == nameof(SeriesDetailsViewModel.HasBackdrop) ||
            e.PropertyName == nameof(SeriesDetailsViewModel.HasPoster))
        {
            UpdateArtworkImages();
        }
    }

    private void UpdateArtworkImages()
    {
        var vm = ViewModel;

        if (HeroBackdropImage != null)
        {
            if (vm != null && vm.HasBackdrop && !string.IsNullOrWhiteSpace(vm.BackdropPath) && File.Exists(vm.BackdropPath))
            {
                try
                {
                    HeroBackdropImage.Source = new BitmapImage(new Uri(vm.BackdropPath));
                }
                catch
                {
                    HeroBackdropImage.Source = null;
                }
            }
            else
            {
                HeroBackdropImage.Source = null;
            }
        }

        if (PosterImage != null)
        {
            if (vm != null && vm.HasPoster && !string.IsNullOrWhiteSpace(vm.PosterPath) && File.Exists(vm.PosterPath))
            {
                try
                {
                    PosterImage.Source = new BitmapImage(new Uri(vm.PosterPath));
                }
                catch
                {
                    PosterImage.Source = null;
                }
            }
            else
            {
                PosterImage.Source = null;
            }
        }
    }

    private void OnEpisodeItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is EpisodeItemViewModel episode)
        {
            episode.PlayCommand?.Execute(null);
        }
    }
}
