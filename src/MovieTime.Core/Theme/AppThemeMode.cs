namespace MovieTime.Core.Theme;

/// <summary>
/// Represents the continuous visual atmosphere mode derived from the celestial slider position.
/// </summary>
public enum AppThemeMode
{
    /// <summary>
    /// Crisp, sunlit Apple-inspired light theme with vibrant emerald tones (Slider in [0.0, 0.35)).
    /// </summary>
    Day = 0,

    /// <summary>
    /// Warm golden-amber sunset dark theme with olive undertones (Slider in [0.35, 0.65]).
    /// </summary>
    Twilight = 1,

    /// <summary>
    /// Signature Movie Time Apple TV obsidian midnight dark theme (Slider in (0.65, 1.0]).
    /// </summary>
    Night = 2
}
