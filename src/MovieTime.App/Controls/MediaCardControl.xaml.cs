using System;
using System.ComponentModel;
using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Controls;

public sealed partial class MediaCardControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MediaCardViewModel),
            typeof(MediaCardControl),
            new PropertyMetadata(null, OnViewModelPropertyChanged));

    public MediaCardViewModel? ViewModel
    {
        get => (MediaCardViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MediaCardControl control)
        {
            if (e.OldValue is MediaCardViewModel oldVm)
            {
                oldVm.PropertyChanged -= control.OnViewModelPropertyChangedInternal;
            }
            if (e.NewValue is MediaCardViewModel newVm)
            {
                newVm.PropertyChanged += control.OnViewModelPropertyChangedInternal;
            }
            control.UpdateCardImage();
        }
    }

    private void OnViewModelPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaCardViewModel.BackdropPath) ||
            e.PropertyName == nameof(MediaCardViewModel.PosterPath) ||
            e.PropertyName == nameof(MediaCardViewModel.HasBackdrop) ||
            e.PropertyName == nameof(MediaCardViewModel.HasPoster))
        {
            UpdateCardImage();
        }
    }

    private void UpdateCardImage()
    {
        if (CardImage == null) return;

        var vm = ViewModel;
        var imagePath = vm?.BackdropPath ?? vm?.PosterPath;
        if (vm != null && !string.IsNullOrWhiteSpace(imagePath) && File.Exists(imagePath))
        {
            try
            {
                var uri = new Uri(imagePath);
                var bmp = new BitmapImage(uri)
                {
                    DecodePixelWidth = 520 // 2x high-DPI density for 260px wide card
                };
                CardImage.Source = bmp;
                return;
            }
            catch
            {
                // Fall back cleanly to typographical placeholder
            }
        }

        CardImage.Source = null;
    }

    public MediaCardControl()
    {
        this.InitializeComponent();
        this.Loaded += (s, e) => UpdateCardImage();

        this.PointerEntered += OnPointerEntered;
        this.PointerExited += OnPointerExited;
        this.GotFocus += OnGotFocus;
        this.LostFocus += OnLostFocus;
        this.PointerPressed += OnPointerPressed;
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

    private void AnimateFocus(bool isFocused)
    {
        if (ViewModel != null)
        {
            ViewModel.IsFocused = isFocused;
        }

        if (CardScale != null)
        {
            CardScale.ScaleX = isFocused ? 1.06 : 1.0;
            CardScale.ScaleY = isFocused ? 1.06 : 1.0;
        }

        if (FocusBorder != null)
        {
            FocusBorder.Opacity = isFocused ? 1.0 : 0.0;
        }

        if (CardShadow != null)
        {
            CardShadow.Opacity = isFocused ? 0.65 : 0.0;
        }
    }
}
