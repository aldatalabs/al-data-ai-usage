using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;

namespace AlData.AiUsage.Core;

/// <summary>Cache of the last successful data. Lets the widget show numbers right after a restart
/// instead of an error: /usage returns 429 after a few requests and keeps it up for hours.
/// The format is the one the Tk prototype wrote, so an upgrade keeps the numbers and the pause.</summary>
public static class Cache
{
    public static string PathName => Path.Combine(AppPaths.UserDir, "cache.json");

    /// <summary>Gauges, when they were fetched, and the time before which the source must not be queried.</summary>
    public static (List<Gauge> Gauges, DateTimeOffset? FetchedAt, DateTimeOffset? NextAllowedAt) Load()
    {
        var gauges = new List<Gauge>();
        if (Json.ReadObject(PathName) is not { } data)
            return (gauges, null, null);
        if (data["gauges"] is JsonArray items)
        {
            foreach (var node in items)
            {
                if (node is not JsonObject item || Json.AsString(item["key"]) is not { } key
                    || Json.AsString(item["label"]) is not { } label)
                    continue;
                gauges.Add(new Gauge
                {
                    Key = key,
                    Label = label,
                    Value = Json.AsDouble(item["value"]),
                    ResetsAt = Model.ParseIso(item["resets_at"]),
                    Hint = item.Str("hint", ""),
                    WindowSeconds = Json.AsInt(item["window_seconds"]),
                    Source = item.Str("source", ""),
                    Provider = item.Str("provider", ""),
                    Detail = item.Str("detail", ""),
                    Account = item.Str("account", ""),
                });
            }
        }
        return (gauges, Model.ParseIso(data["fetched_at"]), Model.ParseIso(data["next_allowed_at"]));
    }

    public static void Save(IEnumerable<Gauge> gauges, DateTimeOffset? fetchedAt = null, DateTimeOffset? nextAllowedAt = null)
    {
        var list = new JsonArray();
        foreach (var g in gauges)
        {
            list.Add(new JsonObject
            {
                ["key"] = g.Key,
                ["label"] = g.Label,
                ["value"] = g.Value,
                ["resets_at"] = g.ResetsAt is { } r ? Model.ToIso(r) : null,
                ["hint"] = g.Hint,
                ["window_seconds"] = g.WindowSeconds,
                ["source"] = g.Source,
                ["provider"] = g.Provider,
                ["detail"] = g.Detail,
                ["account"] = g.Account,
            });
        }
        var payload = new JsonObject
        {
            ["fetched_at"] = Model.ToIso(fetchedAt ?? DateTimeOffset.UtcNow),
            ["next_allowed_at"] = nextAllowedAt is { } n ? Model.ToIso(n) : null,
            ["gauges"] = list,
        };
        try
        {
            Json.WriteAtomic(PathName, payload, indented: false);
        }
        catch (Exception)
        {
            // the cache is a convenience, not a critical path
        }
    }

    /// <summary>The pause has to survive a restart, otherwise every launch hits the rate limit again.</summary>
    public static void SetPause(int seconds)
    {
        var (gauges, fetchedAt, _) = Load();
        Save(gauges, fetchedAt, DateTimeOffset.UtcNow.AddSeconds(seconds));
    }
}
