using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using AlData.AiUsage.Core;
using Microsoft.Win32;

namespace AlData.AiUsage.Ui;

/// <summary>Autostart. A plain copy gets a shortcut in shell:startup, made
/// by PowerShell through WScript.Shell. An MSIX package declares a startup task in its manifest
/// instead (AppData is virtualized, the shortcut would never run): Windows owns that switch, the
/// menu reads its state and sends the user to Settings to flip it.</summary>
internal static class Startup
{
    const string LinkName = "al-data-ai-usage.lnk";
    /// <summary>Must match the TaskId in AppxManifest.xml.</summary>
    const string TaskId = "AlDataAiUsage";
    const string SettingsUri = "ms-settings:startupapps";

    static string LinkPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup), LinkName);

    /// <summary>The MSIX package family name when running from one, else null.</summary>
    public static string? PackageFamily()
    {
        try
        {
            uint length = 0;
            if (Native.GetCurrentPackageFamilyName(ref length, IntPtr.Zero) == Native.AppModelErrorNoPackage || length == 0)
                return null;
            var buffer = Marshal.AllocHGlobal((int)length * 2);
            try
            {
                return Native.GetCurrentPackageFamilyName(ref length, buffer) == 0 ? Marshal.PtrToStringUni(buffer) : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static bool Packaged => PackageFamily() is not null;

    /// <summary>Where to write a file another program opens. A package's writes to AppData land in
    /// its private LocalCache, which a browser started outside the package cannot see; the package's
    /// own folder is real for everyone.</summary>
    public static string SharedDir() => PackageFamily() is { } family
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages", family, "LocalCache")
        : AppPaths.UserDir;

    /// <summary>Windows keeps the startup task switch here; the manifest enables it, so missing means
    /// on. Values follow StartupTaskState: 2 Enabled, 4 EnabledByPolicy, everything else off.</summary>
    static bool TaskState(string family)
    {
        var key = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\"
                  + family + @"\" + TaskId;
        try
        {
            using var handle = Registry.CurrentUser.OpenSubKey(key);
            return handle?.GetValue("State") is not int state || state is 2 or 4;
        }
        catch (Exception)
        {
            return true;
        }
    }

    public static bool IsEnabled() => PackageFamily() is { } family ? TaskState(family) : File.Exists(LinkPath);

    /// <summary>A path inside a single-quoted PowerShell string: the apostrophe is doubled.</summary>
    static string Quote(string path) => path.Replace("'", "''");

    static void Enable()
    {
        var target = Environment.ProcessPath ?? throw new InvalidOperationException("no process path");
        var script = $"$s=(New-Object -ComObject WScript.Shell).CreateShortcut('{Quote(LinkPath)}');"
                     + $"$s.TargetPath='{Quote(target)}';"
                     + $"$s.WorkingDirectory='{Quote(Path.GetDirectoryName(target) ?? "")}';"
                     + "$s.Description='al-data-ai-usage';"
                     + "$s.Save()";
        var info = new ProcessStartInfo("powershell")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var arg in new[] { "-NoProfile", "-NonInteractive", "-Command", script })
            info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("powershell did not start");
        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException(error.Trim() is { Length: > 0 } text ? text : $"exit code {process.ExitCode}");
    }

    static void Disable() => File.Delete(LinkPath);

    public static void Toggle(bool value)
    {
        if (Packaged)
        {
            Shell.Open(SettingsUri);   // the switch belongs to Windows
            return;
        }
        if (value)
            Enable();
        else
            Disable();
    }
}

/// <summary>Hands a URL or a file to whatever Windows opens it with.</summary>
internal static class Shell
{
    public static void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
}
