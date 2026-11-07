using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using MovieTime.App.Services;
using MovieTime.App.ViewModels;
using MovieTime.Core.Playback;
using Windows.System;

namespace MovieTime.App.Views;

/// <summary>
/// Native hardware-accelerated video player view with Apple TV-inspired Liquid Glass HUD.
/// </summary>
public sealed partial class PlayerView : UserControl
{
    private readonly DispatcherTimer _inactivityTimer;
    private bool _isUserDraggingSlider;

    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(PlayerViewModel),
            typeof(PlayerView),
            new PropertyMetadata(null, OnViewModelPropertyChanged));

    public PlayerViewModel? ViewModel
    {
        get => (PlayerViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public PlayerView()
    {
        this.InitializeComponent();

        _inactivityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3.5)
        };
        _inactivityTimer.Tick += OnInactivityTimerTick;

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
        this.PointerMoved += OnPointerMoved;
        this.PointerPressed += OnPointerPressed;
        this.KeyDown += OnKeyDown;

        // Register timeline pointer handlers with handledEventsToo: true
        // This ensures scrubbing and clicking are reliably captured even when internal Slider parts handle events
        TimelineSlider.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnTimelinePointerPressed),
            handledEventsToo: true);

        TimelineSlider.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(OnTimelinePointerMoved),
            handledEventsToo: true);

        TimelineSlider.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnTimelinePointerReleased),
            handledEventsToo: true);

        TimelineSlider.AddHandler(
            UIElement.PointerCaptureLostEvent,
            new PointerEventHandler(OnTimelinePointerCaptureLost),
            handledEventsToo: true);

        TimelineContainer.AddHandler(
            UIElement.PointerPressedEvent,
            new PointerEventHandler(OnTimelinePointerPressed),
            handledEventsToo: true);
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlayerView view)
        {
            view.DataContext = e.NewValue;
            view.ResetInactivityTimer();
        }
    }

    /// <summary>
    /// Connects the native VideoView swapchain surface to the VLC player service.
    /// </summary>
    public void EnsureVideoSurfaceAttached()
    {
        try
        {
            var playerService = App.Current.Services.GetService<IVlcPlayerService>();
            if (playerService is VlcPlayerService vlcService)
            {
                vlcService.AttachVideoView(LibVlcVideoView);
            }
        }
        catch (Exception ex)
        {
            App.Logger?.LogError(ex, "Failed to attach VideoView in PlayerView: {Message}", ex.Message);
        }
    }

    private void OnSubtitleTrackItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlayerTrackInfo track })
        {
            ViewModel?.SelectSubtitleTrackCommand.Execute(track);
        }
    }

    private void OnAudioTrackItemClicked(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: PlayerTrackInfo track })
        {
            ViewModel?.SelectAudioTrackCommand.Execute(track);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (this.Visibility == Visibility.Visible)
        {
            EnsureVideoSurfaceAttached();
            ResetInactivityTimer();
            this.Focus(FocusState.Programmatic);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _inactivityTimer.Stop();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        ResetInactivityTimer();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        ResetInactivityTimer();
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        ResetInactivityTimer();
        var vm = ViewModel;
        if (vm == null) return;

        switch (e.Key)
        {
            case VirtualKey.Space:
                vm.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.Left:
                vm.SeekBackwardCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.Right:
                vm.SeekForwardCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.Up:
                vm.SetVolume(Math.Min(100, vm.Volume + 5));
                e.Handled = true;
                break;

            case VirtualKey.Down:
                vm.SetVolume(Math.Max(0, vm.Volume - 5));
                e.Handled = true;
                break;

            case VirtualKey.M:
                vm.ToggleMuteCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.F:
                vm.ToggleFullscreenCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.Escape:
                if (vm.IsFullscreen)
                {
                    vm.ToggleFullscreenCommand.Execute(null);
                }
                else
                {
                    vm.BackCommand.Execute(null);
                }
                e.Handled = true;
                break;
        }
    }

    private void ResetInactivityTimer()
    {
        if (ViewModel != null)
        {
            ViewModel.IsControlsVisible = true;
        }

        _inactivityTimer.Stop();
        _inactivityTimer.Start();
    }

    private void OnInactivityTimerTick(object? sender, object e)
    {
        if (ViewModel == null) return;

        // Auto-hide controls only during active playback and when menus are closed
        if (ViewModel.IsPlaying &&
            !ViewModel.IsAudioMenuOpen &&
            !ViewModel.IsSubtitleMenuOpen &&
            !ViewModel.IsInfoOpen &&
            !_isUserDraggingSlider)
        {
            ViewModel.IsControlsVisible = false;
        }
    }

    private void OnTimelinePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var point = e.GetCurrentPoint(TimelineSlider);
        if (point.Properties.IsLeftButtonPressed && TimelineSlider.ActualWidth > 0)
        {
            _isUserDraggingSlider = true;
            TimelineSlider.CapturePointer(e.Pointer);

            double ratio = Math.Clamp(point.Position.X / TimelineSlider.ActualWidth, 0.0, 1.0);
            TimelineSlider.Value = ratio;

            ViewModel?.BeginSeek();
            ViewModel?.SeekPreview(ratio);
            ResetInactivityTimer();
            e.Handled = true;
        }
    }

    private void OnTimelinePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_isUserDraggingSlider && TimelineSlider.ActualWidth > 0)
        {
            var point = e.GetCurrentPoint(TimelineSlider);
            double ratio = Math.Clamp(point.Position.X / TimelineSlider.ActualWidth, 0.0, 1.0);
            TimelineSlider.Value = ratio;

            ViewModel?.SeekPreview(ratio);
            ResetInactivityTimer();
            e.Handled = true;
        }
    }

    private void OnTimelinePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_isUserDraggingSlider)
        {
            _isUserDraggingSlider = false;
            try
            {
                TimelineSlider.ReleasePointerCapture(e.Pointer);
            }
            catch
            {
                // Best effort release
            }

            if (TimelineSlider.ActualWidth > 0)
            {
                var point = e.GetCurrentPoint(TimelineSlider);
                double ratio = Math.Clamp(point.Position.X / TimelineSlider.ActualWidth, 0.0, 1.0);
                TimelineSlider.Value = ratio;
                ViewModel?.EndSeek(ratio);
            }
            else
            {
                ViewModel?.EndSeek(TimelineSlider.Value);
            }

            ResetInactivityTimer();
            e.Handled = true;
        }
    }

    private void OnTimelinePointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        if (_isUserDraggingSlider)
        {
            _isUserDraggingSlider = false;
            ViewModel?.EndSeek(TimelineSlider.Value);
            ResetInactivityTimer();
        }
    }

    private void TimelineSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        // Only trigger seek from ValueChanged if NOT actively handled by pointer drag/click
        // and NOT caused by background playback position updates from LibVLC
        if (!_isUserDraggingSlider && ViewModel != null && !ViewModel.IsUpdatingFromPlayer)
        {
            if (Math.Abs(e.NewValue - ViewModel.ProgressRatio) > 0.001)
            {
                ViewModel.SeekToRatio(e.NewValue);
                ResetInactivityTimer();
            }
        }
    }
}
