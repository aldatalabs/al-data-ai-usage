using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using AlData.AiUsage.Core;

namespace AlData.AiUsage.Providers;

/// <summary>A source of gauges. Name is the key under "providers" in the config.</summary>
public interface IProvider
{
    string Name { get; }
    string Title { get; }

    /// <summary>Throws ProviderError for anything the user should read about.</summary>
    Task<List<Gauge>> FetchAsync(JsonObject settings, CancellationToken ct);

    /// <summary>Id of the account a fetch would be for right now; "" when there is no such notion.</summary>
    string? Account(JsonObject settings) => null;

    /// <summary>Whether the pause no longer makes sense, e.g. the token was refreshed.</summary>
    bool WantsEarlyRetry(JsonObject settings) => false;
}

public sealed record LoadedProvider(string Name, IProvider? Provider, JsonObject Settings, Failure? Failure);

public sealed record FetchResult(List<Gauge> Gauges, List<Failure> Failures, int? RetryAfter);

/// <summary>The built-in providers and the ones the config turns on. The core sees only Gauges.</summary>
public static class ProviderRegistry
{
    /// <summary>One instance per provider for the life of the process: they keep backoff state.</summary>
    static readonly Dictionary<string, IProvider> Known = new IProvider[]
    {
        new ClaudeUsage(),
        new CodexUsage(),
    }.ToDictionary(p => p.Name);

    public static IEnumerable<string> Names => Known.Keys.Order(StringComparer.Ordinal);

    /// <summary>(key, title, enabled) for every provider, for the menu.</summary>
    public static List<(string Key, string Title, bool Enabled)> Catalog(JsonObject cfg)
    {
        var configured = cfg.Obj("providers");
        return Names.Select(name =>
        {
            var enabled = configured is not null && configured.ContainsKey(name)
                          && configured.Obj(name).Flag("enabled", true);
            return (name, Known[name].Title, enabled);
        }).ToList();
    }

    /// <summary>Settings are copied: the worker reads them while the menu may change the config.</summary>
    public static List<LoadedProvider> LoadAll(JsonObject cfg)
    {
        var loaded = new List<LoadedProvider>();
        foreach (var (name, node) in cfg.Obj("providers") ?? new JsonObject())
        {
            var settings = node is JsonObject o ? (JsonObject)o.DeepClone() : new JsonObject();
            if (!settings.Flag("enabled", true))
                continue;
            if (Known.TryGetValue(name, out var provider))
                loaded.Add(new LoadedProvider(name, provider, settings, null));
            else
                loaded.Add(new LoadedProvider(name, null, settings,
                    new Failure(name, $"provider {name} not found", "provider_missing",
                                new Dictionary<string, object?> { ["name"] = name })));
        }
        return loaded;
    }

    public static bool WantsEarlyRetry(IEnumerable<LoadedProvider> loaded)
    {
        foreach (var item in loaded)
        {
            try
            {
                if (item.Provider?.WantsEarlyRetry(item.Settings) == true)
                    return true;
            }
            catch (Exception)
            {
                // a broken check never blocks the others
            }
        }
        return false;
    }

    /// <summary>Provider to the id of the account it would fetch for now.</summary>
    public static Dictionary<string, string> Accounts(IEnumerable<LoadedProvider> loaded)
    {
        var found = new Dictionary<string, string>();
        foreach (var item in loaded)
        {
            if (item.Provider is null)
                continue;
            try
            {
                if (item.Provider.Account(item.Settings) is { } account)
                    found[item.Name] = account;
            }
            catch (Exception)
            {
                found[item.Name] = "";
            }
        }
        return found;
    }

    /// <summary>Gauges, failures for i18n, and the largest RetryAfter.</summary>
    public static async Task<FetchResult> FetchAllAsync(IEnumerable<LoadedProvider> loaded, CancellationToken ct)
    {
        var gauges = new List<Gauge>();
        var failures = new List<Failure>();
        int? retryAfter = null;
        foreach (var item in loaded)
        {
            if (item.Provider is null)
            {
                failures.Add(item.Failure ?? new Failure(item.Name, "provider broken", "provider_broken"));
                continue;
            }
            try
            {
                foreach (var gauge in await item.Provider.FetchAsync(item.Settings, ct).ConfigureAwait(false))
                {
                    if (gauge.Source == "")
                        gauge.Source = item.Provider.Title;
                    gauge.Provider = item.Name;
                    gauges.Add(gauge);
                }
            }
            catch (ProviderError e)
            {
                failures.Add(e.ToFailure(item.Name));
                if (e.RetryAfter is > 0)
                    retryAfter = Math.Max(retryAfter ?? 0, e.RetryAfter.Value);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception e)
            {
                failures.Add(new Failure(item.Name, $"{e.GetType().Name}: {e.Message}"));
            }
        }
        return new FetchResult(gauges, failures, retryAfter);
    }
}
