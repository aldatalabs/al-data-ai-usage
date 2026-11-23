using System;
using System.Linq;
using System.Reflection;
using System.Windows.Media;

namespace AlData.AiUsage.Ui;

/// <summary>Menu icons: Segoe Fluent Icons code points, the set Windows 11 uses. On a machine
/// without that font the menu shows an empty gutter, not boxes.</summary>
internal static class Icons
{
    /// <summary>Fluent on Windows 11, MDL2 on 10; null without either.</summary>
    static readonly Lazy<FontFamily?> Installed = new(() =>
    {
        string[] wanted = ["Segoe Fluent Icons", "Segoe MDL2 Assets"];
        var have = Fonts.SystemFontFamilies.Select(f => f.Source).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return wanted.FirstOrDefault(have.Contains) is { } name ? new FontFamily(name) : null;
    });

    public static FontFamily? Family => Installed.Value;

    public const string Refresh = "", Appearance = "", Interval = "", Language = "",
        Sources = "", Skin = "", Palette = "", Count = "", Lines = "",
        Shuffle = "", Color = "", ShowReset = "", Pace = "", Lock = "",
        Toasts = "", Analytics = "", Report = "", Hide = "", Show = "",
        Autostart = "", Exit = "", CheckUpdates = "",
        Transparent = "",
        Mail = "", Link = "",
        Feedback = "", Bug = "", Idea = "", Rate = "", Source = "", Store = "";
}

/// <summary>The version a user reads beside Exit; every bug report starts with it.</summary>
internal static class AppInfo
{
    public static readonly string Version =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0";
}
