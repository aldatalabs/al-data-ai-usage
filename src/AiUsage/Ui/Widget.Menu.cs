using System;
using System.Linq;
using System.Text.Json.Nodes;
using AlData.AiUsage.Core;
using AlData.AiUsage.Providers;
using AlData.AiUsage.Ui.Layouts;

namespace AlData.AiUsage.Ui;

public sealed partial class Widget
{
    /// <summary>Refresh interval choices, in seconds.</summary>
    static readonly int[] Intervals = [60, 180, 300, 600, 1800, 3600];
    /// <summary>Minutes between skin and palette changes; 0 is off.</summary>
    static readonly int[] ShuffleChoices = [0, 10, 60, 1440];
    /// <summary>Icons per gauge in the icon skins.</summary>
    static readonly int[] IconChoices = [5, 8, 10];
    /// <summary>How many lines to lay the gauges out in.</summary>
    static readonly string[] LineChoices = ["auto", "1", "2", "3"];
    static readonly TimeSpan ShuffleEvery = TimeSpan.FromMinutes(1);
    const string UsageUrl = "https://claude.ai/settings/usage";
    const string RepoUrl = "https://github.com/aldatalabs/al-data-ai-usage";
    /// <summary>The feedback form on the site; the address is permanent once a build ships with it.</summary>
    const string FeedbackPage = "https://aldatalabs.com/ai-usage-monitor/feedback";
    const string SupportEmail = "support@aldatalabs.com";
    /// <summary>The product page in the Store app, by the Store ID from Partner Center; a build
    /// from source has no package family to name it by.</summary>
    const string StoreUrl = "ms-windows-store://pdp/?ProductId=9N5VRJ8TLQ46";

    Menu? menu;
    long shuffledAt = Environment.TickCount64;
    bool checkingUpdates;

    bool MenuOpen => menu?.IsOpen == true;

    /// <summary>At the cursor, from the widget or the tray.</summary>
    void PopupMenu()
    {
        tooltip.Hide();
        menu?.Close();
        menu = BuildMenu();
        if (Native.GetCursorPos(out var p))
            menu.Popup(p.X, p.Y);
    }

