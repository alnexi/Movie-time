using System;
using System.Collections.Generic;
using System.IO;
using MovieTime.Core;
using MovieTime.Core.Shell;

namespace MovieTime.App.Services;

/// <summary>
/// WinUI 3 shell activation extractor.
/// Resolves target video file paths from Windows AppActivationArguments
/// (File associations, Windows Open With launch arguments, single-instance redirection)
/// and delegates tokenization to CommandLineHelper.
/// </summary>
public static class CommandLineActivationHelper
{
    public static List<string> ExtractVideoFiles(
        Microsoft.Windows.AppLifecycle.AppActivationArguments? activatedArgs,
        string? fallbackCommandLine = null)
    {
        var rawCandidates = new List<string>();

        if (activatedArgs != null)
        {
            // Case 1: Direct File activation
            if (activatedArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.File)
            {
                if (activatedArgs.Data is Windows.ApplicationModel.Activation.IFileActivatedEventArgs fileArgs && fileArgs.Files != null)
                {
                    foreach (var file in fileArgs.Files)
                    {
                        if (!string.IsNullOrWhiteSpace(file.Path))
                        {
                            rawCandidates.Add(file.Path);
                        }
                    }
                }
            }
            // Case 2: Launch activation (Open With or single-instance redirect)
            else if (activatedArgs.Kind == Microsoft.Windows.AppLifecycle.ExtendedActivationKind.Launch)
            {
                if (activatedArgs.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launchArgs && !string.IsNullOrWhiteSpace(launchArgs.Arguments))
                {
                    var tokens = CommandLineHelper.SplitCommandLine(launchArgs.Arguments);
                    rawCandidates.AddRange(tokens);
                }
            }
        }

        // Case 3: Initial command-line fallback
        if (rawCandidates.Count == 0)
        {
            var cmd = fallbackCommandLine ?? Environment.CommandLine;
            return CommandLineHelper.ExtractVideoFiles(cmd, isFullCommandLineWithExecutable: true);
        }

        return CommandLineHelper.FilterSupportedVideoFiles(rawCandidates);
    }
}
