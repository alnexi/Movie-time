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
                // 1. Backgrounds & Surfaces
                var bg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 244, 245, 247),      // 0%  Light Pearl Gray #F4F5F7
                    c25: ThemeColor.FromArgb(255, 228, 231, 235),     // 25% Soft Cool Slate #E4E7EB
                    c50: ThemeColor.FromArgb(255, 30, 27, 22),         // 50% Sunset Umber #1E1B16
                    c75: ThemeColor.FromArgb(255, 18, 18, 22),         // 75% Deep Charcoal #121216
                    c100: ThemeColor.FromArgb(255, 10, 10, 12),        // 100% Original River Dark #0A0A0C
                    t: themeValue);

                var card = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 255, 255, 255),     // 0%  Crisp White Card #FFFFFF
                    c25: ThemeColor.FromArgb(255, 247, 248, 250),    // 25% Soft White Card #F7F8FA
                    c50: ThemeColor.FromArgb(255, 40, 34, 27),       // 50% Warm Umber Card #28221B
                    c75: ThemeColor.FromArgb(255, 26, 26, 32),       // 75% Charcoal Card #1A1A20
                    c100: ThemeColor.FromArgb(255, 20, 20, 24),      // 100% Original Dark Card #141418
                    t: themeValue);

                var surfaceElevated = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 255, 255, 255),     // 0%  Elevated White #FFFFFF
                    c25: ThemeColor.FromArgb(255, 250, 250, 252),    // 25% Elevated Light #FAFAFC
                    c50: ThemeColor.FromArgb(255, 46, 39, 32),       // 50% Elevated Umber #2E2720
                    c75: ThemeColor.FromArgb(255, 30, 30, 38),       // 75% Elevated Charcoal #1E1E26
                    c100: ThemeColor.FromArgb(255, 24, 24, 34),      // 100% Original Elevated #181822
                    t: themeValue);

                var surfaceSubtle = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 234, 236, 239),     // 0%  Subtle Slate #EAECEF
                    c25: ThemeColor.FromArgb(255, 222, 225, 230),    // 25% Subtle Cool Gray #DEE1E6
                    c50: ThemeColor.FromArgb(255, 36, 30, 24),       // 50% Subtle Umber #241E18
                    c75: ThemeColor.FromArgb(255, 24, 24, 32),       // 75% Subtle Charcoal #181820
                    c100: ThemeColor.FromArgb(255, 20, 20, 28),      // 100% Original Subtle #14141C
                    t: themeValue);

                var inputBg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 230, 233, 238),     // 0%  Light Input Fill #E6E9EE
                    c25: ThemeColor.FromArgb(255, 218, 221, 227),    // 25% Soft Slate Fill #DADDE3
                    c50: ThemeColor.FromArgb(255, 37, 32, 26),       // 50% Warm Input Fill #25201A
                    c75: ThemeColor.FromArgb(255, 24, 24, 34),       // 75% Dark Input Fill #181822
                    c100: ThemeColor.FromArgb(255, 28, 28, 38),      // 100% Original Input Fill #1C1C26
                    t: themeValue);

                var pillInactiveBg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 229, 232, 237),     // 0%  Light Inactive Pill #E5E8ED
                    c25: ThemeColor.FromArgb(255, 220, 224, 230),    // 25% Soft Inactive Pill #DCE0E6
                    c50: ThemeColor.FromArgb(255, 48, 39, 32),       // 50% Umber Inactive Pill #302720
                    c75: ThemeColor.FromArgb(255, 32, 32, 40),       // 75% Dark Inactive Pill #202028
                    c100: ThemeColor.FromArgb(96, 24, 24, 32),       // 100% Original Capsule Fill #60181820
                    t: themeValue);

                var pillActiveBg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 17, 17, 20),        // 0%  Charcoal Active Pill #111114
                    c25: ThemeColor.FromArgb(255, 34, 34, 40),       // 25% Dark Slate Active Pill #222228
                    c50: ThemeColor.FromArgb(255, 224, 154, 53),     // 50% Golden Amber Active Pill #E09A35
                    c75: ThemeColor.FromArgb(255, 240, 240, 245),    // 75% Off-White Active Pill #F0F0F5
                    c100: ThemeColor.FromArgb(255, 255, 255, 255),   // 100% Original Crisp White #FFFFFF
                    t: themeValue);

                var pillActiveFg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 255, 255, 255),     // 0%  White Text on Charcoal #FFFFFF
                    c25: ThemeColor.FromArgb(255, 255, 255, 255),    // 25% White Text #FFFFFF
                    c50: ThemeColor.FromArgb(255, 0, 0, 0),          // 50% Black Text on Gold #000000
                    c75: ThemeColor.FromArgb(255, 0, 0, 0),          // 75% Black Text on White #000000
                    c100: ThemeColor.FromArgb(255, 0, 0, 0),         // 100% Original Black Text #000000
                    t: themeValue);

                // 2. Typography
                var textPrimary = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 17, 17, 20),        // 0%  Deep Charcoal #111114
                    c25: ThemeColor.FromArgb(255, 28, 28, 32),       // 25% Dark Charcoal #1C1C20
                    c50: ThemeColor.FromArgb(255, 247, 239, 230),    // 50% Warm Cream #F7EFE6
                    c75: ThemeColor.FromArgb(255, 253, 253, 253),    // 75% Bright White #FDFDFD
                    c100: ThemeColor.FromArgb(255, 255, 255, 255),   // 100% Original Crisp White #FFFFFF
                    t: themeValue);

                var textSecondary = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 92, 96, 104),       // 0%  Balanced Slate #5C6068
                    c25: ThemeColor.FromArgb(255, 110, 114, 122),    // 25% Slate Gray #6E727A
                    c50: ThemeColor.FromArgb(255, 200, 184, 168),    // 50% Warm Muted Tan #C8B8A8
                    c75: ThemeColor.FromArgb(255, 154, 154, 164),    // 75% Lavender Slate #9A9AA4
                    c100: ThemeColor.FromArgb(255, 142, 142, 147),   // 100% Original Muted Slate #8E8E93
                    t: themeValue);

                var textTertiary = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 140, 144, 152),     // 0%  Subtle Hint #8C9098
                    c25: ThemeColor.FromArgb(255, 150, 154, 162),    // 25% Soft Hint #969AA2
                    c50: ThemeColor.FromArgb(255, 160, 144, 128),    // 50% Tan Hint #A09080
                    c75: ThemeColor.FromArgb(255, 120, 120, 132),    // 75% Muted Hint #787884
                    c100: ThemeColor.FromArgb(255, 110, 110, 120),   // 100% Original Subtle #6E6E78
                    t: themeValue);

                var textDisabled = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 176, 180, 188),     // 0%  Disabled Light #B0B4BC
                    c25: ThemeColor.FromArgb(255, 184, 188, 196),    // 25% Disabled Cool Gray #B8BCC4
                    c50: ThemeColor.FromArgb(255, 96, 88, 80),       // 50% Disabled Umber #605850
                    c75: ThemeColor.FromArgb(255, 80, 80, 90),       // 75% Disabled Charcoal #50505A
                    c100: ThemeColor.FromArgb(255, 74, 74, 82),      // 100% Original Disabled #4A4A52
                    t: themeValue);

                // 3. Accents & Interactive Actions
                var accent = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 17, 17, 20),        // 0%  Charcoal Monochrome Accent #111114
                    c25: ThemeColor.FromArgb(255, 32, 32, 40),       // 25% Dark Accent #202028
                    c50: ThemeColor.FromArgb(255, 224, 154, 53),     // 50% Golden Amber Accent #E09A35
                    c75: ThemeColor.FromArgb(255, 232, 232, 238),    // 75% Soft White Accent #E8E8EE
                    c100: ThemeColor.FromArgb(255, 255, 255, 255),   // 100% Original White Accent #FFFFFF
                    t: themeValue);

                var accentSecondary = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 74, 80, 92),        // 0%  Muted Accent #4A505C
                    c25: ThemeColor.FromArgb(255, 88, 94, 106),      // 25% Muted Accent #585E6A
                    c50: ThemeColor.FromArgb(255, 208, 122, 37),     // 50% Sunset Orange #D07A25
                    c75: ThemeColor.FromArgb(255, 160, 160, 176),    // 75% Lavender Slate #A0A0B0
                    c100: ThemeColor.FromArgb(255, 142, 142, 147),   // 100% Original Accent Secondary #8E8E93
                    t: themeValue);

                var buttonPrimaryBg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 17, 17, 20),        // 0%  Charcoal Button #111114
                    c25: ThemeColor.FromArgb(255, 28, 28, 32),       // 25% Dark Button #1C1C20
                    c50: ThemeColor.FromArgb(255, 224, 154, 53),     // 50% Amber Button #E09A35
                    c75: ThemeColor.FromArgb(255, 255, 255, 255),    // 75% White Button #FFFFFF
                    c100: ThemeColor.FromArgb(255, 255, 255, 255),   // 100% Original White Button #FFFFFF
                    t: themeValue);

                var buttonPrimaryFg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 255, 255, 255),     // 0%  White Text on Button #FFFFFF
                    c25: ThemeColor.FromArgb(255, 255, 255, 255),    // 25% White Text #FFFFFF
                    c50: ThemeColor.FromArgb(255, 0, 0, 0),          // 50% Black Text #000000
                    c75: ThemeColor.FromArgb(255, 0, 0, 0),          // 75% Black Text #000000
                    c100: ThemeColor.FromArgb(255, 0, 0, 0),         // 100% Original Black Text #000000
                    t: themeValue);

                var buttonSecondaryBg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 230, 233, 238),     // 0%  Light Button Fill #E6E9EE
                    c25: ThemeColor.FromArgb(255, 218, 221, 227),    // 25% Slate Button Fill #DADDE3
                    c50: ThemeColor.FromArgb(64, 224, 154, 53),      // 50% Translucent Amber #40E09A35
                    c75: ThemeColor.FromArgb(37, 255, 255, 255),     // 75% Translucent White #25FFFFFF
                    c100: ThemeColor.FromArgb(32, 255, 255, 255),    // 100% Original Translucent Button #20FFFFFF
                    t: themeValue);

                var buttonSecondaryFg = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(255, 17, 17, 20),        // 0%  Dark Text on Secondary #111114
                    c25: ThemeColor.FromArgb(255, 28, 28, 32),       // 25% Dark Text #1C1C20
                    c50: ThemeColor.FromArgb(255, 247, 239, 230),    // 50% Cream Text #F7EFE6
                    c75: ThemeColor.FromArgb(255, 255, 255, 255),    // 75% White Text #FFFFFF
                    c100: ThemeColor.FromArgb(255, 255, 255, 255),   // 100% Original White Text #FFFFFF
                    t: themeValue);

                // 4. Borders & Dividers
                var border = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(24, 0, 0, 0),            // 0%  Subtle Dark Outline #18000000
                    c25: ThemeColor.FromArgb(30, 0, 0, 0),           // 25% Soft Border #1E000000
                    c50: ThemeColor.FromArgb(53, 224, 154, 53),      // 50% Amber Rim Outline #35E09A35
                    c75: ThemeColor.FromArgb(37, 255, 255, 255),     // 75% Translucent White #25FFFFFF
                    c100: ThemeColor.FromArgb(32, 255, 255, 255),    // 100% Original White Outline #20FFFFFF
                    t: themeValue);

                var borderSubtle = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(13, 0, 0, 0),            // 0%  Very Soft Outline #0D000000
                    c25: ThemeColor.FromArgb(18, 0, 0, 0),           // 25% Soft Border #12000000
                    c50: ThemeColor.FromArgb(32, 224, 154, 53),      // 50% Amber Subtle Outline #20E09A35
                    c75: ThemeColor.FromArgb(24, 255, 255, 255),     // 75% Subtle White #18FFFFFF
                    c100: ThemeColor.FromArgb(18, 255, 255, 255),    // 100% Original Subtle Outline #12FFFFFF
                    t: themeValue);

                var glassFill = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(230, 255, 255, 255),     // 0%  Translucent White Glass #E5FFFFFF
                    c25: ThemeColor.FromArgb(216, 245, 245, 250),    // 25% Cool Glass Fill #D8F5F5FA
                    c50: ThemeColor.FromArgb(64, 40, 34, 27),        // 50% Translucent Amber Umber #4028221B
                    c75: ThemeColor.FromArgb(48, 24, 24, 34),        // 75% Dusk Glass #30181822
                    c100: ThemeColor.FromArgb(32, 255, 255, 255),    // 100% Original Liquid Glass #20FFFFFF
                    t: themeValue);

                var overlay = ThemeColorInterpolator.Interpolate5(
                    c0: ThemeColor.FromArgb(64, 0, 0, 0),            // 0%  Soft Overlay #40000000
                    c25: ThemeColor.FromArgb(80, 0, 0, 0),           // 25% Light Overlay #50000000
                    c50: ThemeColor.FromArgb(112, 0, 0, 0),          // 50% Twilight Overlay #70000000
                    c75: ThemeColor.FromArgb(128, 0, 0, 0),          // 75% Dusk Overlay #80000000
                    c100: ThemeColor.FromArgb(144, 0, 0, 0),         // 100% Original Dark Overlay #90000000
                    t: themeValue);

                Color bgCol = ToWindowsColor(bg);
                Color cardCol = ToWindowsColor(card);
                Color surfaceElevatedCol = ToWindowsColor(surfaceElevated);
                Color surfaceSubtleCol = ToWindowsColor(surfaceSubtle);
                Color inputBgCol = ToWindowsColor(inputBg);
                Color pillInactiveBgCol = ToWindowsColor(pillInactiveBg);
                Color pillActiveBgCol = ToWindowsColor(pillActiveBg);
                Color pillActiveFgCol = ToWindowsColor(pillActiveFg);

                Color textPrimaryCol = ToWindowsColor(textPrimary);
                Color textSecondaryCol = ToWindowsColor(textSecondary);
                Color textTertiaryCol = ToWindowsColor(textTertiary);
                Color textDisabledCol = ToWindowsColor(textDisabled);

                Color accentCol = ToWindowsColor(accent);
                Color accentSecondaryCol = ToWindowsColor(accentSecondary);
                Color buttonPrimaryBgCol = ToWindowsColor(buttonPrimaryBg);
                Color buttonPrimaryFgCol = ToWindowsColor(buttonPrimaryFg);
                Color buttonSecondaryBgCol = ToWindowsColor(buttonSecondaryBg);
                Color buttonSecondaryFgCol = ToWindowsColor(buttonSecondaryFg);

                Color borderCol = ToWindowsColor(border);
                Color borderSubtleCol = ToWindowsColor(borderSubtle);
                Color glassFillCol = ToWindowsColor(glassFill);
                Color overlayCol = ToWindowsColor(overlay);

                // Update Application-wide resource brushes (in-place color mutation for live 60 FPS performance)
                if (Application.Current != null && Application.Current.Resources != null)
                {
                    var res = Application.Current.Resources;

                    SetOrUpdateBrush(res, "AppBackgroundBrush", bgCol);
                    SetOrUpdateBrush(res, "AppCardBackgroundBrush", cardCol);
                    SetOrUpdateBrush(res, "AppSurfaceElevatedBrush", surfaceElevatedCol);
                    SetOrUpdateBrush(res, "AppSurfaceSubtleBrush", surfaceSubtleCol);
                    SetOrUpdateBrush(res, "AppInputBackgroundBrush", inputBgCol);
                    SetOrUpdateBrush(res, "AppPillInactiveBackgroundBrush", pillInactiveBgCol);
                    SetOrUpdateBrush(res, "AppPillActiveBackgroundBrush", pillActiveBgCol);
                    SetOrUpdateBrush(res, "AppPillActiveForegroundBrush", pillActiveFgCol);

                    SetOrUpdateBrush(res, "AppTextPrimaryBrush", textPrimaryCol);
                    SetOrUpdateBrush(res, "AppTextSecondaryBrush", textSecondaryCol);
                    SetOrUpdateBrush(res, "AppTextTertiaryBrush", textTertiaryCol);
                    SetOrUpdateBrush(res, "AppTextDisabledBrush", textDisabledCol);

                    SetOrUpdateBrush(res, "AppAccentBrush", accentCol);
                    SetOrUpdateBrush(res, "AppAccentSecondaryBrush", accentSecondaryCol);
                    SetOrUpdateBrush(res, "AppButtonPrimaryBackgroundBrush", buttonPrimaryBgCol);
                    SetOrUpdateBrush(res, "AppButtonPrimaryForegroundBrush", buttonPrimaryFgCol);
                    SetOrUpdateBrush(res, "AppButtonSecondaryBackgroundBrush", buttonSecondaryBgCol);
                    SetOrUpdateBrush(res, "AppButtonSecondaryForegroundBrush", buttonSecondaryFgCol);

                    SetOrUpdateBrush(res, "AppBorderBrush", borderCol);
                    SetOrUpdateBrush(res, "AppBorderSubtleBrush", borderSubtleCol);
                    SetOrUpdateBrush(res, "AppGlassFillBrush", glassFillCol);
                    SetOrUpdateBrush(res, "AppOverlayBrush", overlayCol);

                    // 5. Dynamic Vignette Gradients for Movie/Series Details and Hero Banner
                    // Bottom Vignette: Fades smoothly into the current theme's content background
                    SetOrUpdateGradientBrush(
                        res,
                        "AppBackdropVignetteBottomBrush",
                        new Windows.Foundation.Point(0, 0),
                        new Windows.Foundation.Point(0, 1),
                        new (double, Color)[]
                        {
                            (0.0, Color.FromArgb(0, bgCol.R, bgCol.G, bgCol.B)),
                            (0.50, Color.FromArgb(0, bgCol.R, bgCol.G, bgCol.B)),
                            (0.85, Color.FromArgb(176, bgCol.R, bgCol.G, bgCol.B)),
                            (1.0, Color.FromArgb(255, bgCol.R, bgCol.G, bgCol.B))
                        });

                    // Left Vignette: Guarantees title readability by fading from current theme's background
                    SetOrUpdateGradientBrush(
                        res,
                        "AppBackdropVignetteLeftBrush",
                        new Windows.Foundation.Point(0, 0),
                        new Windows.Foundation.Point(1, 0),
                        new (double, Color)[]
                        {
                            (0.0, Color.FromArgb(240, bgCol.R, bgCol.G, bgCol.B)),
                            (0.45, Color.FromArgb(184, bgCol.R, bgCol.G, bgCol.B)),
                            (0.80, Color.FromArgb(48, bgCol.R, bgCol.G, bgCol.B)),
                            (1.0, Color.FromArgb(0, bgCol.R, bgCol.G, bgCol.B))
                        });
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

    private static void SetOrUpdateBrush(ResourceDictionary res, string key, Color color)
    {
        if (res.TryGetValue(key, out var existing) && existing is SolidColorBrush scb)
        {
            scb.Color = color;
        }
        else
        {
            res[key] = new SolidColorBrush(color);
        }
    }

    private static void SetOrUpdateGradientBrush(
        ResourceDictionary res,
        string key,
        Windows.Foundation.Point startPoint,
        Windows.Foundation.Point endPoint,
        (double offset, Color color)[] stops)
    {
        if (res.TryGetValue(key, out var existing) && existing is LinearGradientBrush lgb && lgb.GradientStops.Count == stops.Length)
        {
            for (int i = 0; i < stops.Length; i++)
            {
                lgb.GradientStops[i].Offset = stops[i].offset;
                lgb.GradientStops[i].Color = stops[i].color;
            }
        }
        else
        {
            var newLgb = new LinearGradientBrush { StartPoint = startPoint, EndPoint = endPoint };
            foreach (var (offset, color) in stops)
            {
                newLgb.GradientStops.Add(new GradientStop { Offset = offset, Color = color });
            }
            res[key] = newLgb;
        }
    }

    private static Color ToWindowsColor(ThemeColor tc) => Color.FromArgb(tc.A, tc.R, tc.G, tc.B);
}