    Menu BuildMenu()
    {
        string T(string key) => i18n.T(key);
        var root = new Menu(BuildMenu);
        root.AddCommand(T("menu.refresh"), RefreshNow, icon: Icons.Refresh);

        var look = root.AddSubmenu(T("menu.appearance"), Icons.Appearance);
        var skins = look.AddSubmenu(T("menu.skin"), Icons.Skin);
        var current = cfg.Str("layout", "rows");
        foreach (var name in Skins.Names)
            skins.AddRadio(T($"layout.{name}"), name == current, () => SetLayout(name));

        var intervals = root.AddSubmenu(T("menu.interval"), Icons.Interval);
        foreach (var seconds in Intervals)
            intervals.AddRadio(IntervalLabel(seconds), seconds == Interval, () => SetInterval(seconds));

        var palettes = look.AddSubmenu(T("menu.palette"), Icons.Palette);
        var currentPalette = cfg.Str("palette", Palettes.Default);
        foreach (var (code, title) in Palettes.Available())
            palettes.AddRadio(title, code == currentPalette, () => SetPalette(code));

        var icons = look.AddSubmenu(T("menu.icons"), Icons.Count);
        var currentIcons = cfg.Int("icons", 8);
        foreach (var count in IconChoices)
            icons.AddRadio(count.ToString(), count == currentIcons, () => SetOption("icons", count));

        var lines = look.AddSubmenu(T("menu.lines"), Icons.Lines);
        var currentLines = cfg.HasValue("lines") ? Json.AsString(cfg["lines"]) : "auto";
        foreach (var count in LineChoices)
            lines.AddRadio(count == "auto" ? T("lines.auto") : count, count == currentLines,
                           () => SetOption("lines", count == "auto" ? JsonValue.Create(count) : JsonValue.Create(int.Parse(count))));

        var shuffle = look.AddSubmenu(T("menu.shuffle"), Icons.Shuffle);
        var currentShuffle = cfg.Int("shuffle_minutes", 0);
        foreach (var minutes in ShuffleChoices)
            shuffle.AddRadio(ShuffleLabel(minutes), minutes == currentShuffle, () => SetShuffle(minutes));

        var colors = look.AddSubmenu(T("menu.color"), Icons.Color);
        var mode = cfg.Str("color_mode", "pace");
        foreach (var value in new[] { "usage", "pace" })
            colors.AddRadio(T($"color.{value}"), value == mode, () => SetOption("color_mode", value));

        look.AddCheck(T("menu.transparent"), window.Transparent, () => SetTransparent(!window.Transparent), Icons.Transparent);

        var languages = root.AddSubmenu(T("menu.language"), Icons.Language);
        languages.AddRadio(T("language.system"), i18n.Setting == I18n.SystemCode, () => SetLanguage(I18n.SystemCode));
        foreach (var (code, title) in I18n.Available())
            languages.AddRadio(title, code == i18n.Setting, () => SetLanguage(code));

        var sources = root.AddSubmenu(T("menu.sources"), Icons.Sources);
        foreach (var (key, title, enabled) in ProviderRegistry.Catalog(cfg))
        {
            var known = gauges.Where(g => g.Provider == key).ToList();
            if (known.Count == 0)
            {
                sources.AddCheck(title, enabled, () => SetProvider(key, !enabled));
                continue;
            }
            var branch = sources.AddSubmenu(title);
            branch.AddCheck(T("sources.show"), enabled, () => SetProvider(key, !enabled));
            branch.AddSeparator();
            var account = known[0].Account;
            if (account != "")
                branch.AddHeader(Plan(key) ?? account);
            var hiddenKeys = HiddenKeys(key, account);
            foreach (var gauge in known)
            {
                var shown = !hiddenKeys.Contains(gauge.Key);
                branch.AddCheck(i18n.GaugeLabel(gauge), shown, () => SetGauge(key, gauge.Key, !shown, account));
            }
        }

        root.AddSeparator();
        var showReset = cfg.Flag("show_reset", true);
        root.AddCheck(T("menu.show_reset"), showReset, () => SetOption("show_reset", !showReset), Icons.ShowReset);
        var showPace = cfg.Flag("show_pace", true);
        root.AddCheck(T("menu.pace"), showPace, () => SetOption("show_pace", !showPace), Icons.Pace);
        var locked = cfg.Flag("locked", false);
        root.AddCheck(T("menu.lock"), locked, () => SetOption("locked", !locked), Icons.Lock);
        var toastsOn = cfg.Obj("toasts").Flag("enabled", true);
        root.AddCheck(T("menu.toasts"), toastsOn, () => SetToasts(!toastsOn), Icons.Toasts);

        root.AddSeparator();
        root.AddCommand(T("menu.analytics"), () => Shell.Open(UsageUrl), icon: Icons.Analytics);
        root.AddCommand(T("menu.report"), ShowReport, icon: Icons.Report);

        root.AddSeparator();
        root.AddCommand(hidden ? T("menu.show") : T("menu.hide"), ToggleHidden, icon: hidden ? Icons.Show : Icons.Hide);
        var autostart = Startup.IsEnabled();
        root.AddCheck(T("menu.autostart"), autostart, () => SetStartup(!autostart), Icons.Autostart);
        if (Startup.Packaged)
            root.AddCommand(T("menu.check_updates"), CheckUpdates, icon: Icons.CheckUpdates);

        var feedback = root.AddSubmenu(T("menu.feedback"), Icons.Feedback);
        // No account needed in the first group; a Microsoft or a GitHub one in the second; plain links in the third.
        feedback.AddCommand(T("feedback.bug"), () => Shell.Open(FeedbackUrl("bug")), icon: Icons.Bug);
        feedback.AddCommand(T("feedback.idea"), () => Shell.Open(FeedbackUrl("idea")), icon: Icons.Idea);
        feedback.AddCommand(T("feedback.email"), WriteEmail, icon: Icons.Mail);
        feedback.AddSeparator();
        if (Startup.PackageFamily() is { } family)
            feedback.AddCommand(T("feedback.rate"), () => Shell.Open($"ms-windows-store://review/?PFN={family}"), icon: Icons.Rate);
        feedback.AddCommand(T("feedback.github"), () => Shell.Open($"{RepoUrl}/issues"), icon: Icons.Link);
        feedback.AddSeparator();
        feedback.AddCommand(T("feedback.store"), () => Shell.Open(StoreUrl), icon: Icons.Store);
        feedback.AddCommand(T("feedback.source"), () => Shell.Open(RepoUrl), icon: Icons.Source);

        var errors = Errors;
        if (errors.Count > 0)
        {
            root.AddSeparator();
            foreach (var error in errors)
                root.AddCommand(error, null, enabled: false);
        }

        root.AddSeparator();
        root.AddCommand(T("menu.exit"), Quit, icon: Icons.Exit, hint: $"v{AppInfo.Version}");
        return root;
    }

