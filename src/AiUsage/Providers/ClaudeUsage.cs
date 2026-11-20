using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AlData.AiUsage.Core;

namespace AlData.AiUsage.Providers;

/// <summary>Claude Code limits. The token is read from %USERPROFILE%\.claude\.credentials.json,
/// read-only. Which account the token belongs to comes from %USERPROFILE%\.claude.json
/// (oauthAccount), also read-only. Claude Code rewrites both on /login, so a switch between a
/// personal and a company account is visible without a request.</summary>
public sealed class ClaudeUsage : IProvider
{
    public string Name => "claude_usage";
    public string Title => "Claude";

    static readonly string Home = Environment.GetEnvironmentVariable("USERPROFILE") is { Length: > 0 } home
        ? home
        : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    static readonly string CredPath = Path.Combine(Home, ".claude", ".credentials.json");
    static readonly string AccountPath = Path.Combine(Home, ".claude.json");
    const string Api = "https://api.anthropic.com/api/oauth";
    const string Beta = "oauth-2025-04-20";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // The /usage limit is counted per access token: a few requests and the 429 holds for hours,
    // sometimes with Retry-After: 0. Hence a long backoff, not a fixed 15 min.
    static readonly int[] Backoff = [900, 1800, 3600];
    const int MinPause = 300;

    readonly object gate = new();
    string? fingerprint;
    int fails;
    string? fetchedAccount;
    // .claude.json grows with every project Claude Code has seen; parse it only when it changed.
    DateTime? accountStamp;
    (string Id, string Label) accountValue = ("", "");

