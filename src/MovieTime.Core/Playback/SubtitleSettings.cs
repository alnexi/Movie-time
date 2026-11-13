using System;

namespace MovieTime.Core.Playback;

public enum SubtitleFontSize
{
    Small = 0,
    Medium = 1,
    Large = 2,
    ExtraLarge = 3
}

public enum SubtitleColor
{
    White = 0,
    Yellow = 1,
    Cyan = 2,
    Green = 3
}

public enum SubtitleOutline
{
    Off = 0,
    Thin = 1,
    Medium = 2,
    Thick = 3
}

public enum SubtitleBackground
{
    Off = 0,
    Subtle = 1,
    Dark = 2
}

public enum SubtitlePosition
{
    Default = 0,
    Higher = 1,
    Lower = 2
}

/// <summary>
/// Configuration model for subtitle appearance, styling, and LibVLC rendering parameters.
/// </summary>
public sealed class SubtitleSettings
{
    public SubtitleFontSize FontSize { get; set; } = SubtitleFontSize.Medium;
    public SubtitleColor Color { get; set; } = SubtitleColor.White;
    public SubtitleOutline Outline { get; set; } = SubtitleOutline.Medium;
    public SubtitleBackground Background { get; set; } = SubtitleBackground.Off;
    public SubtitlePosition Position { get; set; } = SubtitlePosition.Default;

    public int GetColorRgb() => Color switch
    {
        SubtitleColor.Yellow => 0x00FFFF00,
        SubtitleColor.Cyan => 0x0000FFFF,
        SubtitleColor.Green => 0x0000FF00,
        _ => 0x00FFFFFF // White
    };

    public string GetColorHex() => Color switch
    {
        SubtitleColor.Yellow => "#FFE500",
        SubtitleColor.Cyan => "#00E5FF",
        SubtitleColor.Green => "#00E676",
        _ => "#FFFFFF"
    };

    public int GetFontSizePoints() => FontSize switch
    {
        SubtitleFontSize.Small => 32,
        SubtitleFontSize.Large => 54,
        SubtitleFontSize.ExtraLarge => 68,
        _ => 42 // Medium default
    };

    public int GetOutlineThickness() => Outline switch
    {
        SubtitleOutline.Off => 0,
        SubtitleOutline.Thin => 1,
        SubtitleOutline.Thick => 3,
        _ => 2 // Medium default
    };

    public int GetBackgroundOpacity() => Background switch
    {
        SubtitleBackground.Subtle => 90,
        SubtitleBackground.Dark => 190,
        _ => 0 // Off
    };

    public int GetSubMarginPixels() => Position switch
    {
        SubtitlePosition.Higher => 60,
        SubtitlePosition.Lower => 15,
        _ => 30 // Default
    };

    public SubtitleSettings Clone()
    {
        return new SubtitleSettings
        {
            FontSize = this.FontSize,
            Color = this.Color,
            Outline = this.Outline,
            Background = this.Background,
            Position = this.Position
        };
    }
}
