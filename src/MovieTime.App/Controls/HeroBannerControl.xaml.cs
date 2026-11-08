using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Controls;

public sealed partial class HeroBannerControl : UserControl
{
    private string? _currentBackdropLoaded;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(HeroBannerViewModel),
            typeof(HeroBannerControl),
            new PropertyMetadata(null, OnViewModelPropertyChanged));

    public HeroBannerViewModel? ViewModel
    {
        get => (HeroBannerViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is HeroBannerControl control)
        {
            if (e.OldValue is HeroBannerViewModel oldVm)
            {
                oldVm.PropertyChanged -= control.OnViewModelPropertyChangedInternal;
            }
            if (e.NewValue is HeroBannerViewModel newVm)
            {
                newVm.PropertyChanged += control.OnViewModelPropertyChangedInternal;
            }
            control.UpdateBackdropImage();
        }
    }

    private void OnViewModelPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HeroBannerViewModel.BackdropPath) ||
            e.PropertyName == nameof(HeroBannerViewModel.HasBackdrop))
        {
            UpdateBackdropImage();
        }
    }

    private void UpdateBackdropImage()
    {
        if (HeroBackdropImage == null) return;

        var vm = ViewModel;
        var path = vm?.BackdropPath;

        if (string.Equals(_currentBackdropLoaded, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _currentBackdropLoaded = path;

        if (vm != null && vm.HasBackdrop && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                var uri = new Uri(path);
                var bmp = new BitmapImage(uri)
                {
                    DecodePixelWidth = 1920
                };

                // Gentle crossfade effect
                HeroBackdropImage.Opacity = 0.2;
                HeroBackdropImage.Source = bmp;

                var anim = new DoubleAnimation
                {
                    From = 0.2,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(350),
                    EnableDependentAnimation = true
                };

                var storyboard = new Storyboard();
                storyboard.Children.Add(anim);
                Storyboard.SetTarget(anim, HeroBackdropImage);
                Storyboard.SetTargetProperty(anim, "Opacity");
                storyboard.Begin();

                return;
            }
            catch
            {
                // Fall back cleanly to ambient gradient
            }
        }

        HeroBackdropImage.Source = null;
    }

    public HeroBannerControl()
    {
        this.InitializeComponent();
        this.Loaded += (s, e) => UpdateBackdropImage();
    }

    private void OnHeroPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        ViewModel?.PauseCommand?.Execute(null);
    }

    private void OnHeroPointerExited(object sender, PointerRoutedEventArgs e)
    {
        ViewModel?.ResumeCommand?.Execute(null);
    }

    private void OnTitleOrBackdropPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel?.DetailsCommand?.Execute(null);
    }
}
