using System.Threading.Tasks;

namespace MovieTime.Core.Services;

/// <summary>
/// Abstraction for displaying native file selection dialogs without coupling view models to WinUI windows.
/// </summary>
public interface IFilePickerService
{
    Task<string?> PickOpenFileAsync(string title, string[] fileTypeExtensions);
}
