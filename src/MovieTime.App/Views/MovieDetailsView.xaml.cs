using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Views;

/// <summary>
/// View representing the cinematic Apple TV-inspired Movie Details screen.
/// </summary>
public sealed partial class MovieDetailsView : UserControl
{
    private MovieDetailsViewModel? _subscribedVm;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MovieDetailsViewModel),
            typeof(MovieDetailsView),
            new PropertyMetadata(null, OnViewModelPropertyChanged));

    public MovieDetailsViewModel? ViewModel
    {
        get => (MovieDetailsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MovieDetailsView()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MovieDetailsView view)
        {
            view.UpdateViewModelSubscription(e.NewValue as MovieDetailsViewModel);
            view.UpdateBackdropImage();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateViewModelSubscription(ViewModel);
        UpdateBackdropImage();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UpdateViewModelSubscription(null);
    }

    private void UpdateViewModelSubscription(MovieDetailsViewModel? newVm)
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
        if (e.PropertyName == nameof(MovieDetailsViewModel.BackdropPath) ||
            e.PropertyName == nameof(MovieDetailsViewModel.HasBackdrop))
        {
            UpdateBackdropImage();
        }
    }

    private void UpdateBackdropImage()
    {
        if (HeroBackdropImage == null)
        {
            return;
        }

        var vm = ViewModel;
        if (vm != null && vm.HasBackdrop && !string.IsNullOrWhiteSpace(vm.BackdropPath) && File.Exists(vm.BackdropPath))
        {
            try
            {
                var uri = new Uri(vm.BackdropPath);
                HeroBackdropImage.Source = new BitmapImage(uri);
                return;
            }
            catch
            {
                // Fallback safely if URI conversion or image decode fails
            }
        }

        HeroBackdropImage.Source = null;
    }
}
