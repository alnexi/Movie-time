using System;
using System.Collections.Generic;

namespace MovieTime.Core;

/// <summary>
/// Core application constants and metadata.
/// </summary>
public static class AppConstants
{
    public const string ApplicationName = "River";
    public const string ApplicationVersion = "1.2.3";
    public const string LocalDataFolder = "MovieTime";

    /// <summary>
    /// Supported media video file extensions recognized across River's scanner,
    /// Windows Explorer Open With file associations, and command-line activation.
    /// </summary>
    public static readonly HashSet<string> SupportedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mkv", ".mp4", ".avi", ".mov", ".wmv", ".webm", ".m4v",
        ".mpg", ".mpeg", ".ts", ".m2ts", ".mts", ".m2t", ".vob",
        ".flv", ".divx", ".3gp", ".ogv", ".asf", ".rmvb", ".rm"
    };
}
