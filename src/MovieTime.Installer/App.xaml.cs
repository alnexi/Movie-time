using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace MovieTime.Installer;

public partial class App : Application
{
    private void OnStartup(object sender, StartupEventArgs e)
    {
        var args = e.Args;
        bool isUninstall = args.Any(a => a.Equals("--uninstall", StringComparison.OrdinalIgnoreCase) ||
                                         a.Equals("/uninstall", StringComparison.OrdinalIgnoreCase));
        bool isSilent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("/silent", StringComparison.OrdinalIgnoreCase) ||
                                      a.Equals("/s", StringComparison.OrdinalIgnoreCase));

        if (isUninstall)
        {
            var installDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (isSilent)
            {
                var uninstaller = new UninstallationService();
                var progress = new Progress<(double Progress, string Status, string Detail)>();
                uninstaller.UninstallAsync(installDir, progress, CancellationToken.None).GetAwaiter().GetResult();
                Shutdown(0);
                return;
            }

            var uninstallWindow = new UninstallWindow(installDir);
            uninstallWindow.Show();
        }
        else
        {
            var mainWindow = new MainWindow();
            mainWindow.Show();
        }
    }
}
