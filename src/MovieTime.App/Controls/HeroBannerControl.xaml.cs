using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Controls;

public sealed partial class HeroBannerControl : UserControl
{
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
        if (vm != null && vm.HasBackdrop && !string.IsNullOrWhiteSpace(vm.BackdropPath) && File.Exists(vm.BackdropPath))
        {
            try
            {
                var uri = new Uri(vm.BackdropPath);
                var bmp = new BitmapImage(uri)
                {
                    DecodePixelWidth = 1920
                };
                HeroBackdropImage.Source = bmp;
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
}
