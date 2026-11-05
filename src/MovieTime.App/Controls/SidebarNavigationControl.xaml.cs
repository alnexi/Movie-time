using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Controls;

public sealed partial class SidebarNavigationControl : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(HomeViewModel),
            typeof(SidebarNavigationControl),
            new PropertyMetadata(null, OnViewModelChanged));

    public HomeViewModel? ViewModel
    {
        get => (HomeViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public SidebarNavigationControl()
    {
        this.InitializeComponent();
    }

    private static void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SidebarNavigationControl control)
        {
            if (e.OldValue is HomeViewModel oldVm)
            {
                oldVm.PropertyChanged -= control.OnViewModelPropertyChanged;
            }

            if (e.NewValue is HomeViewModel newVm)
            {
                newVm.PropertyChanged += control.OnViewModelPropertyChanged;
                control.UpdateSidebarVisual(newVm.IsSidebarOpen);
            }
        }
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HomeViewModel.IsSidebarOpen) && ViewModel != null)
        {
            UpdateSidebarVisual(ViewModel.IsSidebarOpen);
        }
    }

    private void UpdateSidebarVisual(bool isOpen)
    {
        if (SidebarTranslate != null && SidebarContainer != null)
        {
            SidebarTranslate.TranslateX = isOpen ? 0 : -280;
            SidebarContainer.Opacity = isOpen ? 1.0 : 0.0;
        }

        this.Visibility = isOpen ? Visibility.Visible : Visibility.Collapsed;
    }
}