    /// <summary>What every report starts with: the version and where the widget runs.</summary>
    string EnvironmentLine() =>
        $"Windows {Environment.OSVersion.Version}, {(Startup.Packaged ? "Store app" : "build from source")}, "
        + $"skin {cfg.Str("layout", "rows")}, language {i18n.Language}";

    /// <summary>The feedback form on the site, which needs no account, with the version and the
    /// environment already filled in. They travel in the fragment, which the browser keeps to
    /// itself: nothing reaches anyone until the user presses Send, and the widget sends nothing.</summary>
    string FeedbackUrl(string type) =>
        $"{FeedbackPage}#type={type}&version={Uri.EscapeDataString(AppInfo.Version)}"
        + $"&environment={Uri.EscapeDataString(EnvironmentLine())}";

    /// <summary>The address goes to the clipboard first: a machine without a mail program opens
    /// nothing for mailto, and the toast still tells the user where to write. When another
    /// program holds the clipboard, the toast shows the bare address instead.</summary>
    void WriteEmail()
    {
        var copied = false;
        try
        {
            System.Windows.Clipboard.SetText(SupportEmail);
            copied = true;
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.WriteLine(e);
        }
        Notice(copied ? i18n.T("feedback.email_copied", ("address", SupportEmail)) : SupportEmail, ok: true);
        try
        {
            // A mailto body breaks its lines with CRLF (RFC 6068).
            var subject = $"AI Usage Limits {AppInfo.Version}";
            var body = $"\r\n\r\n--\r\nVersion {AppInfo.Version}\r\n{EnvironmentLine()}";
            Shell.Open($"mailto:{SupportEmail}?subject={Uri.EscapeDataString(subject)}&body={Uri.EscapeDataString(body)}");
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.WriteLine(e);
        }
    }

    /// <summary>What the provider says about the plan and account, e.g. "max · name@company.com".</summary>
    string? Plan(string? provider = null) =>
        gauges.Where(g => provider is null || g.Provider == provider)
              .Select(g => g.Hint).FirstOrDefault(hint => hint.StartsWith("plan ", StringComparison.Ordinal))?[5..];

    string IntervalLabel(int seconds) =>
        seconds >= 3600 ? i18n.T("interval.hour") : i18n.T("interval.minutes", ("n", seconds / 60));

    string ShuffleLabel(int minutes) => minutes switch
    {
        0 => i18n.T("shuffle.off"),
        >= 1440 => i18n.T("interval.day"),
        >= 60 => i18n.T("interval.hour"),
        _ => i18n.T("interval.minutes", ("n", minutes)),
    };

    // --- settings --------------------------------------------------------
    void RebuildLayout()
    {
        layout = MakeLayout();
        Redraw();
    }

    void SetLayout(string name)
    {
        cfg["layout"] = name;
        Config.Save(cfg);
        RebuildLayout();
    }

    void SetPalette(string code)
    {
        cfg["palette"] = code;
        Config.Save(cfg);
        theme = MakeTheme();
        systemSnapshot = SystemSnapshot();
        RebuildLayout();
        UpdateTray();
    }

    void SetLanguage(string code)
    {
        cfg["language"] = code;
        Config.Save(cfg);
        i18n = new I18n(code);
        localErrors.Clear();
        RebuildLayout();
        UpdateTray();
    }

    void SetOption(string key, JsonNode? value)
    {
        cfg[key] = value;
        Config.Save(cfg);
        if (key is "show_reset" or "show_pace" or "color_mode" or "icons" or "lines")
            RebuildLayout();
        else if (key == "topmost")
            window.Topmost = Json.Truthy(value);
    }

    /// <summary>An active pause picks up the new value by itself.</summary>
    void SetInterval(int seconds)
    {
        cfg["refresh_seconds"] = seconds;
        Config.Save(cfg);
        refresher.Interval = Interval;
    }

