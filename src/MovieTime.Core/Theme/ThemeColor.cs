using System;

namespace MovieTime.Core.Theme;

/// <summary>
/// Lightweight RGBA color structure for platform-independent palette calculations and interpolation.
/// </summary>
public readonly record struct ThemeColor(byte A, byte R, byte G, byte B)
{
    public static ThemeColor FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);
    public static ThemeColor FromRgb(byte r, byte g, byte b) => new(255, r, g, b);
}
