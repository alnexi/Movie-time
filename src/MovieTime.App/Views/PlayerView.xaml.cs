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
/// Native hardware-accelerated video player view with Liquid Glass HUD.
/// </summary>
public sealed partial class PlayerView : UserControl
{
    private readonly DispatcherTimer _inactivityTimer;
    private readonly DispatcherTimer _subtitleMenuCloseTimer;
    private bool _isPointerOverHud;
    private bool _isUserDraggingSlider;
    private readonly System.Collections.Generic.Dictionary<uint, Windows.Foundation.Point> _touchPoints = new();
    private readonly System.Collections.Generic.Dictionary<uint, Windows.Foundation.Point> _touchInitialPoints = new();
    private long _lastSeekGestureTime;

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
            Interval = TimeSpan.FromSeconds(3.0)
        };
        _inactivityTimer.Tick += OnInactivityTimerTick;

        _subtitleMenuCloseTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        _subtitleMenuCloseTimer.Tick += OnSubtitleMenuCloseTimerTick;

        this.Loaded += OnLoaded;
        this.Unloaded += OnUnloaded;
        this.PointerMoved += OnPointerMoved;
        this.PointerPressed += OnPointerPressed;
        this.PointerReleased += OnPointerReleased;
        this.PointerCanceled += OnPointerCanceled;
        this.PointerCaptureLost += OnPointerCaptureLost;
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

    private void OnAudioDelayMinusClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.AdjustAudioDelayCommand.Execute(-100L);
    }

    private void OnAudioDelayPlusClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.AdjustAudioDelayCommand.Execute(100L);
    }

    private void OnAudioDelayResetClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.ResetAudioDelayCommand.Execute(null);
    }

    private void OnSubtitleSizeSmallClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleFontSizeCommand.Execute(SubtitleFontSize.Small);
    }

    private void OnSubtitleSizeMediumClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleFontSizeCommand.Execute(SubtitleFontSize.Medium);
    }

    private void OnSubtitleSizeLargeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleFontSizeCommand.Execute(SubtitleFontSize.Large);
    }

    private void OnSubtitleSizeExtraLargeClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleFontSizeCommand.Execute(SubtitleFontSize.ExtraLarge);
    }

    private void OnSubtitleColorWhiteClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleColorCommand.Execute(SubtitleColor.White);
    }

    private void OnSubtitleColorYellowClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleColorCommand.Execute(SubtitleColor.Yellow);
    }

    private void OnSubtitleColorCyanClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleColorCommand.Execute(SubtitleColor.Cyan);
    }

    private void OnSubtitleColorGreenClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleColorCommand.Execute(SubtitleColor.Green);
    }

    private void OnSubtitleOutlineOffClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleOutlineCommand.Execute(SubtitleOutline.Off);
    }

    private void OnSubtitleOutlineThinClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleOutlineCommand.Execute(SubtitleOutline.Thin);
    }

    private void OnSubtitleOutlineMediumClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleOutlineCommand.Execute(SubtitleOutline.Medium);
    }

    private void OnSubtitleOutlineThickClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleOutlineCommand.Execute(SubtitleOutline.Thick);
    }

    private void OnSubtitleBgOffClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleBackgroundCommand.Execute(SubtitleBackground.Off);
    }

    private void OnSubtitleBgSubtleClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleBackgroundCommand.Execute(SubtitleBackground.Subtle);
    }

    private void OnSubtitleBgDarkClicked(object sender, RoutedEventArgs e)
    {
        ViewModel?.SetSubtitleBackgroundCommand.Execute(SubtitleBackground.Dark);
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
        _subtitleMenuCloseTimer.Stop();
    }

    /// <summary>
    /// Programmatically sets focus to PlayerView to ensure keyboard shortcuts work immediately in windowed mode.
    /// </summary>
    public void FocusForPlayback()
    {
        this.Focus(FocusState.Programmatic);
    }

    /// <summary>
    /// Handles keyboard shortcut forwarding from the window or parent views.
    /// </summary>
    public void HandlePlaybackKey(KeyRoutedEventArgs e)
    {
        OnKeyDown(this, e);
    }

    private void OnHudPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOverHud = true;
        ResetInactivityTimer();
    }

    private void OnHudPointerExited(object sender, PointerRoutedEventArgs e)
    {
        _isPointerOverHud = false;
        ResetInactivityTimer();
    }

    private void OnSubtitleMenuPointerEntered(object sender, PointerRoutedEventArgs e)
    {
        _subtitleMenuCloseTimer.Stop();
    }

    private void OnSubtitleMenuPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (ViewModel?.IsSubtitleMenuOpen == true)
        {
            _subtitleMenuCloseTimer.Stop();
            _subtitleMenuCloseTimer.Start();
        }
    }

    private void OnSubtitleMenuCloseTimerTick(object? sender, object e)
    {
        _subtitleMenuCloseTimer.Stop();
        if (ViewModel != null && ViewModel.IsSubtitleMenuOpen)
        {
            ViewModel.IsSubtitleMenuOpen = false;
        }
    }

    private void OnPlayerRootDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (ViewModel == null) return;

        // If double click originated inside interactive controls or flyout panels, ignore
        var element = e.OriginalSource as DependencyObject;
        while (element != null && element != this && element != PlayerRootGrid)
        {
            if (element is Button || element is Slider || element is TextBox ||
                element is ToggleButton || element is ItemsControl ||
                element == SubtitleMenuPanel || element == AudioMenuPanel ||
                element == InfoPanel)
            {
                return;
            }
            element = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetParent(element);
        }

        ViewModel.ToggleFullscreenCommand.Execute(null);
        e.Handled = true;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            var pt = e.GetCurrentPoint(this).Position;
            _touchPoints[e.Pointer.PointerId] = pt;
            _touchInitialPoints[e.Pointer.PointerId] = pt;

            if (_touchPoints.Count == 1)
            {
                ResetInactivityTimer();
            }
        }
        else
        {
            ResetInactivityTimer();
        }
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (e.Pointer.PointerDeviceType == Microsoft.UI.Input.PointerDeviceType.Touch)
        {
            if (_touchPoints.ContainsKey(e.Pointer.PointerId))
            {
                var currentPt = e.GetCurrentPoint(this).Position;
                _touchPoints[e.Pointer.PointerId] = currentPt;

                if (_touchPoints.Count == 2)
                {
                    var pts = System.Linq.Enumerable.ToList(_touchPoints.Values);
                    var initPts = System.Linq.Enumerable.ToList(_touchInitialPoints.Values);
                    if (pts.Count == 2 && initPts.Count == 2)
                    {
                        double deltaX = ((pts[0].X - initPts[0].X) + (pts[1].X - initPts[1].X)) / 2.0;
                        double deltaY = ((pts[0].Y - initPts[0].Y) + (pts[1].Y - initPts[1].Y)) / 2.0;

                        // Vertical gesture: Volume adjustment (Up = Increase, Down = Decrease)
                        if (Math.Abs(deltaY) > Math.Abs(deltaX) && Math.Abs(deltaY) > 20)
                        {
                            int volStep = (int)(-deltaY / 15.0);
                            if (volStep != 0 && ViewModel != null)
                            {
                                ViewModel.SetVolume(ViewModel.Volume + (volStep * 2));

                                var keys = System.Linq.Enumerable.ToList(_touchInitialPoints.Keys);
                                foreach (var k in keys)
                                {
                                    if (_touchPoints.TryGetValue(k, out var curr))
                                    {
                                        _touchInitialPoints[k] = curr;
                                    }
                                }
                            }
                            e.Handled = true;
                            return;
                        }
                        // Horizontal gesture: Seek ±10s (Right = Forward, Left = Backward)
                        else if (Math.Abs(deltaX) > Math.Abs(deltaY) && Math.Abs(deltaX) > 60)
                        {
                            long now = Environment.TickCount64;
                            if (now - _lastSeekGestureTime > 400 && ViewModel != null)
                            {
                                _lastSeekGestureTime = now;
                                if (deltaX > 0)
                                {
                                    ViewModel.SeekForwardCommand.Execute(null);
                                }
                                else
                                {
                                    ViewModel.SeekBackwardCommand.Execute(null);
                                }

                                var keys = System.Linq.Enumerable.ToList(_touchInitialPoints.Keys);
                                foreach (var k in keys)
                                {
                                    if (_touchPoints.TryGetValue(k, out var curr))
                                    {
                                        _touchInitialPoints[k] = curr;
                                    }
                                }
                            }
                            e.Handled = true;
                            return;
                        }
                    }
                }
            }
        }
        else
        {
            ResetInactivityTimer();
        }
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _touchPoints.Remove(e.Pointer.PointerId);
        _touchInitialPoints.Remove(e.Pointer.PointerId);
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        _touchPoints.Remove(e.Pointer.PointerId);
        _touchInitialPoints.Remove(e.Pointer.PointerId);
    }

    private void OnPointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _touchPoints.Remove(e.Pointer.PointerId);
        _touchInitialPoints.Remove(e.Pointer.PointerId);
    }

    private void OnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        // Guard: Do not intercept keyboard shortcuts if user is typing in a text input control
        var focused = FocusManager.GetFocusedElement(this.XamlRoot);
        if (focused is TextBox || focused is AutoSuggestBox || focused is PasswordBox || focused is RichEditBox)
        {
            return;
        }

        ResetInactivityTimer();
        var vm = ViewModel;
        if (vm == null) return;

        switch (e.Key)
        {
            case VirtualKey.Space:
            case VirtualKey.K:
                vm.PlayPauseCommand.Execute(null);
                e.Handled = true;
                break;

            case VirtualKey.J:
                vm.SeekBackwardCommand.Execute(null); // 10s backward
                e.Handled = true;
                break;

            case VirtualKey.L:
                vm.SeekForwardCommand.Execute(null); // 10s forward
                e.Handled = true;
                break;

            case VirtualKey.Left:
                vm.SeekBackward5sCommand.Execute(null); // 5s backward (YouTube standard)
                e.Handled = true;
                break;

            case VirtualKey.Right:
                vm.SeekForward5sCommand.Execute(null); // 5s forward (YouTube standard)
                e.Handled = true;
                break;

            case VirtualKey.Up:
                vm.IncreaseVolume(5);
                e.Handled = true;
                break;

            case VirtualKey.Down:
                vm.DecreaseVolume(5);
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

            case VirtualKey.H:
                vm.NavigateHomeCommand.Execute(null);
                e.Handled = true;
                break;

            // Speed Control Shortcuts: + / = / ] to speed up, - / _ / [ to slow down
            case (VirtualKey)187:
            case (VirtualKey)221:
            case VirtualKey.Add:
                vm.IncreaseSpeed();
                e.Handled = true;
                break;

            case (VirtualKey)189:
            case (VirtualKey)219:
            case VirtualKey.Subtract:
                vm.DecreaseSpeed();
                e.Handled = true;
                break;

            // Numeric Jump Shortcuts (0% through 90%)
            case VirtualKey.Number0:
            case VirtualKey.NumberPad0:
                vm.SeekToPercentage(0);
                e.Handled = true;
                break;

            case VirtualKey.Number1:
            case VirtualKey.NumberPad1:
                vm.SeekToPercentage(10);
                e.Handled = true;
                break;

            case VirtualKey.Number2:
            case VirtualKey.NumberPad2:
                vm.SeekToPercentage(20);
                e.Handled = true;
                break;

            case VirtualKey.Number3:
            case VirtualKey.NumberPad3:
                vm.SeekToPercentage(30);
                e.Handled = true;
                break;

            case VirtualKey.Number4:
            case VirtualKey.NumberPad4:
                vm.SeekToPercentage(40);
                e.Handled = true;
                break;

            case VirtualKey.Number5:
            case VirtualKey.NumberPad5:
                vm.SeekToPercentage(50);
                e.Handled = true;
                break;

            case VirtualKey.Number6:
            case VirtualKey.NumberPad6:
                vm.SeekToPercentage(60);
                e.Handled = true;
                break;

            case VirtualKey.Number7:
            case VirtualKey.NumberPad7:
                vm.SeekToPercentage(70);
                e.Handled = true;
                break;

            case VirtualKey.Number8:
            case VirtualKey.NumberPad8:
                vm.SeekToPercentage(80);
                e.Handled = true;
                break;

            case VirtualKey.Number9:
            case VirtualKey.NumberPad9:
                vm.SeekToPercentage(90);
                e.Handled = true;
                break;

            case VirtualKey.Escape:
            case VirtualKey.Back:
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

        // Auto-hide controls only during active playback, when pointer is NOT hovering over HUD controls,
        // when menus are closed, and user is not actively scrubbing
        if (ViewModel.IsPlaying &&
            !_isPointerOverHud &&
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
