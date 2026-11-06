using System;
using Microsoft.Extensions.DependencyInjection;
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
    }

    private static void OnViewModelPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PlayerView view)
        {
            view.ResetInactivityTimer();
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Attach video surface to LibVLC engine
            var playerService = App.Current.Services.GetService<IVlcPlayerService>();
            if (playerService is VlcPlayerService vlcService)
            {
                vlcService.AttachVideoView(LibVlcVideoView);
            }
        }
        catch
        {
            // Logging handled in service
        }

        ResetInactivityTimer();
        this.Focus(FocusState.Programmatic);
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
