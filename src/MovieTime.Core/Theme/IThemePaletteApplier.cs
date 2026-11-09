namespace MovieTime.Core.Theme;

/// <summary>
/// Platform-specific abstraction for applying continuous theme palette colors and requested themes to UI resources.
/// </summary>
public interface IThemePaletteApplier
{
    /// <summary>
    /// Applies interpolated colors and theme modes to UI application resources.
    /// </summary>
    void ApplyPalette(double themeValue, AppThemeMode mode);

    /// <summary>
    /// Attaches the root visual element for theme application.
    /// </summary>
    void AttachRootElement(object rootElement);
}
