using System;
using System.Threading.Tasks;

namespace MovieTime.Core.Theme;

/// <summary>
/// Service contract for persisting and restoring theme preferences under %LocalAppData%\MovieTime\Config.
/// </summary>
public interface IThemeSettingsService
{
    /// <summary>
    /// Gets the current active theme settings.
    /// </summary>
    ThemeSettings CurrentSettings { get; }

    /// <summary>
    /// Event raised whenever theme settings are modified or restored.
    /// </summary>
    event Action<ThemeSettings>? SettingsChanged;

    /// <summary>
    /// Loads settings from disk, creating default settings if missing.
    /// </summary>
    Task<ThemeSettings> LoadSettingsAsync();

    /// <summary>
    /// Saves the specified settings to disk asynchronously.
    /// </summary>
    Task SaveSettingsAsync(ThemeSettings settings);

    /// <summary>
    /// Updates and persists the theme slider value with bounds clamping to [0.0, 1.0].
    /// </summary>
    Task UpdateThemeValueAsync(double value);

    /// <summary>
    /// Updates and persists the reduced-motion preference.
    /// </summary>
    Task UpdateReducedMotionAsync(bool reducedMotion);

    /// <summary>
    /// Updates and persists the ambient animation preference.
    /// </summary>
    Task UpdateAmbientAnimationAsync(bool ambientEnabled);
}
