using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Services;

namespace MovieTime.Core.Theme;

/// <summary>
/// Standard implementation of <see cref="IThemeSettingsService"/> persisting JSON configuration to %LocalAppData%\MovieTime\Config\theme_settings.json.
/// </summary>
public sealed class ThemeSettingsService : IThemeSettingsService
{
    private readonly IStoragePathService _storagePathService;
    private readonly ILogger<ThemeSettingsService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private ThemeSettings _currentSettings = new();
    private bool _isLoaded;

    public ThemeSettings CurrentSettings => _currentSettings;

    public event Action<ThemeSettings>? SettingsChanged;

    private string SettingsFilePath => Path.Combine(_storagePathService.ConfigDirectory, "theme_settings.json");

    public ThemeSettingsService(
        IStoragePathService storagePathService,
        ILogger<ThemeSettingsService>? logger = null)
    {
        _storagePathService = storagePathService ?? throw new ArgumentNullException(nameof(storagePathService));
        _logger = logger ?? NullLogger<ThemeSettingsService>.Instance;
    }

    public async Task<ThemeSettings> LoadSettingsAsync()
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_isLoaded)
            {
                return _currentSettings;
            }

            var path = SettingsFilePath;
            if (File.Exists(path))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(path).ConfigureAwait(false);
                    var deserialized = JsonSerializer.Deserialize<ThemeSettings>(json);
                    if (deserialized != null)
                    {
                        deserialized.ThemeValue = Math.Clamp(deserialized.ThemeValue, 0.0, 1.0);
                        _currentSettings = deserialized;
                        _logger.LogInformation("Loaded ThemeSettings: ThemeValue={ThemeValue:F2}, ReducedMotion={ReducedMotion}",
                            _currentSettings.ThemeValue, _currentSettings.ReducedMotion);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to deserialize ThemeSettings from {Path}. Using defaults.", path);
                    _currentSettings = new ThemeSettings();
                }
            }
            else
            {
                _currentSettings = new ThemeSettings();
                await SaveInternalAsync(_currentSettings).ConfigureAwait(false);
            }

            _isLoaded = true;
            SettingsChanged?.Invoke(_currentSettings);
            return _currentSettings;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveSettingsAsync(ThemeSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            settings.ThemeValue = Math.Clamp(settings.ThemeValue, 0.0, 1.0);
            _currentSettings = settings;
            _isLoaded = true;
            await SaveInternalAsync(_currentSettings).ConfigureAwait(false);
            SettingsChanged?.Invoke(_currentSettings);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateThemeValueAsync(double value)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_currentSettings.ThemeValue - clamped) > 0.001)
            {
                _currentSettings.ThemeValue = clamped;
                await SaveInternalAsync(_currentSettings).ConfigureAwait(false);
                SettingsChanged?.Invoke(_currentSettings);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateReducedMotionAsync(bool reducedMotion)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_currentSettings.ReducedMotion != reducedMotion)
            {
                _currentSettings.ReducedMotion = reducedMotion;
                await SaveInternalAsync(_currentSettings).ConfigureAwait(false);
                SettingsChanged?.Invoke(_currentSettings);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task UpdateAmbientAnimationAsync(bool ambientEnabled)
    {
        await _lock.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_currentSettings.AmbientAnimationEnabled != ambientEnabled)
            {
                _currentSettings.AmbientAnimationEnabled = ambientEnabled;
                await SaveInternalAsync(_currentSettings).ConfigureAwait(false);
                SettingsChanged?.Invoke(_currentSettings);
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task SaveInternalAsync(ThemeSettings settings)
    {
        try
        {
            _storagePathService.EnsureDirectoriesCreated();
            var path = SettingsFilePath;
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });

            var tempPath = path + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            File.Move(tempPath, path, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist ThemeSettings to disk.");
        }
    }
}
