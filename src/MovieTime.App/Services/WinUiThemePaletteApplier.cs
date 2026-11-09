using System;
using Windows.UI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using MovieTime.App.Common;
using MovieTime.Core.Theme;

namespace MovieTime.App.Services;

/// <summary>
/// WinUI 3 implementation of <see cref="IThemePaletteApplier"/> that translates continuous theme calculations
/// into live XAML Application resources, SolidColorBrushes, and ElementTheme transitions.
/// </summary>
public sealed class WinUiThemePaletteApplier : IThemePaletteApplier
{
    private readonly IDispatcherService? _dispatcher;
    private readonly ILogger<WinUiThemePaletteApplier> _logger;
    private FrameworkElement? _rootElement;

    public WinUiThemePaletteApplier(
        IDispatcherService? dispatcher = null,
        ILogger<WinUiThemePaletteApplier>? logger = null)
    {
        _dispatcher = dispatcher;
        _logger = logger ?? NullLogger<WinUiThemePaletteApplier>.Instance;
    }

    public void AttachRootElement(object rootElement)
    {
        _rootElement = rootElement as FrameworkElement;
    }

    public void ApplyPalette(double themeValue, AppThemeMode mode)
    {
        void ExecuteApply()
        {
            try
            {
                var bg = ThemeColorInterpolator.Interpolate(
                    day: ThemeColor.FromArgb(255, 243, 244, 246),       // Light Apple Gray #F3F4F6
                    twilight: ThemeColor.FromArgb(255, 20, 18, 14),      // Sunset Umber #14120E
                    night: ThemeColor.FromArgb(255, 10, 10, 12),         // Deep Obsidian #0A0A0C
                    t: themeValue);

                var card = ThemeColorInterpolator.Interpolate(
                    day: ThemeColor.FromArgb(255, 255, 255, 255),      // Pure White Card
                    twilight: ThemeColor.FromArgb(255, 34, 28, 22),      // Warm Umber Card #221C16
                    night: ThemeColor.FromArgb(255, 20, 20, 24),         // Deep Slate Card #141418
                    t: themeValue);

                var textPrimary = ThemeColorInterpolator.Interpolate(
                    day: ThemeColor.FromArgb(255, 17, 17, 17),          // Dark Text #111111
                    twilight: ThemeColor.FromArgb(255, 245, 235, 225),   // Warm Cream Text #F5EBE1
                    night: ThemeColor.FromArgb(255, 255, 255, 255),      // Crisp White Text #FFFFFF
                    t: themeValue);

                var textSecondary = ThemeColorInterpolator.Interpolate(
                    day: ThemeColor.FromArgb(255, 102, 102, 102),       // Gray Text #666666
                    twilight: ThemeColor.FromArgb(255, 186, 168, 152),   // Warm Tan Text #BAA898
                    night: ThemeColor.FromArgb(255, 142, 142, 147),      // Apple Muted #8E8E93
                    t: themeValue);

                var accent = ThemeColorInterpolator.Interpolate(
                    day: ThemeColor.FromArgb(255, 46, 139, 87),          // Emerald Green #2E8B57
                    twilight: ThemeColor.FromArgb(255, 224, 154, 53),    // Golden Amber #E09A35
                    night: ThemeColor.FromArgb(255, 255, 255, 255),      // Solid White Accent
                    t: themeValue);

                var border = ThemeColorInterpolator.Interpolate(
                    day: ThemeColor.FromArgb(40, 0, 0, 0),               // Subtle Dark Outline
                    twilight: ThemeColor.FromArgb(50, 224, 154, 53),     // Amber Rim Outline
                    night: ThemeColor.FromArgb(40, 255, 255, 255),       // Specular White Outline
                    t: themeValue);

                var glassFill = ThemeColorInterpolator.Interpolate(
                    day: ThemeColor.FromArgb(235, 255, 255, 255),        // Translucent White Glass
                    twilight: ThemeColor.FromArgb(55, 34, 28, 22),       // Translucent Amber Umber
                    night: ThemeColor.FromArgb(35, 255, 255, 255),       // Translucent Liquid Glass
                    t: themeValue);

                Color bgCol = ToWindowsColor(bg);
                Color cardCol = ToWindowsColor(card);
                Color textPrimaryCol = ToWindowsColor(textPrimary);
                Color textSecondaryCol = ToWindowsColor(textSecondary);
                Color accentCol = ToWindowsColor(accent);
                Color borderCol = ToWindowsColor(border);
                Color glassFillCol = ToWindowsColor(glassFill);

                // Update Application-wide resource brushes
                if (Application.Current != null && Application.Current.Resources != null)
                {
                    var res = Application.Current.Resources;
                    res["AppBackgroundBrush"] = new SolidColorBrush(bgCol);
                    res["AppCardBackgroundBrush"] = new SolidColorBrush(cardCol);
                    res["AppTextPrimaryBrush"] = new SolidColorBrush(textPrimaryCol);
                    res["AppTextSecondaryBrush"] = new SolidColorBrush(textSecondaryCol);
                    res["AppAccentBrush"] = new SolidColorBrush(accentCol);
                    res["AppBorderBrush"] = new SolidColorBrush(borderCol);
                    res["AppGlassFillBrush"] = new SolidColorBrush(glassFillCol);
                }

                // Update root element RequestedTheme and panel background
                if (_rootElement != null)
                {
                    _rootElement.RequestedTheme = themeValue < 0.35 ? ElementTheme.Light : ElementTheme.Dark;
                    if (_rootElement is Panel panel)
                    {
                        panel.Background = new SolidColorBrush(bgCol);
                    }
                    else if (_rootElement is Control ctrl)
                    {
                        ctrl.Background = new SolidColorBrush(bgCol);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to apply dynamic theme resources to WinUI tree.");
            }
        }

        if (_dispatcher != null)
        {
            _dispatcher.RunOnUi(ExecuteApply);
        }
        else
        {
            ExecuteApply();
        }
    }

    private static Color ToWindowsColor(ThemeColor tc) => Color.FromArgb(tc.A, tc.R, tc.G, tc.B);
}