    /// <summary>A fingerprint to notice a token change without keeping the token itself.</summary>
    static string Fingerprint(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)))[..16];

    /// <summary>Retry-After if the server sent a meaningful one; otherwise our own backoff.</summary>
    int Pause(HttpResponseMessage response)
    {
        var hinted = 0;
        if (response.Headers.RetryAfter?.Delta is { } delta)
            hinted = (int)delta.TotalSeconds;
        lock (gate)
        {
            fails++;
            var fallback = Backoff[Math.Min(fails - 1, Backoff.Length - 1)];
            return Math.Max(Math.Max(hinted, fallback), MinPause);
        }
    }

    /// <summary>(id, label) of the signed-in account: the organization, since limits are per
    /// organization. Empty strings when .claude.json is missing or has no oauthAccount.</summary>
    (string Id, string Label) SignedIn()
    {
        DateTime stamp;
        try
        {
            if (!File.Exists(AccountPath))
                return ("", "");
            stamp = File.GetLastWriteTimeUtc(AccountPath);
        }
        catch (Exception)
        {
            return ("", "");
        }
        lock (gate)
            if (stamp == accountStamp)
                return accountValue;
        (string, string) value = ("", "");
        if (Json.ReadObject(AccountPath)?.Obj("oauthAccount") is { } info)
        {
            var ident = Json.Truthy(info["organizationUuid"]) ? info.Str("organizationUuid", "")
                : info.Str("accountUuid", "");
            var email = info.Str("emailAddress", "");
            var label = info.Str("organizationName", "") is { Length: > 0 } org ? org : email;
            if (label.EndsWith("'s Organization", StringComparison.Ordinal) && email != "")
                label = email;   // the auto-named personal organization: the email says more
            value = (ident, label);
        }
        lock (gate)
        {
            accountStamp = stamp;
            accountValue = value;
        }
        return value;
    }

    public string? Account(JsonObject settings) => SignedIn().Id;

    static JsonObject OAuth()
    {
        string text;
        try
        {
            text = File.ReadAllText(CredPath);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ProviderError("no .credentials.json - run claude in a terminal", "no_credentials", kind: "auth");
        }
        catch (Exception e)
        {
            throw new ProviderError($"cannot read .credentials.json: {e.Message}", "unreadable",
                                    parameters: new Dictionary<string, object?> { ["detail"] = e.Message });
        }
        if (Json.Parse(text) is not JsonObject data)
            throw new ProviderError("cannot read .credentials.json: not JSON", "unreadable",
                                    parameters: new Dictionary<string, object?> { ["detail"] = "not JSON" });
        var oauth = data.Obj("claudeAiOauth") ?? new JsonObject();
        if (!Json.Truthy(oauth["accessToken"]))
            throw new ProviderError("no accessToken in .credentials.json", "no_token", kind: "auth");
        return oauth;
    }

    /// <summary>A single request. Every failure becomes a ProviderError with a code for i18n.</summary>
    async Task<JsonObject> GetAsync(string path, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Api}/{path}");
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        request.Headers.TryAddWithoutValidation("anthropic-beta", Beta);
        request.Headers.TryAddWithoutValidation("Accept", "application/json");
        request.Headers.TryAddWithoutValidation("User-Agent", "al-data-ai-usage");
        HttpResponseMessage response;
        string body;
        try
        {
            response = await Http.SendAsync(request, ct).ConfigureAwait(false);
        }
        catch (HttpRequestException e)
        {
            throw Network(e.InnerException?.Message ?? e.Message);
        }
        catch (TaskCanceledException) when (!ct.IsCancellationRequested)
        {
            throw Network("timed out");
        }
        using (response)
        {
            var status = (int)response.StatusCode;
            if (status == 429)
            {
                var pause = Pause(response);
                throw new ProviderError($"429: too many requests, pausing {pause / 60} min", "rate_limited", pause, "rate",
                                        new Dictionary<string, object?> { ["minutes"] = pause / 60 });
            }
            if (status is 401 or 403)
                throw new ProviderError($"{status}: token expired - run claude in a terminal", "auth", kind: "auth",
                                        parameters: new Dictionary<string, object?> { ["status"] = status });
            if (!response.IsSuccessStatusCode)
                throw new ProviderError($"HTTP {status}", "http",
                                        parameters: new Dictionary<string, object?> { ["status"] = status });
            try
            {
                body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (HttpRequestException e)
            {
                throw Network(e.Message);
            }
        }
        return Json.Parse(body) as JsonObject ?? throw new ProviderError("response is not JSON", "bad_json");
    }

    static ProviderError Network(string detail) =>
        new($"network: {detail}", "network", parameters: new Dictionary<string, object?> { ["detail"] = detail });

    /// <summary>utilization and percent arrive on a 0..100 scale.</summary>
    static double? Fraction(JsonNode? raw) => Json.AsDouble(raw) is { } v ? Model.Clamp01(v / 100.0) : null;

    /// <summary>dict.get(first, dict.get(second)): a present key wins even when it is null.</summary>
    static JsonNode? Pick(JsonObject o, string first, string second) => o.ContainsKey(first) ? o[first] : o[second];

    static string Money(JsonNode? raw) => Money(Json.AsDouble(raw) ?? 0);

    static string Money(double value) => "$" + value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>The third gauge moved from seven_day_sonnet to limits[].</summary>
    static (string Name, double? Value, JsonNode? ResetsAt)? ModelLimit(JsonObject data)
    {
        if (data["limits"] is JsonArray limits)
        {
            foreach (var node in limits)
            {
                if (node is not JsonObject item)
                    continue;
                var name = item.Obj("scope").Obj("model").Str("display_name", "");
                if (name != "")
                    return (name, Fraction(Pick(item, "percent", "utilization")), item["resets_at"]);
            }
        }
        if (data["seven_day_sonnet"] is JsonObject legacy)
            return ("Sonnet", Fraction(legacy["utilization"]), legacy["resets_at"]);
        return null;
    }

    /// <summary>Money budget block: any object with limit_dollars and used_dollars. The block has
    /// an internal name, so it is found by shape, not by key.</summary>
    static JsonObject? Budget(JsonObject data)
    {
        foreach (var pair in data)
        {
            if (pair.Value is JsonObject block && block.ContainsKey("limit_dollars") && block.ContainsKey("used_dollars")
                && Json.AsDouble(block["limit_dollars"]) is > 0 && block.HasValue("utilization"))
                return block;
        }
        return null;
    }

    /// <summary>Calendar month in UTC: the spend limit resets on the 1st at 00:00 UTC.</summary>
    static (DateTimeOffset Start, DateTimeOffset End) MonthBounds()
    {
        var now = DateTimeOffset.UtcNow;
        var start = new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, TimeSpan.Zero);
        return (start, start.AddMonths(1));
    }

    /// <summary>Included usage is granted from the 1st of the month before the end date (say,
    /// October 1 to November 14). A different date goes in the included_start key (YYYY-MM-DD).</summary>
    static int? IncludedWindow(DateTimeOffset? expires, JsonNode? over)
    {
        if (expires is not { } end)
            return null;
        var start = Json.Truthy(over) ? Model.ParseIso(over) : null;
        if (start is null)
        {
            var first = new DateTimeOffset(end.Year, end.Month, 1, 0, 0, 0, TimeSpan.Zero);
            start = first.AddMonths(-1);
        }
        var seconds = (end - start.Value).TotalSeconds;
        return seconds >= 1 ? (int)Math.Min(seconds, int.MaxValue) : null;
    }

    /// <summary>Extra credit beyond the plan: spend.used / spend.limit in minor units.</summary>
    static (double? Value, string Detail)? Extra(JsonObject data)
    {
        if (data["spend"] is not JsonObject spend || !Json.Truthy(spend["enabled"]))
            return null;
        if (spend["used"] is not JsonObject used || spend["limit"] is not JsonObject limit)
            return null;
        if (Amount(limit) is not { } cap || cap <= 0 || Amount(used) is not { } spent)
            return null;
        return (Model.Clamp01(spent / cap), $"{Money(spent)} / {Money(cap)}");
    }

    static double? Amount(JsonObject money)
    {
        var exponent = money.ContainsKey("exponent") ? Json.AsInt(money["exponent"]) : 2;
        if (exponent is null || Json.AsDouble(money["amount_minor"]) is not { } minor)
            return null;
        return minor / Math.Pow(10, exponent.Value);
    }

    /// <summary>Whether to cut the wait short: the user switched accounts, or Claude Code issued a
    /// new token while we are sitting out a 429.</summary>
    public bool WantsEarlyRetry(JsonObject settings)
    {
        string? account;
        int failed;
        string? known;
        lock (gate)
        {
            account = fetchedAccount;
            failed = fails;
            known = fingerprint;
        }
        if (account is not null && SignedIn().Id != account)
            return true;   // the numbers on screen belong to another account
        if (failed == 0)
            return false;
        string token;
        try
        {
            token = OAuth().Str("accessToken", "");
        }
        catch (ProviderError)
        {
            return false;
        }
        return Fingerprint(token) != known;
    }

    public async Task<List<Gauge>> FetchAsync(JsonObject settings, CancellationToken ct)
    {
        var oauth = OAuth();
        var token = oauth.Str("accessToken", "");
        var print = Fingerprint(token);
        lock (gate)
        {
            if (print != fingerprint)
            {
                fingerprint = print;
                fails = 0;   // a fresh token: a fresh limit window
            }
        }

        var (ident, label) = SignedIn();
        var data = await GetAsync("usage", token, ct).ConfigureAwait(false);
        lock (gate)
        {
            fails = 0;
            fetchedAccount = ident;
        }
        var plan = oauth.Str("subscriptionType", "") is { Length: > 0 } p ? p : "?";
        var hint = $"plan {plan}" + (label != "" ? $" · {label}" : "");
        var gauges = new List<Gauge>();

        // Enterprise: a money budget instead of windows.
        if (Budget(data) is { } budget)
        {
            var expires = Model.ParseIso(budget["resets_at"]);
            gauges.Add(new Gauge
            {
                Key = "budget",
                Label = "Included",
                Value = Fraction(budget["utilization"]),
                ResetsAt = expires,
                Hint = hint,
                WindowSeconds = IncludedWindow(expires, settings["included_start"]),
                Detail = $"{Money(budget["used_dollars"])} / {Money(budget["limit_dollars"])}",
            });
        }

        foreach (var (key, name, window) in new[] { ("five_hour", "5h", 5 * 3600), ("seven_day", "7d", 7 * 86400) })
        {
            if (data[key] is not JsonObject block)
                continue;
            gauges.Add(new Gauge
            {
                Key = key,
                Label = name,
                Value = Fraction(Pick(block, "utilization", "percent")),
                ResetsAt = Model.ParseIso(block["resets_at"]),
                Hint = hint,
                WindowSeconds = window,
            });
        }

        if (ModelLimit(data) is { } model)
        {
            gauges.Add(new Gauge
            {
                Key = "model",
                Label = model.Name,
                Value = model.Value,
                ResetsAt = Model.ParseIso(model.ResetsAt),
                Hint = hint,
                WindowSeconds = 7 * 86400,
            });
        }

        if (Extra(data) is { } extra)
        {
            var (start, end) = MonthBounds();
            gauges.Add(new Gauge
            {
                Key = "extra",
                Label = "Extra",
                Value = extra.Value,
                ResetsAt = end,
                Hint = hint,
                WindowSeconds = (int)(end - start).TotalSeconds,
                Detail = extra.Detail,
            });
        }

        if (gauges.Count == 0)
            throw new ProviderError("no usage limits in response", "no_limits");
        foreach (var gauge in gauges)
            gauge.Account = ident;
        return gauges;
    }
}
