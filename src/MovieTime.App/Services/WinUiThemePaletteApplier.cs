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
/// into live XAML Application resources, SolidColorBrushes, and ElementTheme transitions across 0–100%.
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
                var bg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 243, 244, 246),      // 0%  Light Apple Gray #F3F4F6
                    c25: ThemeColor.FromArgb(255, 226, 229, 233),     // 25% Soft Cool Slate #E2E5E9
                    c50: ThemeColor.FromArgb(255, 20, 18, 14),         // 50% Sunset Umber #14120E
                    c75: ThemeColor.FromArgb(255, 14, 14, 18),         // 75% Deep Charcoal #0E0E12
                    c100: ThemeColor.FromArgb(255, 10, 10, 12),        // 100% Obsidian #0A0A0C
                    t: themeValue);

                var card = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 255, 255, 255),     // Pure White
                    c25: ThemeColor.FromArgb(255, 248, 249, 250),    // Soft White
                    c50: ThemeColor.FromArgb(255, 34, 28, 22),       // Warm Umber Card #221C16
                    c75: ThemeColor.FromArgb(255, 24, 24, 29),       // Charcoal Card #18181D
                    c100: ThemeColor.FromArgb(255, 20, 20, 24),      // Deep Slate Card #141418
                    t: themeValue);

                var textPrimary = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 17, 17, 17),        // Dark Text #111111
                    c25: ThemeColor.FromArgb(255, 26, 26, 26),       // Dark Slate #1A1A1A
                    c50: ThemeColor.FromArgb(255, 245, 235, 225),    // Warm Cream #F5EBE1
                    c75: ThemeColor.FromArgb(255, 250, 250, 250),    // Off White #FAFAFA
                    c100: ThemeColor.FromArgb(255, 255, 255, 255),   // Crisp White #FFFFFF
                    t: themeValue);

                var textSecondary = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 102, 102, 102),     // Gray #666666
                    c25: ThemeColor.FromArgb(255, 117, 117, 117),    // Medium Gray #757575
                    c50: ThemeColor.FromArgb(255, 186, 168, 152),    // Warm Tan #BAA898
                    c75: ThemeColor.FromArgb(255, 160, 160, 165),    // Muted Lavender Slate
                    c100: ThemeColor.FromArgb(255, 142, 142, 147),   // Apple Muted #8E8E93
                    t: themeValue);

                var accent = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 46, 139, 87),       // Emerald Green #2E8B57
                    c25: ThemeColor.FromArgb(255, 60, 157, 104),     // Seafoam Accent #3C9D68
                    c50: ThemeColor.FromArgb(255, 224, 154, 53),     // Golden Amber #E09A35
                    c75: ThemeColor.FromArgb(255, 208, 122, 37),     // Sunset Orange #D07A25
                    c100: ThemeColor.FromArgb(255, 255, 255, 255),   // Solid White Accent
                    t: themeValue);

                var border = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(40, 0, 0, 0),             // Subtle Dark Outline
                    c25: ThemeColor.FromArgb(45, 0, 0, 0),            // Soft Border
                    c50: ThemeColor.FromArgb(50, 224, 154, 53),       // Amber Rim Outline
                    c75: ThemeColor.FromArgb(45, 255, 255, 255),      // Translucent White
                    c100: ThemeColor.FromArgb(40, 255, 255, 255),     // Specular White Outline
                    t: themeValue);

                var glassFill = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(235, 255, 255, 255),      // Translucent White Glass
                    c25: ThemeColor.FromArgb(200, 245, 245, 250),     // Cool Glass Fill
                    c50: ThemeColor.FromArgb(55, 34, 28, 22),         // Translucent Amber Umber
                    c75: ThemeColor.FromArgb(45, 25, 25, 30),         // Dusk Glass
                    c100: ThemeColor.FromArgb(35, 255, 255, 255),     // Translucent Liquid Glass
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
