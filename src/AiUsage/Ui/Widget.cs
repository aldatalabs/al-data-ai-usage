using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Threading;
using AlData.AiUsage.Core;
using AlData.AiUsage.Providers;
using AlData.AiUsage.Ui.Layouts;

namespace AlData.AiUsage.Ui;

/// <summary>The widget: owns the config, the gauges and the window, tray and refresh loop around them.</summary>
public sealed partial class Widget
{
    const int MinRefresh = 60;
    const int StaleFactor = 3;
    /// <summary>The widget's bottom padding may hide past the screen edge: otherwise the position the
    /// user set shifts up as soon as the widget grows.</summary>
    const int BottomOverhang = 12;
    static readonly TimeSpan TickEvery = TimeSpan.FromSeconds(30);
    static readonly TimeSpan TopEvery = TimeSpan.FromMilliseconds(800);

    readonly JsonObject cfg;
    I18n i18n;
    List<LoadedProvider> providers;
    readonly List<string> localErrors = [];
    WidgetWindow window;
    readonly Refresher refresher;
    readonly Toasts toasts;
    readonly Tooltip tooltip;
    List<Gauge> gauges;
    DateTimeOffset? updatedAt, pausedUntil;
    List<Failure> failures = [];
    Theme theme;
    Layout layout;
    Tray? tray;
    bool hidden;
    string? systemSnapshot;

    public Widget()
    {
        cfg = Config.Load();
        window = new WidgetWindow(cfg.Flag("transparent", false));
        i18n = new I18n(cfg.Str("language", I18n.SystemCode));
        providers = ProviderRegistry.LoadAll(cfg);
        (gauges, updatedAt, pausedUntil) = Cache.Load();
        AdoptLegacyHidden();
        DropForeignCache();
        theme = MakeTheme();
        layout = MakeLayout();
        systemSnapshot = SystemSnapshot();
        toasts = new Toasts(() => window.Handle);
        tooltip = new Tooltip(() => window, TooltipText, () => theme);
        refresher = new Refresher(providers, Interval,
                                  result => window.Dispatcher.InvokeAsync(() => OnFetched(result)));
    }

    public void Start()
    {
        Wire();
        Redraw();
        window.Show();
        StartTray();
        UpdateTray();
        Every(TickEvery, Tick);
        Every(TopEvery, KeepOnTop);
        Every(ShuffleEvery, ShuffleTick);
        refresher.Start(InitialDelay());
    }

    /// <summary>What the widget asks of its window; again for each window that replaces it.</summary>
    void Wire()
    {
        window.Topmost = cfg.Flag("topmost", true);
        window.Locked = () => cfg.Flag("locked", false);
        window.DoubleClicked += RefreshNow;
        window.MenuRequested += PopupMenu;
        window.MouseEnter += (_, _) => tooltip.Schedule();
        window.MouseLeave += (_, _) => tooltip.Leave();
        window.PreviewMouseLeftButtonDown += (_, _) => tooltip.Hide();
        window.Moved += (x, y) =>
        {
            cfg["position"] = new JsonObject { ["x"] = x, ["y"] = y };
            Config.Save(cfg);
        };
        window.DpiChanged += (_, _) =>
        {
            layout = MakeLayout();
            Redraw();
        };
    }

    /// <summary>Transparency is a property of the window itself, so a new window takes the place
    /// of the old one. The new one is up before the old one goes: no flicker.</summary>
    void SetTransparent(bool value)
    {
        cfg["transparent"] = value;
        Config.Save(cfg);
        tooltip.Hide();
        var old = window;
        window = new WidgetWindow(value);
        Wire();
        layout = MakeLayout();
        Redraw();
        if (!hidden)
            window.Show();
        old.Close();
    }

    public void Quit()
    {
        refresher.Stop();
        tray?.Dispose();
        tray = null;
        Application.Current.Shutdown();
    }

