using MovieTime.Core.Parsing.Models;

namespace MovieTime.Core.Parsing;

/// <summary>
/// Offline parser that converts raw media filenames and optional folder context
/// into structured movie or TV episode information.
/// </summary>
public interface IFilenameParserEngine
{
    /// <summary>
    /// Parses a media filename with optional parent folder context.
    /// </summary>
    /// <param name="filename">The filename to parse (with or without extension).</param>
    /// <param name="folderPath">Optional parent directory path or folder name hierarchy.</param>
    /// <returns>A structured <see cref="ParsedMediaInfo"/> result.</returns>
    ParsedMediaInfo Parse(string filename, string? folderPath = null);

    /// <summary>
    /// Parses a complete file path by decomposing it into filename and folder hierarchy.
    /// </summary>
    /// <param name="fullPath">The complete path to the media file on disk.</param>
    /// <returns>A structured <see cref="ParsedMediaInfo"/> result.</returns>
    ParsedMediaInfo ParsePath(string fullPath);
}
