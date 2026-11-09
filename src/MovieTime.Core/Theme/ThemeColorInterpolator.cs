using System;

namespace MovieTime.Core.Theme;

/// <summary>
/// Mathematical color interpolation engine for Day-to-Twilight-to-Night theme transitions.
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
}
