using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Windows.Media;

namespace AlData.AiUsage.Core;

/// <summary>A level: below Limit percent the gauge takes Color.</summary>
public sealed record Level(double Limit, Color Color);

/// <summary>Colors the widget draws with: theme from the config with the palette laid on top.</summary>
public sealed class Theme
{
    public Color Bg { get; init; }
    public Color Track { get; init; }
    public Color Text { get; init; }
    public Color Dim { get; init; }
    /// <summary>Only the system palette has one: the Windows accent.</summary>
    public Color? Accent { get; init; }
    public IReadOnlyList<Level> Levels { get; init; } = [];
    /// <summary>Empty when the palette has none; coloring by pace then falls back to Levels.</summary>
    public IReadOnlyList<Level> PaceLevels { get; init; } = [];

    /// <summary>The system palette supplies the Windows accent; the others use the regular text color.</summary>
    public Color TickColor => Accent ?? Text;

    public static Theme From(JsonObject colors) => new()
    {
        Bg = Parse(colors["bg"], Color.FromRgb(0x20, 0x20, 0x20)),
        Track = Parse(colors["track"], Color.FromRgb(0x3e, 0x3e, 0x3e)),
        Text = Parse(colors["text"], Color.FromRgb(0xe8, 0xe8, 0xe8)),
        Dim = Parse(colors["dim"], Color.FromRgb(0x96, 0x96, 0x96)),
        Accent = Json.AsString(colors["accent"]) is { } accent ? TryParse(accent) : null,
        Levels = ParseLevels(colors["levels"]),
        PaceLevels = ParseLevels(colors["pace_levels"]),
    };

    static IReadOnlyList<Level> ParseLevels(JsonNode? node)
    {
        var levels = new List<Level>();
        if (node is not JsonArray items)
            return levels;
        foreach (var item in items)
        {
            if (item is JsonArray pair && pair.Count >= 2 && Json.AsDouble(pair[0]) is { } limit
                && Json.AsString(pair[1]) is { } raw && TryParse(raw) is { } color)
                levels.Add(new Level(limit, color));
        }
        return levels;
    }

    static Color Parse(JsonNode? node, Color fallback) =>
        Json.AsString(node) is { } raw && TryParse(raw) is { } color ? color : fallback;

    /// <summary>#rgb, #rrggbb or a color name.</summary>
    public static Color? TryParse(string raw)
    {
        try
        {
            return ColorConverter.ConvertFromString(raw.Trim()) is Color color ? color : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Plain linear blend of two colors, for shading.</summary>
    public static Color Mix(Color color, Color other, double part) => Color.FromRgb(
        (byte)(color.R + (other.R - color.R) * part),
        (byte)(color.G + (other.G - color.G) * part),
        (byte)(color.B + (other.B - color.B) * part));

    public static string Hex(Color color) =>
        string.Create(CultureInfo.InvariantCulture, $"#{color.R:x2}{color.G:x2}{color.B:x2}");
}
