using System.Threading.Tasks;
using MovieTime.Core.Entities;

namespace MovieTime.Core.Database.Repositories;

/// <summary>
/// Repository abstraction for saving and retrieving user playback progress and completion states.
/// </summary>
public interface IPlaybackProgressRepository
{
    Task<PlaybackProgress?> GetByMediaFileIdAsync(int mediaFileId);
    Task<PlaybackProgress?> GetByMovieIdAsync(int movieId);
    Task<PlaybackProgress?> GetByEpisodeIdAsync(int episodeId);
    Task<int> SaveProgressAsync(int mediaFileId, int? movieId, long positionTicks, long durationTicks, bool isFinished, int? episodeId = null);
    Task<int> DeleteByMediaFileIdAsync(int mediaFileId);
    Task<int> DeleteByMovieIdAsync(int movieId);
    Task<int> DeleteByEpisodeIdAsync(int episodeId);
}
