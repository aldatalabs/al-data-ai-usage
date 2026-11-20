using System;
using System.Collections.Generic;

namespace AlData.AiUsage.Core;

/// <summary>A single gauge. Value is a fraction 0..1, null = unknown.</summary>
public sealed class Gauge
{
    public required string Key { get; init; }
    public required string Label { get; init; }
    public double? Value { get; init; }
    public DateTimeOffset? ResetsAt { get; init; }
    public string Hint { get; init; } = "";
    /// <summary>Length of the limit window; without it there is no projection.</summary>
    public int? WindowSeconds { get; init; }
    /// <summary>Whose limit this is: Claude, Codex, ...</summary>
    public string Source { get; set; } = "";
    /// <summary>Provider key, e.g. claude_usage.</summary>
    public string Provider { get; set; } = "";
    /// <summary>Extra tooltip line, e.g. "$57 / $2000".</summary>
    public string Detail { get; init; } = "";
    /// <summary>Opaque id of the account the numbers belong to.</summary>
    public string Account { get; set; } = "";
}

/// <summary>Projection: Elapsed is how much of the window has passed, Projected the level at reset.</summary>
public sealed record Pace(double Elapsed, double Projected, DateTimeOffset? ExhaustAt)
{
    /// <summary>Enough until reset.</summary>
    public bool Ok => ExhaustAt is null;
}

/// <summary>A provider failure, ready for i18n: Code and Params pick the translation, Message is
/// the English fallback.</summary>
public sealed record Failure(string? Provider, string Message, string? Code = null,
                             IReadOnlyDictionary<string, object?>? Params = null, string Kind = "error");

/// <summary>Provider failure. RetryAfter is how many seconds to leave the source alone.</summary>
public sealed class ProviderError : Exception
{
    public ProviderError(string message, string? code = null, int? retryAfter = null, string kind = "error",
                         IReadOnlyDictionary<string, object?>? parameters = null) : base(message)
    {
        Code = code;
        RetryAfter = retryAfter;
        Kind = kind;
        Params = parameters ?? new Dictionary<string, object?>();
    }

    public string? Code { get; }
    public int? RetryAfter { get; }
    public string Kind { get; }
    public IReadOnlyDictionary<string, object?> Params { get; }

    public Failure ToFailure(string? provider) => new(provider, Message, Code, Params, Kind);
}
