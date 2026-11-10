using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace MovieTime.Core.Shell;

/// <summary>
/// Platform-aware command-line argument parser and video file path extractor.
/// Handles quoted paths with spaces, Unicode characters, and multi-file arguments
/// using the native Windows Explorer CommandLineToArgvW API with managed fallback.
/// </summary>
public static class CommandLineHelper
{
    [DllImport("shell32.dll", SetLastError = true)]
    private static extern IntPtr CommandLineToArgvW(
        [MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine,
        out int pNumArgs);

    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr hMem);

    /// <summary>
    /// Splits a command line string into separate arguments according to Windows standard.
    /// </summary>
    public static string[] SplitCommandLine(string? cmdLine)
    {
        if (string.IsNullOrWhiteSpace(cmdLine))
        {
            return Array.Empty<string>();
        }

        if (OperatingSystem.IsWindows())
        {
            try
            {
                IntPtr ptrToSplitArgs = CommandLineToArgvW(cmdLine, out int numberOfArgs);
                if (ptrToSplitArgs != IntPtr.Zero)
                {
                    try
                    {
                        var splitArgs = new string[numberOfArgs];
                        for (int i = 0; i < numberOfArgs; i++)
                        {
                            IntPtr ptrToArg = Marshal.ReadIntPtr(ptrToSplitArgs, i * IntPtr.Size);
                            splitArgs[i] = Marshal.PtrToStringUni(ptrToArg) ?? string.Empty;
                        }
                        return splitArgs;
                    }
                    finally
                    {
                        LocalFree(ptrToSplitArgs);
                    }
                }
            }
            catch
            {
                // Fallback to managed parser if shell32 P/Invoke is unavailable
            }
        }

        return ManagedSplitCommandLine(cmdLine);
    }

    /// <summary>
    /// Managed fallback parser that respects double-quotes and whitespace boundaries.
    /// </summary>
    public static string[] ManagedSplitCommandLine(string cmdLine)
    {
        if (string.IsNullOrWhiteSpace(cmdLine))
        {
            return Array.Empty<string>();
        }

        var args = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < cmdLine.Length; i++)
        {
            char c = cmdLine[i];
            if (c == '\"')
            {
                inQuotes = !inQuotes;
            }
            else if (char.IsWhiteSpace(c) && !inQuotes)
            {
                if (current.Length > 0)
                {
                    args.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(c);
            }
        }

        if (current.Length > 0)
        {
            args.Add(current.ToString());
        }

        return args.ToArray();
    }

    /// <summary>
    /// Extracts, validates, and normalizes supported video file paths from command-line arguments.
    /// </summary>
    public static List<string> ExtractVideoFiles(string? cmdLine, bool isFullCommandLineWithExecutable = true)
    {
        if (string.IsNullOrWhiteSpace(cmdLine))
        {
            return new List<string>();
        }

        var tokens = SplitCommandLine(cmdLine);
        int startIndex = isFullCommandLineWithExecutable && tokens.Length > 0 ? 1 : 0;

        var rawCandidates = new List<string>();
        for (int i = startIndex; i < tokens.Length; i++)
        {
            rawCandidates.Add(tokens[i]);
        }

        return FilterSupportedVideoFiles(rawCandidates);
    }

    /// <summary>
    /// Filters raw file candidate strings against supported extensions and filesystem presence.
    /// </summary>
    public static List<string> FilterSupportedVideoFiles(IEnumerable<string> candidates)
    {
        var validFiles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in candidates)
        {
            var clean = raw.Trim('\"', '\'').Trim();
            if (string.IsNullOrWhiteSpace(clean)) continue;

            try
            {
                var ext = Path.GetExtension(clean);
                if (AppConstants.SupportedVideoExtensions.Contains(ext) && File.Exists(clean))
                {
                    var full = Path.GetFullPath(clean);
                    if (seen.Add(full))
                    {
                        validFiles.Add(full);
                    }
                }
            }
            catch
            {
                // Silently ignore malformed path syntax
            }
        }

        return validFiles;
    }
}
