using System;
using System.Globalization;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace AlData.AiUsage.Core;

/// <summary>Loose readers over System.Text.Json nodes: config and API values come in whatever shape
/// they were written, and a wrong shape reads as missing, never as an exception.</summary>
public static class Json
{
    static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    static readonly JsonSerializerOptions Compact = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    static readonly JsonDocumentOptions Lenient = new() { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip };

    public static JsonNode? Parse(string text)
    {
        try
        {
            return JsonNode.Parse(text, documentOptions: Lenient);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static JsonObject? ReadObject(string path)
    {
        try
        {
            return Parse(File.ReadAllText(path)) as JsonObject;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Write through a temp file, so a crash never leaves half a file behind.</summary>
    public static void WriteAtomic(string path, JsonNode node, bool indented)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, node.ToJsonString(indented ? Indented : Compact));
        File.Move(tmp, path, overwrite: true);
    }

    public static string Serialize(JsonNode node) => node.ToJsonString(Compact);

    public static double? AsDouble(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        switch (value.GetValueKind())
        {
            case JsonValueKind.Number:
                if (value.TryGetValue<double>(out var d)) return d;
                if (value.TryGetValue<long>(out var l)) return l;
                if (value.TryGetValue<int>(out var i)) return i;
                if (value.TryGetValue<decimal>(out var m)) return (double)m;
                return double.TryParse(value.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var raw)
                    ? raw
                    : null;
            case JsonValueKind.String:
                return double.TryParse(value.GetValue<string>(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                return null;
        }
    }

    public static int? AsInt(JsonNode? node) => AsDouble(node) is { } d && !double.IsNaN(d) ? (int)Math.Clamp(d, int.MinValue, int.MaxValue) : null;

    public static string? AsString(JsonNode? node)
    {
        if (node is not JsonValue value)
            return null;
        return value.GetValueKind() switch
        {
            JsonValueKind.String => value.GetValue<string>(),
            JsonValueKind.Number => value.ToJsonString(),
            _ => null,
        };
    }

    /// <summary>Truthiness, as the Tk prototype read the config: null, false, 0, "" and empty containers are false.</summary>
    public static bool Truthy(JsonNode? node) => node switch
    {
        null => false,
        JsonObject o => o.Count > 0,
        JsonArray a => a.Count > 0,
        _ => node.GetValueKind() switch
        {
            JsonValueKind.True => true,
            JsonValueKind.Number => AsDouble(node) is { } d && d != 0,
            JsonValueKind.String => AsString(node) != "",
            _ => false,
        },
    };

    /// <summary>A copy of base with over laid on top; nested objects merge, everything else replaces.</summary>
    public static JsonObject Merge(JsonObject @base, JsonObject? over)
    {
        var result = (JsonObject)@base.DeepClone();
        if (over is null)
            return result;
        foreach (var (key, value) in over)
        {
            if (value is JsonObject child && result[key] is JsonObject existing)
                result[key] = Merge(existing, child);
            else
                result[key] = value?.DeepClone();
        }
        return result;
    }
}