    /// <summary>A disabled source disappears right away, an enabled one appears after the next request.</summary>
    void SetProvider(string key, bool enabled)
    {
        cfg.Child("providers").Child(key)["enabled"] = enabled;
        Config.Save(cfg);
        var dropped = ProviderRegistry.Catalog(cfg).FirstOrDefault(p => p.Key == key).Title;
        providers = ProviderRegistry.LoadAll(cfg);
        refresher.Providers = providers;
        if (enabled)
        {
            RefreshNow();
            return;
        }
        gauges = gauges.Where(g => g.Source != dropped).ToList();
        failures = failures.Where(f => f.Provider != key).ToList();
        Redraw();
        UpdateTray();
    }

    /// <summary>Hidden gauges stay in the data, they just are not drawn. The list is per account.</summary>
    void SetGauge(string provider, string key, bool visible, string account)
    {
        var settings = cfg.Child("providers").Child(provider);
        var (store, slot) = account != "" ? (settings.Child("hidden_by_account"), account) : (settings, "hidden");
        var list = (store[slot] as JsonArray ?? []).Select(Json.AsString).OfType<string>().Where(k => k != key).ToList();
        if (!visible)
            list.Add(key);
        store[slot] = new JsonArray(list.Select(k => (JsonNode?)k).ToArray());
        Config.Save(cfg);
        Redraw();
        UpdateTray();
    }

    void SetToasts(bool value)
    {
        cfg.Child("toasts")["enabled"] = value;
        Config.Save(cfg);
    }

    void SetStartup(bool value)
    {
        try
        {
            Startup.Toggle(value);
        }
        catch (Exception e)
        {
            localErrors.Add(i18n.T("err.autostart", ("detail", e.Message)));
        }
    }

    /// <summary>A toast when there is nothing newer; when there is, the Store's dialog says the rest.
    /// A click while a check runs does nothing.</summary>
    async void CheckUpdates()
    {
        if (checkingUpdates)
            return;
        checkingUpdates = true;
        try
        {
            if (!await Updates.InstallAsync(window.Handle))
                Notice(i18n.T("update.latest", ("version", AppInfo.Version)), ok: true);
        }
        catch (Exception e)
        {
            // The Store's errors often come without a message; the code is still worth looking up.
            var detail = string.IsNullOrWhiteSpace(e.Message) ? $"0x{e.HResult:X8}" : e.Message;
            Notice(i18n.T("err.update", ("detail", detail)), ok: false);
        }
        finally
        {
            checkingUpdates = false;
        }
    }

    /// <summary>A toast in the widget's colors: the first level for good news, the last for bad.</summary>
    void Notice(string text, bool ok)
    {
        var levels = theme.Levels;
        toasts.Show(text, levels.Count == 0 ? theme.Dim : ok ? levels[0].Color : levels[^1].Color, theme.Text);
    }

    /// <summary>Reading 90 days of logs takes a moment: off the UI thread, then the browser.</summary>
    static void ShowReport() => System.Threading.Tasks.Task.Run(() =>
    {
        try
        {
            Shell.Open(Report.Build(Startup.SharedDir()));
        }
        catch (Exception e)
        {
            System.Diagnostics.Trace.WriteLine(e);
        }
    });

    // --- shuffle ---------------------------------------------------------
    void SetShuffle(int minutes)
    {
        cfg["shuffle_minutes"] = minutes;
        Config.Save(cfg);
        shuffledAt = Environment.TickCount64;
    }

    /// <summary>Once a minute, check whether it is time to change the look.</summary>
    void ShuffleTick()
    {
        var minutes = cfg.Int("shuffle_minutes", 0);
        if (minutes > 0 && Environment.TickCount64 - shuffledAt >= minutes * 60_000L)
            ShuffleLook();
    }

    /// <summary>A random skin and palette, but not the ones in use now.</summary>
    void ShuffleLook()
    {
        shuffledAt = Environment.TickCount64;
        var skins = Skins.Names.Where(name => name != cfg.Str("layout", "")).ToList();
        var palettes = Palettes.Available().Select(p => p.Code).Where(code => code != cfg.Str("palette", "")).ToList();
        if (skins.Count > 0)
            cfg["layout"] = skins[Random.Shared.Next(skins.Count)];
        if (palettes.Count > 0)
            cfg["palette"] = palettes[Random.Shared.Next(palettes.Count)];
        Config.Save(cfg);
        theme = MakeTheme();
        RebuildLayout();
        UpdateTray();
    }
}
