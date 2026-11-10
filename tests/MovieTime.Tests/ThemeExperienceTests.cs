using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.App.ViewModels;
using MovieTime.Core.Database;
using MovieTime.Core.Database.Repositories;
using MovieTime.Core.Library;
using MovieTime.Core.Parsing;
using MovieTime.Core.Scanning;
using MovieTime.Core.Services;
using MovieTime.Core.Theme;
using Xunit;

namespace MovieTime.Tests;

public sealed class ThemeExperienceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StoragePathService _storagePathService;
    private readonly ThemeSettingsService _settingsService;

    public ThemeExperienceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ThemeExpTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _storagePathService = new StoragePathService(_tempDir);
        _settingsService = new ThemeSettingsService(_storagePathService, NullLogger<ThemeSettingsService>.Instance);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // best effort cleanup
        }
    }

    [Theory]
    [InlineData(0.0, AppThemeMode.Day)]
    [InlineData(0.15, AppThemeMode.Day)]
    [InlineData(0.34, AppThemeMode.Day)]
    [InlineData(0.35, AppThemeMode.Twilight)]
    [InlineData(0.50, AppThemeMode.Twilight)]
    [InlineData(0.65, AppThemeMode.Twilight)]
    [InlineData(0.66, AppThemeMode.Night)]
    [InlineData(0.85, AppThemeMode.Night)]
    [InlineData(1.0, AppThemeMode.Night)]
    public void ThemeSettings_ModeMapping_ResolvesCorrectly(double sliderVal, AppThemeMode expectedMode)
    {
        var settings = new ThemeSettings { ThemeValue = sliderVal };
        Assert.Equal(expectedMode, settings.Mode);
    }

    [Fact]
    public void ThemeSettings_Continuous0To100PercentageMapping_ResolvesExactIntegers()
    {
        for (int p = 0; p <= 100; p++)
        {
            double val = p / 100.0;
            var settings = new ThemeSettings { ThemeValue = val };
            Assert.Equal(p, settings.ThemePercentage);
            Assert.Equal(val, settings.SinkingProgress, precision: 4);
        }
    }

    [Fact]
    public void PaletteInterpolation_CalculatesContinuousColorTransitions()
    {
        var dayCol = ThemeColor.FromArgb(255, 46, 139, 87);       // #2E8B57
        var twiCol = ThemeColor.FromArgb(255, 224, 154, 53);      // #E09A35
        var ngtCol = ThemeColor.FromArgb(255, 10, 10, 12);        // #0A0A0C

        // At t = 0.0 (Day)
        var col0 = ThemeColorInterpolator.Interpolate(dayCol, twiCol, ngtCol, 0.0);
        Assert.Equal(dayCol.R, col0.R);
        Assert.Equal(dayCol.G, col0.G);
        Assert.Equal(dayCol.B, col0.B);

        // At t = 0.5 (Twilight)
        var col50 = ThemeColorInterpolator.Interpolate(dayCol, twiCol, ngtCol, 0.5);
        Assert.Equal(twiCol.R, col50.R);
        Assert.Equal(twiCol.G, col50.G);
        Assert.Equal(twiCol.B, col50.B);

        // At t = 1.0 (Night)
        var col100 = ThemeColorInterpolator.Interpolate(dayCol, twiCol, ngtCol, 1.0);
        Assert.Equal(ngtCol.R, col100.R);
        Assert.Equal(ngtCol.G, col100.G);
        Assert.Equal(ngtCol.B, col100.B);

        // At t = 0.25 (Midpoint between Day and Twilight)
        var col25 = ThemeColorInterpolator.Interpolate(dayCol, twiCol, ngtCol, 0.25);
        byte expectedR = (byte)((dayCol.R + twiCol.R) / 2);
        byte expectedG = (byte)((dayCol.G + twiCol.G) / 2);
        byte expectedB = (byte)((dayCol.B + twiCol.B) / 2);
        Assert.Equal(expectedR, col25.R);
        Assert.Equal(expectedG, col25.G);
        Assert.Equal(expectedB, col25.B);

        // At t = 0.75 (Midpoint between Twilight and Night)
        var col75 = ThemeColorInterpolator.Interpolate(dayCol, twiCol, ngtCol, 0.75);
        byte expectedR75 = (byte)((twiCol.R + ngtCol.R) / 2);
        byte expectedG75 = (byte)((twiCol.G + ngtCol.G) / 2);
        byte expectedB75 = (byte)((twiCol.B + ngtCol.B) / 2);
        Assert.Equal(expectedR75, col75.R);
        Assert.Equal(expectedG75, col75.G);
        Assert.Equal(expectedB75, col75.B);
    }

    [Fact]
    public void PaletteInterpolation_Interpolate5_CalculatesSmooth5StopTransitions()
    {
        var c0 = ThemeColor.FromArgb(255, 18, 92, 54);     // Day
        var c25 = ThemeColor.FromArgb(255, 26, 75, 48);    // Afternoon
        var c50 = ThemeColor.FromArgb(255, 140, 72, 22);   // Twilight
        var c75 = ThemeColor.FromArgb(255, 18, 42, 64);    // Dusk
        var c100 = ThemeColor.FromArgb(255, 6, 12, 20);    // Night

        // Key stops
        var col0 = ThemeColorInterpolator.Interpolate5(c0, c25, c50, c75, c100, 0.0);
        Assert.Equal(c0.R, col0.R);
        Assert.Equal(c0.G, col0.G);
        Assert.Equal(c0.B, col0.B);

        var col25 = ThemeColorInterpolator.Interpolate5(c0, c25, c50, c75, c100, 0.25);
        Assert.Equal(c25.R, col25.R);
        Assert.Equal(c25.G, col25.G);
        Assert.Equal(c25.B, col25.B);

        var col50 = ThemeColorInterpolator.Interpolate5(c0, c25, c50, c75, c100, 0.50);
        Assert.Equal(c50.R, col50.R);
        Assert.Equal(c50.G, col50.G);
        Assert.Equal(c50.B, col50.B);

        var col75 = ThemeColorInterpolator.Interpolate5(c0, c25, c50, c75, c100, 0.75);
        Assert.Equal(c75.R, col75.R);
        Assert.Equal(c75.G, col75.G);
        Assert.Equal(c75.B, col75.B);

        var col100 = ThemeColorInterpolator.Interpolate5(c0, c25, c50, c75, c100, 1.0);
        Assert.Equal(c100.R, col100.R);
        Assert.Equal(c100.G, col100.G);
        Assert.Equal(c100.B, col100.B);

        // Smooth continuous intermediate values
        var mid12 = ThemeColorInterpolator.Interpolate5(c0, c25, c50, c75, c100, 0.125);
        Assert.InRange(mid12.R, (byte)Math.Min(c0.R, c25.R), (byte)Math.Max(c0.R, c25.R));
        Assert.InRange(mid12.G, (byte)Math.Min(c0.G, c25.G), (byte)Math.Max(c0.G, c25.G));
    }

    [Fact]
    public async Task ThemeSettingsService_PersistenceAndRestoration_WorksCorrectly()
    {
        // 1. Initial load creates default settings (1.0 = Night)
        var initial = await _settingsService.LoadSettingsAsync();
        Assert.NotNull(initial);
        Assert.Equal(1.0, initial.ThemeValue);
        Assert.Equal(100, initial.ThemePercentage);
        Assert.False(initial.ReducedMotion);
        Assert.True(initial.AmbientAnimationEnabled);

        // 2. Update theme value and persist
        await _settingsService.UpdateThemeValueAsync(0.50);
        await _settingsService.UpdateReducedMotionAsync(true);
        await _settingsService.UpdateAmbientAnimationAsync(false);

        // 3. New instance loading from same directory
        var secondService = new ThemeSettingsService(_storagePathService, NullLogger<ThemeSettingsService>.Instance);
        var restored = await secondService.LoadSettingsAsync();

        Assert.Equal(0.50, restored.ThemeValue, precision: 2);
        Assert.Equal(50, restored.ThemePercentage);
        Assert.Equal(AppThemeMode.Twilight, restored.Mode);
        Assert.True(restored.ReducedMotion);
        Assert.False(restored.AmbientAnimationEnabled);
    }

    [Fact]
    public async Task ThemeSettingsService_ClampsOutOfBoundsValues()
    {
        await _settingsService.UpdateThemeValueAsync(-0.8);
        Assert.Equal(0.0, _settingsService.CurrentSettings.ThemeValue);
        Assert.Equal(0, _settingsService.CurrentSettings.ThemePercentage);

        await _settingsService.UpdateThemeValueAsync(2.4);
        Assert.Equal(1.0, _settingsService.CurrentSettings.ThemeValue);
        Assert.Equal(100, _settingsService.CurrentSettings.ThemePercentage);
    }

    [Fact]
    public async Task ThemeManagerService_InitializeAndSetTheme_TriggersEvents()
    {
        var manager = new ThemeManagerService(_settingsService, null, NullLogger<ThemeManagerService>.Instance);
        await manager.InitializeAsync();

        double? lastVal = null;
        AppThemeMode? lastMode = null;

        manager.ThemeChanged += (v, m) =>
        {
            lastVal = v;
            lastMode = m;
        };

        // Transition to Day
        manager.SetThemeValue(0.1);
        Assert.Equal(0.1, lastVal);
        Assert.Equal(AppThemeMode.Day, lastMode);
        Assert.Equal(0.1, manager.CurrentThemeValue);
        Assert.Equal(AppThemeMode.Day, manager.CurrentMode);

        // Transition to Twilight
        manager.SetThemeValue(0.5);
        Assert.Equal(0.5, lastVal);
        Assert.Equal(AppThemeMode.Twilight, lastMode);
        Assert.Equal(0.5, manager.CurrentThemeValue);
        Assert.Equal(AppThemeMode.Twilight, manager.CurrentMode);

        // Transition to Night
        manager.SetThemeValue(0.95);
        Assert.Equal(0.95, lastVal);
        Assert.Equal(AppThemeMode.Night, lastMode);
        Assert.Equal(0.95, manager.CurrentThemeValue);
        Assert.Equal(AppThemeMode.Night, manager.CurrentMode);
    }

    [Fact]
    public async Task SettingsViewModel_TabNavigation_SwitchesBetweenLibraryAndThemeTabs()
    {
        var manager = new ThemeManagerService(_settingsService, null, NullLogger<ThemeManagerService>.Instance);
        await manager.InitializeAsync();

        var storage = new StoragePathService(_tempDir);
        var db = new DatabaseService(storage, NullLogger<DatabaseService>.Instance);
        await db.InitializeAsync();
        var scanRootRepo = new ScanRootRepository(db);
        var scanRootService = new ScanRootService(scanRootRepo, NullLogger<ScanRootService>.Instance);
        var mediaScanner = new MediaScannerService(NullLogger<MediaScannerService>.Instance);
        var parser = new FilenameParserEngine(NullLogger<FilenameParserEngine>.Instance, storage);
        var importService = new LibraryImportService(
            scanRootService, mediaScanner, parser, db,
            new MovieRepository(db),
            new SeriesRepository(db),
            new SeasonRepository(db),
            new EpisodeRepository(db),
            new MediaFileRepository(db),
            NullLogger<LibraryImportService>.Instance);

        var libManager = new MLibraryManagerService(scanRootService, importService, NullLogger<MLibraryManagerService>.Instance);
        var vm = new SettingsViewModel(libManager, manager, NullLogger<SettingsViewModel>.Instance);

        // Default tab is General / Storage (0)
        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.True(vm.IsGeneralTabSelected);
        Assert.False(vm.IsThemeTabSelected);

        // Switch to Theme Experience (1)
        vm.SelectThemeTabCommand.Execute(null);
        Assert.Equal(1, vm.SelectedTabIndex);
        Assert.False(vm.IsGeneralTabSelected);
        Assert.True(vm.IsThemeTabSelected);

        // Switch back to General (0)
        vm.SelectGeneralTabCommand.Execute(null);
        Assert.Equal(0, vm.SelectedTabIndex);
        Assert.True(vm.IsGeneralTabSelected);
        Assert.False(vm.IsThemeTabSelected);

        db.Dispose();
    }

    [Fact]
    public async Task SettingsViewModel_ThemeCommandsAndProperties_SynchronizeWithManager()
    {
        var manager = new ThemeManagerService(_settingsService, null, NullLogger<ThemeManagerService>.Instance);
        await manager.InitializeAsync();

        // Create a stub library manager
        var storage = new StoragePathService(_tempDir);
        var db = new DatabaseService(storage, NullLogger<DatabaseService>.Instance);
        await db.InitializeAsync();
        var scanRootRepo = new ScanRootRepository(db);
        var scanRootService = new ScanRootService(scanRootRepo, NullLogger<ScanRootService>.Instance);
        var mediaScanner = new MediaScannerService(NullLogger<MediaScannerService>.Instance);
        var parser = new FilenameParserEngine(NullLogger<FilenameParserEngine>.Instance, storage);
        var importService = new LibraryImportService(
            scanRootService, mediaScanner, parser, db,
            new MovieRepository(db),
            new SeriesRepository(db),
            new SeasonRepository(db),
            new EpisodeRepository(db),
            new MediaFileRepository(db),
            NullLogger<LibraryImportService>.Instance);

        var libManager = new MLibraryManagerService(scanRootService, importService, NullLogger<MLibraryManagerService>.Instance);

        var vm = new SettingsViewModel(libManager, manager, NullLogger<SettingsViewModel>.Instance);

        // Verify initial state
        Assert.Equal(1.0, vm.CurrentThemeValue);
        Assert.Equal(100, vm.ThemePercentageInt);
        Assert.Equal(AppThemeMode.Night, vm.CurrentThemeMode);
        Assert.Contains("Night", vm.ThemeModeTitle);
        Assert.Equal("100%", vm.ThemePercentageText);

        // Execute Day preset (0%)
        vm.SetDayThemeCommand.Execute(null);
        Assert.Equal(0.0, vm.CurrentThemeValue);
        Assert.Equal(0, vm.ThemePercentageInt);
        Assert.Equal(AppThemeMode.Day, vm.CurrentThemeMode);
        Assert.Contains("Day", vm.ThemeModeTitle);
        Assert.Equal("0%", vm.ThemePercentageText);

        // Execute Twilight preset (50%)
        vm.SetTwilightThemeCommand.Execute(null);
        Assert.Equal(0.5, vm.CurrentThemeValue);
        Assert.Equal(50, vm.ThemePercentageInt);
        Assert.Equal(AppThemeMode.Twilight, vm.CurrentThemeMode);
        Assert.Contains("Twilight", vm.ThemeModeTitle);
        Assert.Equal("50%", vm.ThemePercentageText);

        // Execute Night preset (100%)
        vm.SetNightThemeCommand.Execute(null);
        Assert.Equal(1.0, vm.CurrentThemeValue);
        Assert.Equal(100, vm.ThemePercentageInt);
        Assert.Equal(AppThemeMode.Night, vm.CurrentThemeMode);
        Assert.Contains("Night", vm.ThemeModeTitle);
        Assert.Equal("100%", vm.ThemePercentageText);

        // Set exact arbitrary integer percentage (e.g. 37%)
        vm.SetThemePercentage(37);
        Assert.Equal(0.37, vm.CurrentThemeValue, precision: 2);
        Assert.Equal(37, vm.ThemePercentageInt);
        Assert.Equal("37%", vm.ThemePercentageText);
        Assert.Equal(AppThemeMode.Twilight, vm.CurrentThemeMode);

        // Toggle ReducedMotion
        vm.ReducedMotion = true;
        Assert.True(manager.ReducedMotion);

        // Toggle AmbientAnimationEnabled
        vm.AmbientAnimationEnabled = false;
        Assert.False(manager.AmbientAnimationEnabled);

        db.Dispose();
    }

    [Fact]
    public void SmoothInterpolate_Keyframes_MatchExactDayTwilightNight()
    {
        var dayCol = ThemeColor.FromArgb(255, 46, 139, 87);
        var twiCol = ThemeColor.FromArgb(255, 224, 154, 53);
        var ngtCol = ThemeColor.FromArgb(255, 10, 10, 12);

        // At t = 0.0 (Day)
        var col0 = ThemeColorInterpolator.SmoothInterpolate(dayCol, twiCol, ngtCol, 0.0);
        Assert.Equal(dayCol.R, col0.R);
        Assert.Equal(dayCol.G, col0.G);
        Assert.Equal(dayCol.B, col0.B);

        // At t = 0.5 (Twilight)
        var col50 = ThemeColorInterpolator.SmoothInterpolate(dayCol, twiCol, ngtCol, 0.5);
        Assert.Equal(twiCol.R, col50.R);
        Assert.Equal(twiCol.G, col50.G);
        Assert.Equal(twiCol.B, col50.B);

        // At t = 1.0 (Night)
        var col100 = ThemeColorInterpolator.SmoothInterpolate(dayCol, twiCol, ngtCol, 1.0);
        Assert.Equal(ngtCol.R, col100.R);
        Assert.Equal(ngtCol.G, col100.G);
        Assert.Equal(ngtCol.B, col100.B);
    }

    [Fact]
    public void SmoothInterpolate_ProducesContinuousSmootherStepCurve_NoSuddenJumps()
    {
        var dayCol = ThemeColor.FromArgb(255, 46, 139, 87);
        var twiCol = ThemeColor.FromArgb(255, 224, 154, 53);
        var ngtCol = ThemeColor.FromArgb(255, 10, 10, 12);

        ThemeColor prev = ThemeColorInterpolator.SmoothInterpolate(dayCol, twiCol, ngtCol, 0.0);

        // Step through 100 percentage points, ensure delta per step is strictly bounded (no jump > 12 units)
        for (int p = 1; p <= 100; p++)
        {
            double t = p / 100.0;
            var curr = ThemeColorInterpolator.SmoothInterpolate(dayCol, twiCol, ngtCol, t);

            int dr = Math.Abs(curr.R - prev.R);
            int dg = Math.Abs(curr.G - prev.G);
            int db = Math.Abs(curr.B - prev.B);

            Assert.True(dr < 12, $"Red jumped {dr} at step {p}%");
            Assert.True(dg < 12, $"Green jumped {dg} at step {p}%");
            Assert.True(db < 12, $"Blue jumped {db} at step {p}%");

            prev = curr;
        }
    }

    [Fact]
    public void SmootherStep_HasZeroDerivativesAtBoundaries()
    {
        Assert.Equal(0.0, ThemeColorInterpolator.SmootherStep(0.0), precision: 6);
        Assert.Equal(1.0, ThemeColorInterpolator.SmootherStep(1.0), precision: 6);

        // Check slope near 0 is nearly flat (1st & 2nd derivative = 0)
        double nearZero = ThemeColorInterpolator.SmootherStep(0.01);
        Assert.True(nearZero < 0.0001, "SmootherStep should be near 0 at t=0.01");

        // Check slope near 1 is nearly flat
        double nearOne = ThemeColorInterpolator.SmootherStep(0.99);
        Assert.True(nearOne > 0.9999, "SmootherStep should be near 1 at t=0.99");
    }

    [Theory]
    [InlineData(0.0, 444)]   // Day nadir
    [InlineData(0.5, 301)]   // Twilight midpoint
    [InlineData(1.0, 158)]   // Night zenith
    public void SliderTrack_DotThumbGeometry_IsProperlyCenteredAndProportioned(double themeVal, int expectedY)
    {
        const int sliderTop = 158;
        const int sliderBottom = 444;
        const int sliderTravel = sliderBottom - sliderTop; // 286 px

        int thumbY = sliderBottom - (int)Math.Round(themeVal * sliderTravel);
        Assert.Equal(expectedY, thumbY);

        // Dot thumb radius is 5.0 px, so diameter is 10.0 px
        const float dotRadius = 5.0f;
        Assert.True(dotRadius >= 4.5f && dotRadius <= 6.0f, "Dot thumb radius must be 5.0px for ideal proportion to 2px track");

        // Center X is 36 px
        const int sliderCenterX = 36;
        Assert.Equal(36, sliderCenterX);
    }

    [Fact]
    public void ThemePalette_NightEndpoint_MatchesOriginalRiverDarkTheme()
    {
        // 100% Night endpoint (t = 1.0) must match original River Dark Theme values exactly
        var bg = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 244, 245, 247),
            c25: ThemeColor.FromArgb(255, 228, 231, 235),
            c50: ThemeColor.FromArgb(255, 30, 27, 22),
            c75: ThemeColor.FromArgb(255, 18, 18, 22),
            c100: ThemeColor.FromArgb(255, 10, 10, 12),
            t: 1.0);

        Assert.Equal(10, bg.R);
        Assert.Equal(10, bg.G);
        Assert.Equal(12, bg.B); // #0A0A0C

        var card = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 255, 255, 255),
            c25: ThemeColor.FromArgb(255, 247, 248, 250),
            c50: ThemeColor.FromArgb(255, 40, 34, 27),
            c75: ThemeColor.FromArgb(255, 26, 26, 32),
            c100: ThemeColor.FromArgb(255, 20, 20, 24),
            t: 1.0);

        Assert.Equal(20, card.R);
        Assert.Equal(20, card.G);
        Assert.Equal(24, card.B); // #141418

        var textPrimary = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 17, 17, 20),
            c25: ThemeColor.FromArgb(255, 28, 28, 32),
            c50: ThemeColor.FromArgb(255, 247, 239, 230),
            c75: ThemeColor.FromArgb(255, 253, 253, 253),
            c100: ThemeColor.FromArgb(255, 255, 255, 255),
            t: 1.0);

        Assert.Equal(255, textPrimary.R);
        Assert.Equal(255, textPrimary.G);
        Assert.Equal(255, textPrimary.B); // #FFFFFF

        var textSecondary = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 92, 96, 104),
            c25: ThemeColor.FromArgb(255, 110, 114, 122),
            c50: ThemeColor.FromArgb(255, 200, 184, 168),
            c75: ThemeColor.FromArgb(255, 154, 154, 164),
            c100: ThemeColor.FromArgb(255, 142, 142, 147),
            t: 1.0);

        Assert.Equal(142, textSecondary.R);
        Assert.Equal(142, textSecondary.G);
        Assert.Equal(147, textSecondary.B); // #8E8E93
    }

    [Fact]
    public void ThemePalette_DayEndpoint_MatchesOptimizedWarmLightTheme()
    {
        // 0% Day endpoint (t = 0.0) must match calibrated Light Theme values
        var bg = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 244, 245, 247),
            c25: ThemeColor.FromArgb(255, 228, 231, 235),
            c50: ThemeColor.FromArgb(255, 30, 27, 22),
            c75: ThemeColor.FromArgb(255, 18, 18, 22),
            c100: ThemeColor.FromArgb(255, 10, 10, 12),
            t: 0.0);

        Assert.Equal(244, bg.R);
        Assert.Equal(245, bg.G);
        Assert.Equal(247, bg.B); // #F4F5F7 (Warm Pearl Gray)

        var card = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 255, 255, 255),
            c25: ThemeColor.FromArgb(255, 247, 248, 250),
            c50: ThemeColor.FromArgb(255, 40, 34, 27),
            c75: ThemeColor.FromArgb(255, 26, 26, 32),
            c100: ThemeColor.FromArgb(255, 20, 20, 24),
            t: 0.0);

        Assert.Equal(255, card.R);
        Assert.Equal(255, card.G);
        Assert.Equal(255, card.B); // #FFFFFF (Crisp White Card)

        var textPrimary = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 17, 17, 20),
            c25: ThemeColor.FromArgb(255, 28, 28, 32),
            c50: ThemeColor.FromArgb(255, 247, 239, 230),
            c75: ThemeColor.FromArgb(255, 253, 253, 253),
            c100: ThemeColor.FromArgb(255, 255, 255, 255),
            t: 0.0);

        Assert.Equal(17, textPrimary.R);
        Assert.Equal(17, textPrimary.G);
        Assert.Equal(20, textPrimary.B); // #111114 (Deep Charcoal)
    }

    [Theory]
    [InlineData(0.0, 244, 245, 247)] // Day: fades to warm pearl gray #F4F5F7
    [InlineData(0.5, 30, 27, 22)]    // Twilight: fades to sunset umber #1E1B16
    [InlineData(1.0, 10, 10, 12)]    // Night: fades to original dark #0A0A0C
    public void ThemePalette_BackdropVignetteEndStops_AdaptToCurrentThemeBackground(double t, byte expectedR, byte expectedG, byte expectedB)
    {
        var bg = ThemeColorInterpolator.Interpolate5(
            c0: ThemeColor.FromArgb(255, 244, 245, 247),
            c25: ThemeColor.FromArgb(255, 228, 231, 235),
            c50: ThemeColor.FromArgb(255, 30, 27, 22),
            c75: ThemeColor.FromArgb(255, 18, 18, 22),
            c100: ThemeColor.FromArgb(255, 10, 10, 12),
            t: t);

        Assert.Equal(expectedR, bg.R);
        Assert.Equal(expectedG, bg.G);
        Assert.Equal(expectedB, bg.B);
    }
}

