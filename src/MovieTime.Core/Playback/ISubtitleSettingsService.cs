using System.Threading.Tasks;

namespace MovieTime.Core.Playback;

/// <summary>
/// Service managing persistent storage of user subtitle appearance preferences.
/// </summary>
public interface ISubtitleSettingsService
{
    SubtitleSettings GetSettings();
    Task SaveSettingsAsync(SubtitleSettings settings);
}
