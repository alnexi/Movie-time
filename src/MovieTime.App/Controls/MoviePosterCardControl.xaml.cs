using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Controls;

public sealed partial class MoviePosterCardControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MoviePosterItemViewModel),
            typeof(MoviePosterCardControl),
            new PropertyMetadata(null, OnViewModelPropertyChanged));

    public MoviePosterItemViewModel? ViewModel
    {
        get => (MoviePosterItemViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MoviePosterCardControl control)
        {
            if (e.OldValue is MoviePosterItemViewModel oldVm)
            {
                oldVm.PropertyChanged -= control.OnViewModelPropertyChangedInternal;
            }
            if (e.NewValue is MoviePosterItemViewModel newVm)
            {
                newVm.PropertyChanged += control.OnViewModelPropertyChangedInternal;
            }
            control.UpdatePosterImage();
        }
    }

    private void OnViewModelPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MoviePosterItemViewModel.PosterPath) ||
            e.PropertyName == nameof(MoviePosterItemViewModel.HasPoster))
        {
            UpdatePosterImage();
        }
    }

    private void UpdatePosterImage()
    {
        if (PosterImage == null) return;

        var vm = ViewModel;
        if (vm != null && vm.HasPoster && !string.IsNullOrWhiteSpace(vm.PosterPath) && File.Exists(vm.PosterPath))
        {
            try
            {
                var uri = new Uri(vm.PosterPath);
                var bmp = new BitmapImage(uri)
                {
                    DecodePixelWidth = 340 // 2x high-DPI density for 170px width card
                };
                PosterImage.Source = bmp;
                return;
            }
            catch
            {
                // Fall back cleanly to typographical placeholder
            }
        }

        PosterImage.Source = null;
    }

    public MoviePosterCardControl()
    {
        this.InitializeComponent();
        this.Loaded += (s, e) => UpdatePosterImage();

        this.PointerEntered += OnPointerEntered;
        this.PointerExited += OnPointerExited;
        this.GotFocus += OnGotFocus;
        this.LostFocus += OnLostFocus;
        this.PointerPressed += OnPointerPressed;
        this.KeyDown += OnKeyDown;
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        AnimateFocus(true);
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (!this.FocusState.Equals(FocusState.Keyboard) && !this.FocusState.Equals(FocusState.Programmatic))
        {
            AnimateFocus(false);
        }
    }

    private void OnGotFocus(object sender, RoutedEventArgs e)
    {
        AnimateFocus(true);
    }

    private void OnLostFocus(object sender, RoutedEventArgs e)
    {
        AnimateFocus(false);
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ViewModel?.SelectCommand?.Execute(ViewModel);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter || e.Key == Windows.System.VirtualKey.Space)
        {
            ViewModel?.SelectCommand?.Execute(ViewModel);
            e.Handled = true;
        }
    }

    private void AnimateFocus(bool isFocused)
    {
        if (ViewModel != null)
        {
            ViewModel.IsFocused = isFocused;
        }

        if (PosterScale != null)
        {
            PosterScale.ScaleX = isFocused ? 1.06 : 1.0;
            PosterScale.ScaleY = isFocused ? 1.06 : 1.0;
        }

        if (FocusBorder != null)
        {
            FocusBorder.Opacity = isFocused ? 1.0 : 0.0;
        }

        if (PosterShadow != null)
        {
            PosterShadow.Opacity = isFocused ? 0.65 : 0.0;
        }
    }
}
