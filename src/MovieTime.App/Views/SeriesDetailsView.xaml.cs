using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Media.Animation;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Views;

/// <summary>
/// View representing the cinematic Series Details screen.
/// </summary>
public sealed partial class SeriesDetailsView : UserControl
{
    private SeriesDetailsViewModel? _subscribedVm;
    private string? _currentBackdropLoaded;

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
        ViewModel?.StopRotation();
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
            var path = vm?.BackdropPath;
            if (vm != null && vm.HasBackdrop && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                if (!string.Equals(_currentBackdropLoaded, path, StringComparison.OrdinalIgnoreCase))
                {
                    _currentBackdropLoaded = path;
                    try
                    {
                        var bmp = new BitmapImage(new Uri(path))
                        {
                            DecodePixelWidth = 1920
                        };

                        HeroBackdropImage.Opacity = 0.25;
                        HeroBackdropImage.Source = bmp;

                        var anim = new DoubleAnimation
                        {
                            From = 0.25,
                            To = 1.0,
                            Duration = TimeSpan.FromMilliseconds(350),
                            EnableDependentAnimation = true
                        };

                        var storyboard = new Storyboard();
                        storyboard.Children.Add(anim);
                        Storyboard.SetTarget(anim, HeroBackdropImage);
                        Storyboard.SetTargetProperty(anim, "Opacity");
                        storyboard.Begin();
                    }
                    catch
                    {
                        HeroBackdropImage.Source = null;
                    }
                }
            }
            else
            {
                _currentBackdropLoaded = null;
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
