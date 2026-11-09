using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MovieTime.Core.Theme;

/// <summary>
/// Centralized implementation of <see cref="IThemeManagerService"/> providing continuous palette calculation,
/// theme mode transitions, and settings persistence synchronization.
/// </summary>
public sealed class ThemeManagerService : IThemeManagerService
{
    private readonly IThemeSettingsService _settingsService;
    private readonly IThemePaletteApplier? _paletteApplier;
    private readonly ILogger<ThemeManagerService> _logger;

    private double _currentThemeValue = 1.0;
    private AppThemeMode _currentMode = AppThemeMode.Night;
    private bool _reducedMotion;
    private bool _ambientAnimationEnabled = true;

    public double CurrentThemeValue => _currentThemeValue;
    public AppThemeMode CurrentMode => _currentMode;
    public bool ReducedMotion => _reducedMotion;
    public bool AmbientAnimationEnabled => _ambientAnimationEnabled;

    public event Action<double, AppThemeMode>? ThemeChanged;

    public ThemeManagerService(
        IThemeSettingsService settingsService,
        IThemePaletteApplier? paletteApplier = null,
        ILogger<ThemeManagerService>? logger = null)
    {
        _settingsService = settingsService ?? throw new ArgumentNullException(nameof(settingsService));
        _paletteApplier = paletteApplier;
        _logger = logger ?? NullLogger<ThemeManagerService>.Instance;
    }

    public async Task InitializeAsync()
    {
        var settings = await _settingsService.LoadSettingsAsync().ConfigureAwait(false);
        _currentThemeValue = settings.ThemeValue;
        _reducedMotion = settings.ReducedMotion;
        _ambientAnimationEnabled = settings.AmbientAnimationEnabled;
        _currentMode = settings.Mode;

        _paletteApplier?.ApplyPalette(_currentThemeValue, _currentMode);
        ThemeChanged?.Invoke(_currentThemeValue, _currentMode);
    }

    public void AttachRootElement(object rootElement)
    {
        if (_paletteApplier != null)
        {
            _paletteApplier.AttachRootElement(rootElement);
            _paletteApplier.ApplyPalette(_currentThemeValue, _currentMode);
        }
    }

    public void SetThemeValue(double value, bool persist = true)
    {
        var clamped = Math.Clamp(value, 0.0, 1.0);
        if (Math.Abs(_currentThemeValue - clamped) < 0.0001) return;

        _currentThemeValue = clamped;
        _currentMode = _currentThemeValue switch
        {
            < 0.35 => AppThemeMode.Day,
            <= 0.65 => AppThemeMode.Twilight,
            _ => AppThemeMode.Night
        };

        _paletteApplier?.ApplyPalette(_currentThemeValue, _currentMode);
        ThemeChanged?.Invoke(_currentThemeValue, _currentMode);

        if (persist)
        {
            _ = _settingsService.UpdateThemeValueAsync(_currentThemeValue);
        }
    }

    public async Task SetReducedMotionAsync(bool reducedMotion)
    {
        _reducedMotion = reducedMotion;
        await _settingsService.UpdateReducedMotionAsync(reducedMotion).ConfigureAwait(false);
    }

    public async Task SetAmbientAnimationAsync(bool ambientEnabled)
    {
        _ambientAnimationEnabled = ambientEnabled;
        await _settingsService.UpdateAmbientAnimationAsync(ambientEnabled).ConfigureAwait(false);
    }
}
