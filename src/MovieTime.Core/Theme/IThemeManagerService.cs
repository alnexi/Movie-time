using System;
using System.Threading.Tasks;

namespace MovieTime.Core.Theme;

/// <summary>
/// Service coordinating real-time application theme palette interpolation,
/// theme mode transitions, and user accessibility/animation preferences.
/// </summary>
public interface IThemeManagerService
{
    /// <summary>
    /// Gets the current normalized theme value in [0.0, 1.0].
    /// </summary>
    double CurrentThemeValue { get; }

    /// <summary>
    /// Gets the current active theme mode (Day, Twilight, Night).
    /// </summary>
    AppThemeMode CurrentMode { get; }

    /// <summary>
    /// Gets whether reduced motion accessibility preference is active.
    /// </summary>
    bool ReducedMotion { get; }

    /// <summary>
    /// Gets whether ambient idle water animation is active.
    /// </summary>
    bool AmbientAnimationEnabled { get; }

    /// <summary>
    /// Event raised when the theme value or palette changes.
    /// </summary>
    event Action<double, AppThemeMode>? ThemeChanged;

    /// <summary>
    /// Initializes the theme manager, restoring persisted settings from disk and applying them.
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Sets and interpolates the theme value in real time.
    /// </summary>
    void SetThemeValue(double value, bool persist = true);

    /// <summary>
    /// Sets reduced motion preference.
    /// </summary>
    Task SetReducedMotionAsync(bool reducedMotion);

    /// <summary>
    /// Sets ambient animation preference.
    /// </summary>
    Task SetAmbientAnimationAsync(bool ambientEnabled);

    /// <summary>
    /// Attaches the root visual element for theme application.
    /// </summary>
    void AttachRootElement(object rootElement);
}
