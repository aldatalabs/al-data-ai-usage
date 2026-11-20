using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace AlData.AiUsage.Core;

/// <summary>UI translations. Dictionaries live in locales/&lt;code&gt;.json, one file per language.
/// A new language is a new file, no code changes. A missing key falls back to English.</summary>
public sealed partial class I18n
{
    public const string Default = "en";

    /// <summary>The config value that follows the Windows display language.</summary>
    public const string SystemCode = "system";

    static readonly ConcurrentDictionary<string, JsonObject> Tables = new();

    static string Dir => Path.Combine(AppPaths.Root, "locales");

    static JsonObject Table(string code) =>
        Tables.GetOrAdd(code, c => Json.ReadObject(Path.Combine(Dir, c + ".json")) ?? new JsonObject());

    /// <summary>(code, native name) for the menu. English first, the rest by name.</summary>
    public static List<(string Code, string Name)> Available()
    {
        var items = new List<(string Code, string Name)>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(Dir, "*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(path);
                items.Add((code, Table(code).Str("language.name", code)));
            }
        }
        catch (Exception)
        {
            // no locales folder: the menu shows nothing to pick
        }
        return items.OrderBy(i => i.Code != Default)
                    .ThenBy(i => i.Name.ToLowerInvariant(), StringComparer.Ordinal)
                    .ToList();
    }

    public static bool Exists(string code) => File.Exists(Path.Combine(Dir, code + ".json"));

    /// <summary>The locale file for the Windows display language: the exact tag (pt-BR), then the
    /// bare language (uk for uk-UA), then any regional file of that language (pt-BR for pt-PT).</summary>
    static string FromSystem()
    {
        try
        {
            var culture = CultureInfo.CurrentUICulture;
            if (Exists(culture.Name))
                return culture.Name;
            var bare = culture.TwoLetterISOLanguageName;
            if (Exists(bare))
                return bare;
            return Available().Select(i => i.Code)
                              .FirstOrDefault(c => c.StartsWith(bare + "-", StringComparison.OrdinalIgnoreCase)) ?? Default;
        }
        catch (Exception)
        {
            return Default;
        }
    }

    public I18n(string language = SystemCode)
    {
        Setting = language == SystemCode ? SystemCode : Exists(language) ? language : Default;
        Language = Setting == SystemCode ? FromSystem() : Setting;
    }

    /// <summary>What the config says: a locale code, or <see cref="SystemCode"/>.</summary>
    public string Setting { get; }

    /// <summary>The locale in use.</summary>
    public string Language { get; }

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex Placeholder();

    /// <summary>The translation of key with {name} placeholders filled; an unknown name stays as written.</summary>
    public string T(string key, IReadOnlyDictionary<string, object?>? args = null)
    {
        var text = Table(Language).Str(key, "") is { Length: > 0 } own ? own
            : Table(Default).Str(key, "") is { Length: > 0 } en ? en
            : key;
        if (args is null || args.Count == 0)
            return text;
        return Placeholder().Replace(text, m =>
            args.TryGetValue(m.Groups[1].Value, out var v) ? v?.ToString() ?? "" : m.Value);
    }

    public string T(string key, params (string Name, object? Value)[] args) =>
        T(key, args.ToDictionary(a => a.Name, a => a.Value));

    public Units Units => new(T("time.d"), T("time.h"), T("time.m"), T("time.now"));

    /// <summary>Labels of known gauges are localized; model names stay as they are.</summary>
    public string GaugeLabel(Gauge gauge)
    {
        var key = "gauge." + gauge.Key;
        return Table(Language).ContainsKey(key) || Table(Default).ContainsKey(key) ? T(key) : gauge.Label;
    }

    public string Error(Failure failure)
    {
        string? text = null;
        if (failure.Code is { } code)
        {
            text = T("err." + code, failure.Params);
            if (text == "err." + code)
                text = null;
        }
        text ??= failure.Message;
        return failure.Provider is { } provider ? $"{provider}: {text}" : text;
    }
}
