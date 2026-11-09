using System;

namespace MovieTime.Core.Theme;

/// <summary>
/// Persistent settings model for the Movie Time interactive theme experience.
/// Stores exact normalized theme value in [0.0, 1.0], corresponding 0–100% percentage,
/// continuous boat sinking animation state, and accessibility preferences.
/// </summary>
public sealed class ThemeSettings
{
    /// <summary>
    /// Normalized celestial theme slider value in [0.0, 1.0].
    /// 0.0 = Full Day (0%), 0.5 = Twilight (50%), 1.0 = Full Night (100%).
    /// Default is 1.0 (Cinematic Night / Dark Theme).
    /// </summary>
    public double ThemeValue { get; set; } = 1.0;

    /// <summary>
    /// Gets the exact integer percentage in [0, 100].
    /// </summary>
    public int ThemePercentage => (int)Math.Round(ThemeValue * 100.0);

    /// <summary>
    /// Continuous boat sinking progression parameter in [0.0, 1.0].
    /// 0.0 = boat fully riding high above water; 1.0 = boat deeply submerged in night water.
    /// </summary>
    public double SinkingProgress => ThemeValue;

    /// <summary>
    /// Whether reduced-motion accessibility preference is enabled,
    /// disabling ripple displacement and mouse parallax while preserving instant palette interpolation.
    /// </summary>
    public bool ReducedMotion { get; set; } = false;

    /// <summary>
    /// Whether ambient water shimmer and background animations are enabled when idle.
    /// </summary>
    public bool AmbientAnimationEnabled { get; set; } = true;

    /// <summary>
    /// Computes the corresponding categorical theme mode.
    /// </summary>
    public AppThemeMode Mode => ThemeValue switch
    {
        < 0.35 => AppThemeMode.Day,
        <= 0.65 => AppThemeMode.Twilight,
        _ => AppThemeMode.Night
    };
}
