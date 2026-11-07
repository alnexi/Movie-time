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
    private LibVLCSharp.Platforms.Windows.VideoView? _videoView;

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
    /// Lazily instantiates and attaches the native VideoView swapchain surface only when the player view is active.
    /// </summary>
    public void EnsureVideoSurfaceAttached()
    {
        try
        {
            if (_videoView == null)
            {
                App.Logger?.LogInformation("EnsureVideoSurfaceAttached: Initializing Core...");
                LibVLCSharp.Shared.Core.Initialize();
                App.Logger?.LogInformation("EnsureVideoSurfaceAttached: Instantiating new VideoView...");
                _videoView = new LibVLCSharp.Platforms.Windows.VideoView
                {
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch
                };
                App.Logger?.LogInformation("EnsureVideoSurfaceAttached: VideoView instantiated. Adding to VideoViewContainer...");

                VideoViewContainer.Children.Clear();
                VideoViewContainer.Children.Add(_videoView);
                App.Logger?.LogInformation("EnsureVideoSurfaceAttached: VideoView added to container.");
            }

            var playerService = App.Current.Services.GetService<IVlcPlayerService>();
            if (playerService is VlcPlayerService vlcService)
            {
                App.Logger?.LogInformation("EnsureVideoSurfaceAttached: Attaching VideoView to VlcPlayerService...");
                vlcService.AttachVideoView(_videoView);
                App.Logger?.LogInformation("EnsureVideoSurfaceAttached: VideoView attached successfully.");
            }
        }
        catch (Exception ex)
        {
            App.Logger?.LogError(ex, "Failed to initialize or attach VideoView in PlayerView: {Message}", ex.Message);
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

    private void TimelineSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (TimelineSlider.FocusState != FocusState.Unfocused && ReferenceEquals(FocusManager.GetFocusedElement(this.XamlRoot), TimelineSlider))
        {
            _isUserDraggingSlider = true;
            ViewModel?.SeekToRatio(e.NewValue);
            ResetInactivityTimer();
        }
    }

    private void TimelineSlider_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _isUserDraggingSlider = false;
        ResetInactivityTimer();
    }
}
