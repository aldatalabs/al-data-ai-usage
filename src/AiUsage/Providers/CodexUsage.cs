using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AlData.AiUsage.Core;

namespace AlData.AiUsage.Providers;

/// <summary>Codex CLI limits. No network needed: Codex itself writes a limits snapshot to the
/// session log (%USERPROFILE%\.codex\sessions\YYYY\MM\DD\rollout-*.jsonl), as a line like
/// "rate_limits":{"primary":{"used_percent":5.0,"window_minutes":300,"resets_at":1790451451}, ...}.
/// The last such snapshot from the newest file is exactly what Codex saw at its last reply.</summary>
public sealed class CodexUsage : IProvider
{
    public string Name => "codex_usage";
    public string Title => "Codex";

    static readonly string Sessions = Path.Combine(
        Environment.GetEnvironmentVariable("USERPROFILE") is { Length: > 0 } home
            ? home
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".codex", "sessions");

    /// <summary>The snapshot sits at the end of the file, no need to read all of it.</summary>
    const int TailBytes = 256 * 1024;
    const string Marker = "\"rate_limits\"";

    /// <summary>window_minutes to gauge key; other windows are shown as they are.</summary>
    static readonly Dictionary<int, (string Key, string Label)> Windows = new()
    {
        [300] = ("codex_primary", "5h"),
        [10080] = ("codex_secondary", "7d"),
    };

    static ProviderError Missing() => new("no Codex sessions - run codex once", "codex_missing");
    static ProviderError NoData() => new("no rate limits in the Codex log yet", "codex_no_data");

    static string NewestLog()
    {
        if (!Directory.Exists(Sessions))
            throw Missing();
        string? newest = null;
        var newestTime = DateTime.MinValue;
        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var path in Directory.EnumerateFiles(Sessions, "*.jsonl", options))
        {
            DateTime time;
            try
            {
                time = File.GetLastWriteTimeUtc(path);
            }
            catch (Exception)
            {
                continue;
            }
            if (time > newestTime)
            {
                newest = path;
                newestTime = time;
            }
        }
        return newest ?? throw Missing();
    }

    static string Tail(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var start = Math.Max(0, stream.Length - TailBytes);
        stream.Seek(start, SeekOrigin.Begin);
        var buffer = new byte[stream.Length - start];
        stream.ReadExactly(buffer);
        return Encoding.UTF8.GetString(buffer);
    }

    /// <summary>The last rate_limits object in the text, cut out by brace balance.</summary>
    static JsonObject LastSnapshot(string text)
    {
        var start = text.LastIndexOf(Marker, StringComparison.Ordinal);
        if (start < 0)
            throw NoData();
        var brace = text.IndexOf('{', start + Marker.Length);
        if (brace < 0)
            throw NoData();
        int depth = 0, end = -1;
        for (var index = brace; index < text.Length; index++)
        {
            if (text[index] == '{')
            {
                depth++;
            }
            else if (text[index] == '}' && --depth == 0)
            {
                end = index + 1;
                break;
            }
        }
        if (end < 0)
            throw NoData();
        return Json.Parse(text[brace..end]) as JsonObject
               ?? throw new ProviderError("Codex log is not readable", "codex_broken");
    }

    static Gauge MakeGauge(string key, string label, JsonObject block, DateTimeOffset now)
    {
        var minutes = Json.AsInt(block["window_minutes"]);
        var moment = block["resets_at"] is JsonValue raw && raw.GetValueKind() == System.Text.Json.JsonValueKind.Number
            ? Model.ParseIso(raw)
            : null;
        double? value = Model.Clamp01((Json.AsDouble(block["used_percent"]) ?? 0) / 100.0);
        if (moment is { } at && at <= now)
        {
            // the window has already rolled over since Codex's last reply
            value = 0.0;
            moment = null;
        }
        return new Gauge
        {
            Key = key,
            Label = label,
            Value = value,
            ResetsAt = moment,
            Hint = "Codex",
            WindowSeconds = minutes is > 0 ? minutes * 60 : null,
        };
    }

    public Task<List<Gauge>> FetchAsync(JsonObject settings, CancellationToken ct) => Task.Run(() =>
    {
        var snapshot = LastSnapshot(Tail(NewestLog()));
        var now = DateTimeOffset.UtcNow;
        var gauges = new List<Gauge>();
        foreach (var slot in new[] { "primary", "secondary" })
        {
            if (snapshot[slot] is not JsonObject block)
                continue;
            var (key, label) = Json.AsInt(block["window_minutes"]) is { } m && Windows.TryGetValue(m, out var known)
                ? known
                : ($"codex_{slot}", "cx");
            gauges.Add(MakeGauge(key, label, block, now));
        }
        if (gauges.Count == 0)
            throw NoData();
        return gauges;
    }, ct);
}
