using System;
using System.IO;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Services;

namespace MovieTime.App.Services;

/// <summary>
/// Native WinUI 3 implementation of <see cref="IFilePickerService"/> using Windows App SDK <see cref="FileOpenPicker"/>.
/// Safely initializes with the active application window HWND for desktop compatibility.
/// </summary>
public sealed class WinUiFilePickerService : IFilePickerService
{
    private readonly ILogger<WinUiFilePickerService> _logger;

    public WinUiFilePickerService(ILogger<WinUiFilePickerService>? logger = null)
    {
        _logger = logger ?? NullLogger<WinUiFilePickerService>.Instance;
    }

    public async Task<string?> PickOpenFileAsync(string title, string[] fileTypeExtensions)
    {
        try
        {
            var picker = new FileOpenPicker
            {
                ViewMode = PickerViewMode.List,
                SuggestedStartLocation = PickerLocationId.ComputerFolder
            };

            foreach (var ext in fileTypeExtensions)
            {
                var clean = ext.StartsWith('.') ? ext : $".{ext}";
                picker.FileTypeFilter.Add(clean);
            }

            var window = App.Current.MainWindow;
            if (window != null)
            {
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);
            }

            var file = await picker.PickSingleFileAsync();
            if (file != null && File.Exists(file.Path))
            {
                _logger.LogInformation("FileOpenPicker selected file: {Path}", file.Path);
                return file.Path;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to display FileOpenPicker for extensions: {Extensions}", string.Join(", ", fileTypeExtensions));
        }

        return null;
    }
}
