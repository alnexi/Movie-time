using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;

namespace MovieTime.Installer;

public enum InstallerStep
{
    Welcome,
    Options,
    Installing,
    Finished,
    Error
}

public partial class MainWindow : Window
{
    private readonly InstallationService _installerService = new();
    private InstallationOptions _options = new();
    private CancellationTokenSource? _cts;

    public MainWindow()
    {
        InitializeComponent();
        PathTextBox.Text = _options.InstallPath;
        UpdateDriveSpace(PathTextBox.Text);
        ShowStep(InstallerStep.Welcome);
    }

    private void ShowStep(InstallerStep step)
    {
        WelcomePanel.Visibility = step == InstallerStep.Welcome ? Visibility.Visible : Visibility.Collapsed;
        OptionsPanel.Visibility = step == InstallerStep.Options ? Visibility.Visible : Visibility.Collapsed;
        InstallingPanel.Visibility = step == InstallerStep.Installing ? Visibility.Visible : Visibility.Collapsed;
        FinishedPanel.Visibility = step == InstallerStep.Finished ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility = step == InstallerStep.Error ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        Close();
    }

    private void OnCustomizeClick(object sender, RoutedEventArgs e)
    {
        ShowStep(InstallerStep.Options);
    }

    private void OnBackToWelcomeClick(object sender, RoutedEventArgs e)
    {
        ShowStep(InstallerStep.Welcome);
    }

    private void OnBrowseClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select River Installation Directory",
            InitialDirectory = PathTextBox.Text
        };

        if (dialog.ShowDialog(this) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
        {
            var chosen = dialog.FolderName;
            // If the folder doesn't end with River, suggest River subfolder
            if (!chosen.EndsWith("River", StringComparison.OrdinalIgnoreCase))
            {
                chosen = Path.Combine(chosen, "River");
            }
            PathTextBox.Text = chosen;
            UpdateDriveSpace(chosen);
        }
    }

    private void UpdateDriveSpace(string path)
    {
        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady)
                {
                    double availableGb = Math.Round((double)drive.AvailableFreeSpace / (1024 * 1024 * 1024), 1);
                    SpaceStatsText.Text = $"Space Required: ~310 MB  |  Drive ({drive.Name.TrimEnd('\\')}): {availableGb} GB Available";
                    return;
                }
            }
        }
        catch
        {
            // Fallback
        }
        SpaceStatsText.Text = "Space Required: ~310 MB";
    }

    private async void OnQuickInstallClick(object sender, RoutedEventArgs e)
    {
        _options = new InstallationOptions();
        await RunInstallationAsync();
    }

    private async void OnStartInstallClick(object sender, RoutedEventArgs e)
    {
        _options = new InstallationOptions
        {
            InstallPath = string.IsNullOrWhiteSpace(PathTextBox.Text) ? _options.InstallPath : PathTextBox.Text.Trim(),
            CreateDesktopShortcut = DesktopShortcutCheck.IsChecked ?? true,
            CreateStartMenuShortcut = StartMenuShortcutCheck.IsChecked ?? true,
            RegisterFileAssociations = FileAssociationsCheck.IsChecked ?? true
        };
        await RunInstallationAsync();
    }

    private async Task RunInstallationAsync()
    {
        ShowStep(InstallerStep.Installing);
        _cts = new CancellationTokenSource();

        var progress = new Progress<(double Progress, string Status, string Detail)>(update =>
        {
            Dispatcher.Invoke(() =>
            {
                double percentage = Math.Clamp(update.Progress, 0.0, 1.0);
                PercentageLabel.Text = $"{(int)(percentage * 100)}%";
                StatusLabel.Text = update.Status;
                DetailLabel.Text = update.Detail;

                // Animate progress bar fill width (Total track width is ~500)
                double trackWidth = 580;
                ProgressBarFillGrid.Width = trackWidth * percentage;
            });
        });

        try
        {
            await Task.Run(() => _installerService.InstallAsync(_options, progress, _cts.Token));
            InstalledPathText.Text = $"Installed to: {_options.InstallPath}";
            ShowStep(InstallerStep.Finished);
        }
        catch (OperationCanceledException)
        {
            ShowStep(InstallerStep.Welcome);
        }
        catch (Exception ex)
        {
            ErrorMessageText.Text = "Installation could not be completed.";
            ErrorDetailBox.Text = $"{ex.Message}\n\n{ex.StackTrace}";
            ShowStep(InstallerStep.Error);
        }
    }

    private async void OnRetryClick(object sender, RoutedEventArgs e)
    {
        await RunInstallationAsync();
    }

    private void OnFinishClick(object sender, RoutedEventArgs e)
    {
        if (LaunchAfterCheck.IsChecked == true)
        {
            try
            {
                var mainExe = Path.Combine(_options.InstallPath, "MovieTime.App.exe");
                if (File.Exists(mainExe))
                {
                    Process.Start(new ProcessStartInfo(mainExe) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to launch River: {ex.Message}");
            }
        }

        Close();
    }
}
