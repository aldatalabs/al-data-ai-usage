using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Win32;

namespace AlData.AiUsage.Core;

/// <summary>Palettes: color sets in palettes/&lt;code&gt;.json, laid on top of theme from the config.
/// A new palette is a new file, no code changes.</summary>
public static class Palettes
{
    public const string Default = "dark";
    /// <summary>Not a file but the current Windows colors.</summary>
    public const string SystemCode = "system";

    /// <summary>Drawing hits the theme often: read the registry at most once every 2 s.</summary>
    static readonly TimeSpan SystemTtl = TimeSpan.FromSeconds(2);

    static readonly ConcurrentDictionary<string, JsonObject> Tables = new();
    static readonly Stopwatch Clock = Stopwatch.StartNew();
    static (TimeSpan Stamp, JsonObject? Colors) systemCache;

    static string Dir => Path.Combine(AppPaths.Root, "palettes");

    static JsonObject Table(string code) =>
        Tables.GetOrAdd(code, c => Json.ReadObject(Path.Combine(Dir, c + ".json")) ?? new JsonObject());

    static int? Dword(string path, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(path);
            return key?.GetValue(name) is int value ? value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>DWM stores the accent as 0xAABBGGRR.</summary>
    static string? Accent()
    {
        if (Dword(@"Software\Microsoft\Windows\DWM", "AccentColor") is not { } raw)
            return null;
        var blue = (raw >> 16) & 0xFF;
        var green = (raw >> 8) & 0xFF;
        var red = raw & 0xFF;
        return "#" + red.ToString("x2") + green.ToString("x2") + blue.ToString("x2");
    }

    static JsonObject ColorsOnly(JsonObject table)
    {
        var colors = new JsonObject();
        foreach (var pair in table)
            if (!pair.Key.StartsWith("palette.", StringComparison.Ordinal))
                colors[pair.Key] = pair.Value?.DeepClone();
        return colors;
    }

    /// <summary>Light or dark comes from the same registry branch as the taskbar color.</summary>
    public static JsonObject SystemColors()
    {
        var now = Clock.Elapsed;
        var (stamp, cached) = systemCache;
        if (cached is not null && now - stamp < SystemTtl)
            return cached;
        var light = Dword(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme");
        var colors = ColorsOnly(Table(light is > 0 ? "light" : "dark"));
        if (Accent() is { } accent)
            colors["accent"] = accent;
        systemCache = (now, colors);
        return colors;
    }

    /// <summary>(code, name) for the menu: System, the default, the rest by name.</summary>
    public static List<(string Code, string Name)> Available()
    {
        var items = new List<(string Code, string Name)> { (SystemCode, "System") };
        try
        {
            foreach (var path in Directory.EnumerateFiles(Dir, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(path);
                items.Add((code, Table(code).Str("palette.name", code)));
            }
        }
        catch (Exception)
        {
            // no palettes folder: only System
        }
        return items
            .OrderBy(i => i.Code != SystemCode && i.Code != Default)
            .ThenBy(i => i.Code != SystemCode)
            .ThenBy(i => i.Name.ToLowerInvariant(), StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Colors only, without the internal palette.name.</summary>
    public static JsonObject Colors(string code) => code == SystemCode ? SystemColors() : ColorsOnly(Table(code));
}
