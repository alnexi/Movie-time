using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MovieTime.App.ViewModels;

namespace MovieTime.App.Views;

/// <summary>
/// View representing the Settings and MLibrary multi-drive management page.
/// </summary>
public sealed partial class SettingsView : UserControl
{
    public static readonly DependencyProperty ViewModelProperty =
        DependencyProperty.Register(
            nameof(ViewModel),
            typeof(SettingsViewModel),
            typeof(SettingsView),
            new PropertyMetadata(null, OnViewModelChanged));

    public SettingsViewModel? ViewModel
    {
        get => (SettingsViewModel?)GetValue(ViewModelProperty);
        set => SetValue(ViewModelProperty, value);
    }

    public Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BoolToInvertedVisibility(bool value) => value ? Visibility.Collapsed : Visibility.Visible;

    public SettingsView()
    {
        this.InitializeComponent();
        this.Loaded += OnLoaded;
    }

    private static async void OnViewModelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is SettingsView view && e.NewValue is SettingsViewModel vm)
        {
            await vm.InitializeAsync();
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            await ViewModel.InitializeAsync();
        }
    }
}
