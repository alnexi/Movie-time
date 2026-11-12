using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MovieTime.Installer;

public partial class UninstallWindow : Window
{
    private readonly string _installDir;
    private readonly UninstallationService _uninstaller = new();
    private CancellationTokenSource? _cts;

    public UninstallWindow(string installDir)
    {
        InitializeComponent();
        _installDir = installDir;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    private async void OnStartUninstallClick(object sender, RoutedEventArgs e)
    {
        ConfirmPanel.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        DonePanel.Visibility = Visibility.Collapsed;

        _cts = new CancellationTokenSource();

        var progress = new Progress<(double Progress, string Status, string Detail)>(update =>
        {
            Dispatcher.Invoke(() =>
            {
                UninstallStatusText.Text = update.Status;
                UninstallDetailText.Text = update.Detail;

                double percentage = Math.Clamp(update.Progress, 0.0, 1.0);
                double trackWidth = 540;
                UninstallProgressFill.Width = trackWidth * percentage;
            });
        });

        try
        {
            await Task.Run(() => _uninstaller.UninstallAsync(_installDir, progress, _cts.Token));
            ProgressPanel.Visibility = Visibility.Collapsed;
            DonePanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Uninstallation encountered an error: {ex.Message}", "River Uninstaller", MessageBoxButton.OK, MessageBoxImage.Error);
            Close();
        }
    }
}
