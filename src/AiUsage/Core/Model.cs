using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media;

namespace AlData.AiUsage.Core;

/// <summary>Short time units as the active language writes them.</summary>
public sealed record Units(string D, string H, string M, string Now)
{
    public static readonly Units Default = new("d", "h", "m", "now");
}

/// <summary>Pure functions over gauges: percent, level colors, countdown, pace. Knows nothing about Claude.</summary>
public static class Model
{
    /// <summary>At the very start of the window the projection is noise.</summary>
    public const double MinElapsed = 0.02;

    public static double? Clamp01(double? value) => value is null ? null : Math.Clamp(value.Value, 0.0, 1.0);

    public static int? Percent(Gauge gauge) =>
        gauge.Value is null ? null : (int)Math.Round(gauge.Value.Value * 100, MidpointRounding.ToEven);

    /// <summary>Levels are (threshold in %, color) in ascending order.</summary>
    public static Color LevelColor(double? value, IReadOnlyList<Level> levels, Color dim)
    {
        if (value is null || levels.Count == 0)
            return dim;
        var pct = value.Value * 100;
        foreach (var level in levels)
            if (pct < level.Limit)
                return level.Color;
        return levels[^1].Color;
    }

    /// <summary>An ISO 8601 string or a Unix timestamp; a time without an offset is UTC.</summary>
    public static DateTimeOffset? ParseIso(JsonNode? raw)
    {
        if (raw is not JsonValue value)
            return null;
        if (value.GetValueKind() == JsonValueKind.Number)
        {
            var seconds = Json.AsDouble(value);
            if (seconds is null || seconds == 0)
                return null;
            try
            {
                return DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds.Value * 1000));
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }
        return ParseIso(Json.AsString(value));
    }

    public static DateTimeOffset? ParseIso(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
            return null;
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture,
                                       DateTimeStyles.AssumeUniversal | DateTimeStyles.AllowWhiteSpaces, out var moment)
            ? moment
            : null;
    }

    /// <summary>The format of times in cache.json, as the Tk prototype wrote them: 2026-10-31T14:00:37.014247+00:00.</summary>
    public static string ToIso(DateTimeOffset moment) =>
        moment.ToString("yyyy-MM-dd'T'HH:mm:ss.ffffffzzz", CultureInfo.InvariantCulture);

    /// <summary>Short time remaining: 4d6h / 2:14 / 7m / now.</summary>
    public static string Countdown(DateTimeOffset? resetsAt, Units? units = null, DateTimeOffset? now = null)
    {
        if (resetsAt is null)
            return "";
        units ??= Units.Default;
        var left = (long)(resetsAt.Value - (now ?? DateTimeOffset.UtcNow)).TotalSeconds;
        if (left <= 0)
            return units.Now;
        var days = left / 86400;
        var hours = left % 86400 / 3600;
        var minutes = left % 3600 / 60;
        if (days > 0)
            return $"{days}{units.D}{hours}{units.H}";
        if (hours > 0)
            return $"{hours}:{minutes:00}";
        return $"{minutes}{units.M}";
    }

    /// <summary>Local reset time for the tooltip.</summary>
    public static string ResetFull(DateTimeOffset? resetsAt, string unknown = "unknown") =>
        resetsAt is null ? unknown : resetsAt.Value.ToLocalTime().ToString("dd.MM HH:mm", CultureInfo.InvariantCulture);

    /// <summary>Whether the limit will last until reset if the pace holds.
    /// The pace is measured from the start of the window (reset minus its length), not from recent
    /// samples: the widget polls the source rarely and keeps no history.</summary>
    public static Pace? Pace(Gauge gauge, DateTimeOffset? now = null)
    {
        if (gauge.Value is not { } value || gauge.ResetsAt is not { } resetsAt || gauge.WindowSeconds is not > 0)
            return null;
        var at = now ?? DateTimeOffset.UtcNow;
        var window = (double)gauge.WindowSeconds.Value;
        var elapsed = (at - resetsAt.AddSeconds(-window)).TotalSeconds;
        if (elapsed <= 0)
            return null;
        var share = Math.Clamp(elapsed / window, 0.0, 1.0);
        if (share < MinElapsed)
            return null;
        DateTimeOffset? exhaustAt = null;
        var rate = value / elapsed;
        if (rate > 0 && value < 1)
        {
            // Compare seconds before adding: a tiny rate would overflow DateTimeOffset.
            var seconds = (1 - value) / rate;
            if (seconds < (resetsAt - at).TotalSeconds)
                exhaustAt = at.AddSeconds(seconds);
        }
        else if (value >= 1)
        {
            exhaustAt = at;
        }
        return new Pace(share, value / share, exhaustAt);
    }
}
