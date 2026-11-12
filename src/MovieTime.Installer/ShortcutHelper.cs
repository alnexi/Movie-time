using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace MovieTime.Installer;

internal static class ShortcutHelper
{
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    private class ShellLinkClass
    {
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    public static void CreateShortcut(
        string shortcutPath,
        string targetPath,
        string? workingDirectory = null,
        string? description = null,
        string? iconPath = null)
    {
        var directory = Path.GetDirectoryName(shortcutPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var link = (IShellLinkW)new ShellLinkClass();
        link.SetPath(targetPath);

        if (!string.IsNullOrEmpty(workingDirectory))
        {
            link.SetWorkingDirectory(workingDirectory);
        }
        else
        {
            var targetDir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(targetDir))
            {
                link.SetWorkingDirectory(targetDir);
            }
        }

        if (!string.IsNullOrEmpty(description))
        {
            link.SetDescription(description);
        }

        if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
        {
            link.SetIconLocation(iconPath, 0);
        }

        var persistFile = (IPersistFile)link;
        persistFile.Save(shortcutPath, true);
    }

    public static void DeleteShortcut(string shortcutPath)
    {
        try
        {
            if (File.Exists(shortcutPath))
            {
                File.Delete(shortcutPath);
            }
        }
        catch
        {
            // Best effort cleanup
        }
    }
}
