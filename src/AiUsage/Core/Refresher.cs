using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AlData.AiUsage.Providers;

namespace AlData.AiUsage.Core;

/// <summary>The background fetch loop. Sleeps in slices: a provider may ask to retry earlier, a
/// menu change of the interval is picked up on the fly, and Wake() serves "refresh now".</summary>
public sealed class Refresher
{
    /// <summary>How often during a pause to ask providers whether to retry early.</summary>
    const int EarlyCheckSeconds = 60;

    readonly SemaphoreSlim wake = new(0, 1);
    readonly CancellationTokenSource stop = new();
    readonly Action<FetchResult> deliver;
    volatile List<LoadedProvider> providers;
    volatile int interval;

    /// <summary>deliver runs on the worker thread; marshal to the UI there.</summary>
    public Refresher(List<LoadedProvider> providers, int interval, Action<FetchResult> deliver)
    {
        this.providers = providers;
        this.interval = interval;
        this.deliver = deliver;
    }

    public List<LoadedProvider> Providers
    {
        get => providers;
        set => providers = value;
    }

    public int Interval
    {
        get => interval;
        set => interval = value;
    }

    public void Start(int initialDelay) => Task.Run(() => LoopAsync(initialDelay));

    public void Wake()
    {
        try
        {
            wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // already woken
        }
    }

    public void Stop() => stop.Cancel();

    async Task LoopAsync(int initialDelay)
    {
        try
        {
            await SleepAsync(initialDelay, dynamic: false).ConfigureAwait(false);
            while (!stop.IsCancellationRequested)
            {
                var result = await ProviderRegistry.FetchAllAsync(providers, stop.Token).ConfigureAwait(false);
                deliver(result);
                await SleepAsync(result.RetryAfter ?? interval, dynamic: result.RetryAfter is null).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
        catch (Exception e)
        {
            Trace.TraceError($"refresh loop died: {e}");
        }
    }

    async Task SleepAsync(int seconds, bool dynamic)
    {
        var clock = Stopwatch.StartNew();
        while (!stop.IsCancellationRequested)
        {
            var target = dynamic ? interval : seconds;
            var left = target - clock.Elapsed.TotalSeconds;
            if (left <= 0)
                break;
            if (await wake.WaitAsync(TimeSpan.FromSeconds(Math.Min(EarlyCheckSeconds, left)), stop.Token).ConfigureAwait(false))
                break;
            if (ProviderRegistry.WantsEarlyRetry(providers))
                break;
        }
        while (wake.CurrentCount > 0)
            wake.Wait(0);
    }
}
