using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using MovieTime.App.ViewModels;
using MovieTime.Core.Artwork;

namespace MovieTime.App.Controls;

public sealed partial class MediaCardControl : UserControl
{
    private readonly DispatcherTimer _hoverDelayTimer;
    private readonly DispatcherTimer _frameCycleTimer;
    private CancellationTokenSource? _previewCts;
    private IReadOnlyList<string>? _previewFrames;
    private int _currentFrameIndex;
    private bool _isHovered;

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
            control.ResetHoverPreview();
            control.UpdateCardImage();
        }
    }

    private void OnViewModelPropertyChangedInternal(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MediaCardViewModel.BackdropPath) ||
            e.PropertyName == nameof(MediaCardViewModel.PosterPath) ||
            e.PropertyName == nameof(MediaCardViewModel.PositionThumbnailPath) ||
            e.PropertyName == nameof(MediaCardViewModel.DisplayArtworkPath) ||
            e.PropertyName == nameof(MediaCardViewModel.HasArtwork) ||
            e.PropertyName == nameof(MediaCardViewModel.NeedsFallback))
        {
            UpdateCardImage();
        }
    }

    public MediaCardControl()
    {
        this.InitializeComponent();

        _hoverDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
        _hoverDelayTimer.Tick += OnHoverDelayTimerTick;

        _frameCycleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
        _frameCycleTimer.Tick += OnFrameCycleTimerTick;

        this.Loaded += (s, e) => UpdateCardImage();
        this.Unloaded += (s, e) => ResetHoverPreview();

        this.PointerEntered += OnPointerEntered;
        this.PointerExited += OnPointerExited;
        this.GotFocus += OnGotFocus;
        this.LostFocus += OnLostFocus;
        this.PointerPressed += OnPointerPressed;
        this.KeyDown += OnKeyDown;
    }

    private void UpdateCardImage()
    {
        if (CardImage == null) return;

        var vm = ViewModel;
        var imagePath = vm?.DisplayArtworkPath;
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

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = true;
        AnimateFocus(true);

        var vm = ViewModel;
        if (vm != null && !string.IsNullOrWhiteSpace(vm.FilePath) && vm.HasProgress)
        {
            _hoverDelayTimer.Stop();
            _hoverDelayTimer.Start();
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isHovered = false;
        ResetHoverPreview();

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
        if (!_isHovered)
        {
            AnimateFocus(false);
        }
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var originalSource = e.OriginalSource as DependencyObject;
        if (originalSource != null && CardPlayButton != null && IsChildOf(originalSource, CardPlayButton))
        {
            return;
        }

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

    private static bool IsChildOf(DependencyObject child, DependencyObject parent)
    {
        var current = child;
        while (current != null)
        {
            if (current == parent) return true;
            current = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(current);
        }
        return false;
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

        if (CardPlayButton != null)
        {
            CardPlayButton.Visibility = (isFocused && ViewModel?.PlayCommand != null)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }
    }

    private async void OnHoverDelayTimerTick(object? sender, object e)
    {
        _hoverDelayTimer.Stop();

        if (!_isHovered) return;

        var vm = ViewModel;
        if (vm == null || string.IsNullOrWhiteSpace(vm.FilePath) || !vm.HasProgress)
        {
            return;
        }

        _previewCts?.Cancel();
        _previewCts = new CancellationTokenSource();
        var ct = _previewCts.Token;

        try
        {
            var hoverService = (Application.Current as App)?.Services.GetService<IHoverPreviewService>();
            if (hoverService == null) return;

            var frames = await hoverService.GetOrExtractPreviewFramesAsync(
                vm.FilePath,
                vm.FileSizeBytes,
                vm.FileModifiedUtc,
                vm.PositionSeconds,
                ct);

            if (ct.IsCancellationRequested || !_isHovered || frames == null || frames.Count < 2)
            {
                return;
            }

            _previewFrames = frames;
            _currentFrameIndex = 0;

            ShowPreviewFrame(_previewFrames[_currentFrameIndex]);
            if (PreviewBadge != null) PreviewBadge.Visibility = Visibility.Visible;
            if (PreviewImage != null) PreviewImage.Opacity = 1.0;

            _frameCycleTimer.Stop();
            _frameCycleTimer.Start();
        }
        catch (OperationCanceledException) { }
        catch
        {
            ResetHoverPreview();
        }
    }

    private void OnFrameCycleTimerTick(object? sender, object e)
    {
        if (!_isHovered || _previewFrames == null || _previewFrames.Count < 2)
        {
            _frameCycleTimer.Stop();
            return;
        }

        _currentFrameIndex = (_currentFrameIndex + 1) % _previewFrames.Count;
        ShowPreviewFrame(_previewFrames[_currentFrameIndex]);
    }

    private void ShowPreviewFrame(string framePath)
    {
        if (PreviewImage == null || !File.Exists(framePath)) return;

        try
        {
            var uri = new Uri(framePath);
            var bmp = new BitmapImage(uri)
            {
                DecodePixelWidth = 520
            };
            PreviewImage.Source = bmp;
        }
        catch
        {
            // Ignore frame load failures
        }
    }

    private void ResetHoverPreview()
    {
        _hoverDelayTimer.Stop();
        _frameCycleTimer.Stop();

        try
        {
            _previewCts?.Cancel();
            _previewCts?.Dispose();
        }
        catch { }
        finally
        {
            _previewCts = null;
        }

        _previewFrames = null;
        _currentFrameIndex = 0;

        if (PreviewBadge != null)
        {
            PreviewBadge.Visibility = Visibility.Collapsed;
        }

        if (PreviewImage != null)
        {
            PreviewImage.Opacity = 0.0;
            PreviewImage.Source = null;
        }
    }
}
