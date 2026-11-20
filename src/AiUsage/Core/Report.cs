using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;

namespace AlData.AiUsage.Core;

/// <summary>Usage analytics from local Claude Code logs.
///
/// Reads %USERPROFILE%\.claude\projects\*\*.jsonl read-only and writes a single self-contained HTML
/// report: the data is embedded in the page; date filters, sessions and recommendations are
/// computed in the browser.</summary>
public static class Report
{
    const int Days = 90;
    static readonly string Logs = System.IO.Path.Combine(
        Environment.GetEnvironmentVariable("USERPROFILE") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".claude", "projects");
    static readonly string[] Kinds = ["input_tokens", "output_tokens", "cache_creation_input_tokens", "cache_read_input_tokens"];
    /// <summary>Same thing, short: keeps the embedded JSON small.</summary>
    static readonly string[] Short = ["in", "out", "cw", "cr"];
    static readonly Dictionary<string, (int Rank, string Field)> TitleRank = new()
    {
        ["last-prompt"] = (1, "lastPrompt"), ["ai-title"] = (2, "aiTitle"), ["custom-title"] = (3, "customTitle"),
    };

    const string FileName = "analytics.html";

    sealed class Cell
    {
        public int Count;
        public long Last;
        public readonly long[] Tokens = new long[Kinds.Length];
    }

    sealed class KeyOrder : IComparer<(string, string, string, string)>
    {
        public int Compare((string, string, string, string) a, (string, string, string, string) b)
        {
            var order = string.CompareOrdinal(a.Item1, b.Item1);
            if (order == 0)
                order = string.CompareOrdinal(a.Item2, b.Item2);
            if (order == 0)
                order = string.CompareOrdinal(a.Item3, b.Item3);
            return order != 0 ? order : string.CompareOrdinal(a.Item4, b.Item4);
        }
    }

    /// <summary>Writes the report into <paramref name="dir"/> and returns its path.</summary>
    public static string Build(string dir)
    {
        var (rows, titles) = Collect();
        Directory.CreateDirectory(dir);
        var path = System.IO.Path.Combine(dir, FileName);
        File.WriteAllText(path, Render(rows, titles));
        return path;
    }

    /// <summary>Rows (day, session, project, model) with token counts, plus session titles.
    ///
    /// Streaming rows are duplicated, so they are counted by id. Title: a custom one (customTitle)
    /// beats a generated one (aiTitle); otherwise the start of the last prompt.</summary>
    public static (JsonArray Rows, JsonObject Titles) Collect(int days = Days)
    {
        var seen = new HashSet<(string?, string?)>();
        var titles = new Dictionary<string, (int Rank, string Text)>();
        var cells = new SortedDictionary<(string Day, string Session, string Project, string Model), Cell>(new KeyOrder());
        var cutoff = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - days * 86400L;
        foreach (var path in Files())
        {
            string[] lines;
            try
            {
                if (new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds() < cutoff)
                    continue;
                lines = File.ReadAllLines(path);
            }
            catch (Exception)
            {
                continue;
            }
            foreach (var line in lines)
            {
                if (Json.Parse(line) is not JsonObject row)
                    continue;
                if (TitleRank.TryGetValue(Json.AsString(row["type"]) ?? "", out var rank))
                {
                    var text = (Json.AsString(row[rank.Field]) ?? "").Trim();
                    var sid = Json.AsString(row["sessionId"]);
                    if (text != "" && !string.IsNullOrEmpty(sid) && rank.Rank >= titles.GetValueOrDefault(sid).Rank)
                        titles[sid] = (rank.Rank, string.Concat(text.EnumerateRunes().Take(80)));
                    continue;
                }
                if (row["message"] is not JsonObject message || message["usage"] is not JsonObject { Count: > 0 } usage)
                    continue;
                var key = (Json.AsString(message["id"]), Json.AsString(row["requestId"]));
                if (key != (null, null) && !seen.Add(key))
                    continue;
                if (!DateTimeOffset.TryParse(Json.AsString(row["timestamp"]), CultureInfo.InvariantCulture,
                                             DateTimeStyles.AssumeLocal, out var when))
                    continue;
                var stamp = when.ToUnixTimeSeconds();
                if (stamp < cutoff)
                    continue;
                var session = Json.AsString(row["sessionId"]) is { Length: > 0 } id ? id : System.IO.Path.GetFileNameWithoutExtension(path);
                var model = Json.AsString(message["model"]) is { Length: > 0 } name ? name : "?";
                var day = when.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                var cellKey = (day, session, Project(Json.AsString(row["cwd"])), model);
                if (!cells.TryGetValue(cellKey, out var cell))
                    cells[cellKey] = cell = new Cell();
                cell.Count++;
                cell.Last = Math.Max(cell.Last, stamp);
                for (var i = 0; i < Kinds.Length; i++)
                    cell.Tokens[i] += (long)(Json.AsDouble(usage[Kinds[i]]) ?? 0);
            }
        }
        var rows = new JsonArray();
        var used = new HashSet<string>();
        foreach (var ((day, session, project, model), cell) in cells)
        {
            var row = new JsonObject { ["d"] = day, ["s"] = session, ["p"] = project, ["m"] = model, ["n"] = cell.Count, ["t"] = cell.Last };
            for (var i = 0; i < Short.Length; i++)
                row[Short[i]] = cell.Tokens[i];
            rows.Add(row);
            used.Add(session);
        }
        var named = new JsonObject();
        foreach (var (sid, (_, text)) in titles)
            if (used.Contains(sid))
                named[sid] = text;
        return (rows, named);
    }

    static IEnumerable<string> Files()
    {
        try
        {
            return Directory.EnumerateDirectories(Logs).SelectMany(dir => Directory.EnumerateFiles(dir, "*.jsonl")).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>The last folder of the working directory, as the report groups by it.</summary>
    static string Project(string? cwd)
    {
        if (string.IsNullOrEmpty(cwd))
            return "?";
        var name = System.IO.Path.GetFileName(System.IO.Path.TrimEndingDirectorySeparator(cwd));
        return name.Length > 0 ? name : "?";
    }

    /// <summary>The page with the data in it. The default encoder escapes &lt; and non-ASCII, so no
    /// value can close the script element.</summary>
    public static string Render(JsonArray rows, JsonObject titles)
    {
        using var stream = typeof(Report).Assembly.GetManifestResourceStream("Report.html")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("__DATA__", rows.ToJsonString()).Replace("__TITLES__", titles.ToJsonString());
    }
}
