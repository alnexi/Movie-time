using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MovieTime.Core.Services;

namespace MovieTime.Core.Playback;

/// <summary>
/// Persists subtitle customization preferences to local JSON storage.
/// </summary>
public sealed class SubtitleSettingsService : ISubtitleSettingsService
{
    private const string SettingsFileName = "subtitle_settings.json";
    private readonly IStoragePathService _storagePathService;
    private readonly ILogger<SubtitleSettingsService> _logger;
    private readonly object _lock = new();

    private SubtitleSettings? _cachedSettings;

    public SubtitleSettingsService(
        IStoragePathService storagePathService,
        ILogger<SubtitleSettingsService>? logger = null)
    {
        _storagePathService = storagePathService ?? throw new ArgumentNullException(nameof(storagePathService));
        _logger = logger ?? NullLogger<SubtitleSettingsService>.Instance;
    }

    private string GetSettingsFilePath()
    {
        return Path.Combine(_storagePathService.ConfigDirectory, SettingsFileName);
    }

    public SubtitleSettings GetSettings()
    {
        lock (_lock)
        {
            if (_cachedSettings != null)
            {
                return _cachedSettings.Clone();
            }

            var filePath = GetSettingsFilePath();
            try
            {
                if (File.Exists(filePath))
                {
                    var json = File.ReadAllText(filePath);
                    var deserialized = JsonSerializer.Deserialize<SubtitleSettings>(json);
                    if (deserialized != null)
                    {
                        _cachedSettings = deserialized;
                        return _cachedSettings.Clone();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load subtitle settings from {Path}. Using defaults.", filePath);
            }

            _cachedSettings = new SubtitleSettings();
            return _cachedSettings.Clone();
        }
    }

    public async Task SaveSettingsAsync(SubtitleSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        lock (_lock)
        {
            _cachedSettings = settings.Clone();
        }

        var filePath = GetSettingsFilePath();
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
            _logger.LogInformation("Saved subtitle settings to {Path}", filePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist subtitle settings to {Path}", filePath);
        }
    }
}
