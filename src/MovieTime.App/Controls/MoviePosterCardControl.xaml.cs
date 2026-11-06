using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Controls;

public sealed partial class MoviePosterCardControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(MoviePosterItemViewModel),
            typeof(MoviePosterCardControl),
            new PropertyMetadata(null));

    public MoviePosterItemViewModel? ViewModel
    {
        get => (MoviePosterItemViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public MoviePosterCardControl()
    {
        this.InitializeComponent();

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