    static void Every(TimeSpan period, Action action)
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = period };
        timer.Tick += (_, _) => action();
        timer.Start();
    }

    // --- data ------------------------------------------------------------
    /// <summary>Palette colors override the theme from the config.</summary>
    Theme MakeTheme() =>
        Theme.From(Json.Merge(cfg.Obj("theme") ?? new JsonObject(), Palettes.Colors(cfg.Str("palette", Palettes.Default))));

    Layout MakeLayout() => Skins.Make(cfg.Str("layout", "rows"), new LayoutOptions(
        theme, cfg.Num("scale", 1.0), cfg.Flag("show_reset", true), i18n, cfg.Flag("show_pace", true),
        cfg.Str("color_mode", "pace"), cfg.Int("icons", 8), cfg["lines"], window.PixelsPerDip, window.Transparent));

    int Interval => Math.Max(MinRefresh, cfg.Int("refresh_seconds", 600));

    /// <summary>Check marks are kept per account: a company account has other gauges than a personal
    /// one. Gauges without an account use the plain "hidden" list.</summary>
    HashSet<string> HiddenKeys(string provider, string account)
    {
        var settings = cfg.Obj("providers").Obj(provider);
        var list = account != "" ? settings.Obj("hidden_by_account")?[account] : settings?["hidden"];
        return list is JsonArray items ? items.Select(Json.AsString).OfType<string>().ToHashSet() : [];
    }

    /// <summary>Gauges minus the ones hidden via check marks in the sources menu.</summary>
    List<Gauge> Visible => gauges.Where(g => !HiddenKeys(g.Provider, g.Account).Contains(g.Key)).ToList();

    List<string> Errors => failures.Select(i18n.Error).Concat(localErrors).ToList();

    bool IsStale => updatedAt is { } at && (DateTimeOffset.UtcNow - at).TotalSeconds > Interval * StaleFactor;

    /// <summary>Check marks made before gauges carried an account belong to the account that was
    /// cached with them. One-time move into hidden_by_account; the old list stays for gauges
    /// without an account.</summary>
    void AdoptLegacyHidden()
    {
        var changed = false;
        var current = ProviderRegistry.Accounts(providers);
        foreach (var (provider, node) in cfg.Obj("providers") ?? new JsonObject())
        {
            if (node is not JsonObject settings || !Json.Truthy(settings["hidden"]) || settings.ContainsKey("hidden_by_account"))
                continue;
            var accounts = gauges.Where(g => g.Provider == provider && g.Account != "").Select(g => g.Account).ToHashSet();
            if (accounts.Count == 0 && current.TryGetValue(provider, out var signedIn) && signedIn != "")
                accounts = [signedIn];   // an old cache: the marks were made under the signed-in account
            if (accounts.Count != 1)
                continue;
            settings["hidden_by_account"] = new JsonObject { [accounts.First()] = settings["hidden"]!.DeepClone() };
            changed = true;
        }
        if (changed)
            Config.Save(cfg);
    }

    /// <summary>Cached numbers of another account must not be shown: the user switched accounts while
    /// the widget was down. Without them the first fetch happens right away.</summary>
    void DropForeignCache()
    {
        var current = ProviderRegistry.Accounts(providers);
        var kept = gauges.Where(g => !current.TryGetValue(g.Provider, out var account) || g.Account == account).ToList();
        if (kept.Count == gauges.Count)
            return;
        gauges = kept;
        updatedAt = null;
        pausedUntil = null;   // a new account means a new token and a fresh rate-limit window
    }

    /// <summary>A restart must not cost a request: honor both cache freshness and the pause.</summary>
    int InitialDelay()
    {
        if (gauges.Count > 0 && gauges.Any(g => g.Provider == ""))
            return 0;   // old-format cache: source check marks don't work without the provider
        var now = DateTimeOffset.UtcNow;
        double delay = 0;
        if (updatedAt is { } at)
            delay = Interval - (now - at).TotalSeconds;
        if (pausedUntil is { } until)
            delay = Math.Max(delay, (until - now).TotalSeconds);
        return (int)Math.Max(0, delay);
    }

    void OnFetched(FetchResult result)
    {
        if (result.Gauges.Count > 0)
        {
            gauges = result.Gauges;
            updatedAt = DateTimeOffset.UtcNow;
            pausedUntil = null;
            Cache.Save(gauges, updatedAt);
            CheckToasts();
        }
        if (result.RetryAfter is { } retry && result.Gauges.Count == 0)
            Cache.SetPause(retry);
        failures = result.Failures;
        Redraw();
        UpdateTray();
    }

    public void RefreshNow() => refresher.Wake();

    void CheckToasts()
    {
        var settings = cfg.Obj("toasts");
        if (!settings.Flag("enabled", true))
            return;
        var thresholds = (settings?["thresholds"] as JsonArray ?? []).Select(Json.AsDouble).OfType<double>().Order().ToList();
        toasts.Check(Visible, thresholds, theme, i18n, cfg.Str("color_mode", "pace") == "pace");
    }

    /// <summary>Reset timers keep ticking between requests.</summary>
    void Tick()
    {
        FollowSystemTheme();
        if (gauges.Count > 0)
            Redraw();
    }

    string? SystemSnapshot() =>
        cfg.Str("palette", Palettes.Default) == Palettes.SystemCode ? Json.Serialize(Palettes.SystemColors()) : null;

    /// <summary>The user may have switched Windows from the light theme to the dark one.</summary>
    void FollowSystemTheme()
    {
        var snapshot = SystemSnapshot();
        if (snapshot is null || snapshot == systemSnapshot)
        {
            systemSnapshot = snapshot;
            return;
        }
        systemSnapshot = snapshot;
        theme = MakeTheme();
        layout = MakeLayout();
        Redraw();
        UpdateTray();
    }

    /// <summary>The taskbar is topmost too, and a click on it puts the widget beneath it: the place in
    /// the topmost band has to be reasserted. Left alone while a menu or the tooltip is open, or it
    /// would cover them.</summary>
    void KeepOnTop()
    {
        if (cfg.Flag("topmost", true) && !hidden && !MenuOpen && !tooltip.IsOpen)
            window.KeepOnTop();
    }

    // --- drawing ---------------------------------------------------------
    void Redraw()
    {
        var shown = Visible;
        Size size;
        if (shown.Count > 0)
        {
            var current = layout;
            size = current.Size(shown);
            window.Paint(theme.Bg, dc => current.Draw(dc, shown));
        }
        else
        {
            var errors = Errors;
            size = window.PaintStatus(errors.Count > 0 ? errors[0] : i18n.T("status.loading"), theme.Bg, theme.Dim);
        }
        Place(size);
    }

    /// <summary>Size in DIPs; the position in the config is in physical pixels.</summary>
    void Place(Size size)
    {
        var ppd = window.PixelsPerDip;
        var width = (int)Math.Round(size.Width * ppd);
        var height = (int)Math.Round(size.Height * ppd);
        var screenW = Native.GetSystemMetrics(Native.SmCxScreen);
        var screenH = Native.GetSystemMetrics(Native.SmCyScreen);
        var position = cfg.Obj("position");
        int x, y;
        if (Json.AsInt(position?["x"]) is { } px && Json.AsInt(position?["y"]) is { } py)
            (x, y) = (px, py);
        else
            (x, y) = (screenW - width - 320, screenH - height - 4);
        x = Math.Max(0, Math.Min(x, screenW - width));
        y = Math.Max(0, Math.Min(y, screenH - height + BottomOverhang));
        window.Place(x, y, width, height);
    }

    // --- tooltip ---------------------------------------------------------
    /// <summary>The full data: exact percent, reset time, plan, refresh status.</summary>
    string TooltipText()
    {
        var lines = new List<string>();
        if (Plan() is { } plan)
            lines.Add($"Claude · {i18n.T("tip.plan")} {plan}");
        var shown = Visible;
        var labels = shown.Select(g => $"{g.Source} {i18n.GaugeLabel(g)}".Trim()).ToList();
        var width = labels.Select(l => l.Length).DefaultIfEmpty(0).Max();
        var unknown = i18n.T("time.unknown");
        foreach (var (gauge, label) in shown.Zip(labels))
        {
            var tail = $"{i18n.T("tip.resets")} {Model.ResetFull(gauge.ResetsAt, unknown)}";
            if (Model.Countdown(gauge.ResetsAt, i18n.Units) is { Length: > 0 } left)
                tail += $" ({left})";
            if (Model.Pace(gauge) is { } forecast)
            {
                tail += $"  {i18n.T("pace.projected", ("n", (int)Math.Round(forecast.Projected * 100)))}";
                if (forecast.ExhaustAt is { } exhaustAt)
                    tail += $" ⚠ {i18n.T("pace.out", ("time", Clock(exhaustAt, "HH:mm")))}";
            }
            var value = Model.Percent(gauge) is { } pct ? $"{pct}%" : "—";
            if (gauge.Detail.Length > 0)
                value += $" ({gauge.Detail})";
            lines.Add($"{label.PadRight(width)}  {value,4}  {tail}");
        }
        if (updatedAt is { } at)
        {
            var line = $"{i18n.T("tip.updated")} {Clock(at, "HH:mm:ss")}";
            if (IsStale)
                line += $" · {i18n.T("tip.stale")}";
            lines.Add(line);
        }
        lines.AddRange(Errors);
        if (lines.Count == 0)
            lines.Add(i18n.T("status.no_data"));
        return string.Join("\n", lines);
    }

    static string Clock(DateTimeOffset at, string format) => at.ToLocalTime().ToString(format, CultureInfo.InvariantCulture);

    // --- tray ------------------------------------------------------------
    void StartTray()
    {
        if (!cfg.Obj("tray").Flag("enabled", true))
            return;
        try
        {
            tray = new Tray();
            tray.LeftClick += ToggleHidden;
            tray.RightClick += PopupMenu;
        }
        catch (Exception e)
        {
            localErrors.Add(i18n.T("err.tray", ("detail", e.Message)));
        }
    }

    void UpdateTray()
    {
        if (tray is null)
            return;
        var known = Visible.Where(g => g.Value is not null).ToList();
        var worst = known.OrderByDescending(g => g.Value).FirstOrDefault();
        var color = Model.LevelColor(worst?.Value, theme.Levels, theme.Dim);
        var tip = string.Join(" · ", known.Select(g => $"{g.Source} {i18n.GaugeLabel(g)} {Model.Percent(g)}%".Trim()));
        tray.Update(worst?.Value, color, tip.Length > 0 ? tip : "al-data-ai-usage");
    }

    void ToggleHidden()
    {
        hidden = !hidden;
        tooltip.Hide();
        if (hidden)
        {
            window.Hide();
            return;
        }
        window.Show();
        window.Topmost = cfg.Flag("topmost", true);
        Redraw();
    }
}
