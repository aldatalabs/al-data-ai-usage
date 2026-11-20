using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlData.AiUsage.Core;

/// <summary>Where things live. The JSON data (locales, palettes, defaults) ships beside the exe;
/// the user's config and cache live in %LOCALAPPDATA%.</summary>
public static class AppPaths
{
    public const string App = "al-data-ai-usage";

    public static string Root { get; } = AppContext.BaseDirectory;

    public static string UserDir { get; } = Path.Combine(
        Environment.GetEnvironmentVariable("LOCALAPPDATA") is { Length: > 0 } local
            ? local
            : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        App);
}

/// <summary>Config: defaults from config.default.json, the user's copy in %LOCALAPPDATA%.
/// Kept as a JSON tree, not a class: keys are append-only and unknown keys must survive a save.</summary>
public static class Config
{
    public static string DefaultPath => Path.Combine(AppPaths.Root, "config.default.json");
    public static string UserPath => Path.Combine(AppPaths.UserDir, "config.json");

    public static JsonObject Defaults() => Json.ReadObject(DefaultPath) ?? new JsonObject();

    public static JsonObject Load()
    {
        var @base = Defaults();
        if (File.Exists(UserPath))
            return Json.ReadObject(UserPath) is { } user ? Json.Merge(@base, user) : @base;
        Save(@base);
        return @base;
    }

    public static void Save(JsonObject cfg)
    {
        try
        {
            Json.WriteAtomic(UserPath, cfg, indented: true);
        }
        catch (Exception)
        {
            // a read-only profile must not take the widget down
        }
    }

    // --- loose getters: a missing or mistyped key is the default ---------

    public static JsonObject? Obj(this JsonObject? o, string key) => o?[key] as JsonObject;

    public static double Num(this JsonObject? o, string key, double fallback) =>
        Json.AsDouble(o?[key]) ?? fallback;

    public static int Int(this JsonObject? o, string key, int fallback) =>
        Json.AsInt(o?[key]) ?? fallback;

    public static string Str(this JsonObject? o, string key, string fallback) =>
        Json.AsString(o?[key]) ?? fallback;

    /// <summary>bool(cfg.get(key, default)): a missing key is the default, a present one is truthy or not.</summary>
    public static bool Flag(this JsonObject? o, string key, bool fallback) =>
        o is not null && o.ContainsKey(key) ? Json.Truthy(o[key]) : fallback;

    /// <summary>The child object at key, created when missing or of the wrong shape.</summary>
    public static JsonObject Child(this JsonObject o, string key)
    {
        if (o[key] is JsonObject existing)
            return existing;
        var created = new JsonObject();
        o[key] = created;
        return created;
    }

    public static bool HasValue(this JsonObject? o, string key) =>
        o?[key] is { } node && node.GetValueKind() != JsonValueKind.Null;
}
