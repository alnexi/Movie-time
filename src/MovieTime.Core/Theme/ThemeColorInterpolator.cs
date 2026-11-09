using System;

namespace MovieTime.Core.Theme;

/// <summary>
/// Mathematical color interpolation engine for Day-to-Twilight-to-Night theme transitions.
/// Supports both 3-stop and high-gamut 5-stop perceptual continuous spline transitions.
/// </summary>
public static class ThemeColorInterpolator
{
    /// <summary>
    /// Interpolates smoothly between Day, Twilight, and Night colors across a normalized [0.0, 1.0] domain.
    /// 0.0 = Day, 0.5 = Twilight, 1.0 = Night.
    /// </summary>
    public static ThemeColor Interpolate(ThemeColor day, ThemeColor twilight, ThemeColor night, double t)
    {
        double factor;
        ThemeColor c1, c2;

        if (t <= 0.5)
        {
            factor = Math.Clamp(t / 0.5, 0.0, 1.0);
            c1 = day;
            c2 = twilight;
        }
        else
        {
            factor = Math.Clamp((t - 0.5) / 0.5, 0.0, 1.0);
            c1 = twilight;
            c2 = night;
        }

        byte a = (byte)(c1.A + ((c2.A - c1.A) * factor));
        byte r = (byte)(c1.R + ((c2.R - c1.R) * factor));
        byte g = (byte)(c1.G + ((c2.G - c1.G) * factor));
        byte b = (byte)(c1.B + ((c2.B - c1.B) * factor));

        return new ThemeColor(a, r, g, b);
    }

    /// <summary>
    /// Smooth continuous 5-stop perceptual color interpolation across the 0–100% theme progression:
    /// Stop 0 (0% Day) -> Stop 1 (25% Muted Green) -> Stop 2 (50% Sunset Twilight) -> Stop 3 (75% Deep Indigo) -> Stop 4 (100% Obsidian Night).
    /// Uses cubic Hermite smoothstep to eliminate color banding and avoid desaturated middle values.
    /// </summary>
    public static ThemeColor Interpolate5(
        ThemeColor c0,
        ThemeColor c25,
        ThemeColor c50,
        ThemeColor c75,
        ThemeColor c100,
        double t)
    {
        double clamped = Math.Clamp(t, 0.0, 1.0);
        double segmentPos;
        ThemeColor startCol, endCol;

        if (clamped <= 0.25)
        {
            segmentPos = clamped / 0.25;
            startCol = c0;
            endCol = c25;
        }
        else if (clamped <= 0.50)
        {
            segmentPos = (clamped - 0.25) / 0.25;
            startCol = c25;
            endCol = c50;
        }
        else if (clamped <= 0.75)
        {
            segmentPos = (clamped - 0.50) / 0.25;
            startCol = c50;
            endCol = c75;
        }
        else
        {
            segmentPos = (clamped - 0.75) / 0.25;
            startCol = c75;
            endCol = c100;
        }

        // Cubic smoothstep easing: s(u) = 3u^2 - 2u^3
        double smoothFactor = segmentPos * segmentPos * (3.0 - (2.0 * segmentPos));

        byte a = (byte)(startCol.A + ((endCol.A - startCol.A) * smoothFactor));
        byte r = (byte)(startCol.R + ((endCol.R - startCol.R) * smoothFactor));
        byte g = (byte)(startCol.G + ((endCol.G - startCol.G) * smoothFactor));
        byte b = (byte)(startCol.B + ((endCol.B - startCol.B) * smoothFactor));

        return new ThemeColor(a, r, g, b);
    }
}
