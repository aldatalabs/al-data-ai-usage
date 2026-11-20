# al-data-ai-usage

A desktop widget for Windows 11: fill bars for your AI usage limits, always on
top, sitting in the free space of the taskbar. Supports Claude Code and Codex CLI.

Installed from the Microsoft Store (coming soon), which also keeps it up to
date, or built from source with the .NET 10 SDK. No admin rights either way.

> **Unofficial.** This project is not affiliated with, endorsed by, or supported
> by Anthropic or OpenAI. It relies on an undocumented Claude endpoint and on
> local log formats that may change or stop working at any time. Use at your own
> risk.

## Requirements

- Windows 10 (2004 or later) or Windows 11
- Claude Code, already signed in (the widget reads its token); Codex CLI is optional
- To build from source: the [.NET 10 SDK](https://dotnet.microsoft.com/download)

## Building from source

```
dotnet build src/AiUsage -c Release
src\AiUsage\bin\Release\net10.0-windows10.0.19041.0\al-data-ai-usage.exe
```

Autostart is toggled with "Start with Windows" in the right-click menu. A build
from source creates a shortcut in `shell:startup`; the Store app has a startup
task, and the menu item opens Settings > Apps > Startup to switch it.

## Controls

| Action | Result |
|---|---|
| Drag with the left button | move the widget; the position is saved |
| Hover | tooltip: exact %, reset time, plan, last refresh time |
| Double-click | refresh now |
| Right-click | menu: sources, appearance (skin, palette, color, shuffle), interval, language, toggles, analytics, autostart, feedback, exit |
| Click the tray icon | hide/show the widget |

The widget keeps itself on top: the taskbar is topmost too, and a click on it
pushes the widget underneath. So every 0.8 s the widget reasserts its place
(`SetWindowPos` with `NOACTIVATE`, it never takes focus) — in the worst case it
is back on top in under a second. While a menu or tooltip is open the widget is
not raised, so it does not cover them. Disable with the `topmost` key.

Windows 11 hides new tray icons under "^" — to pin it, drag the icon from the
overflow onto the taskbar.

The menu is custom-drawn in the Windows 11 style: rounded corners, side
flyouts, arrow-key navigation. Everything about the look lives under a single
"Appearance" item: skin, palette, color mode and shuffle.

## Refresh interval

10 minutes by default; change it in the menu (1 min – 1 hour) or with the
`refresh_seconds` key. Less than 60 s is not allowed.

The `/usage` limit is counted **per access token**: a few requests are enough
to get a 429 that then holds for hours. Therefore:

- `Retry-After` is honored when the server sends a meaningful one; otherwise
  the backoff is 15 / 30 / 60 min;
- the pause and the last data are stored in `cache.json`, so a restart does not
  cost a request;
- the widget shows the last known numbers instead of an error; the error is
  visible in the tooltip;
- when Claude Code refreshes the token, the limit starts from a clean slate —
  the widget notices and does not sit out the rest of the pause.

## Skins (shape)

The shape of the gauges. Switched in the menu ("Skin"), stored in the config
under the `layout` key.

| Key | Name | Size (3 gauges) | Reset time |
|---|---|---|---|
| `rows` | Rows | 249×49 | on every row |
| `cells` | Cells | 228×46 | under the bar |
| `strip` | Strip | 399×26 | inline |
| `bars` | Bars | 105×48 | tooltip only |
| `rings` | Rings | 132×52 | tooltip only |
| `hearts` | Hearts | 249×70 | on every row |
| `candies` | Lollipops | 249×70 | on every row |
| `smokes` | Cigarettes | 249×70 | on every row |
| `coffee` | Coffee | 249×70 | on every row |
| `cocktails` | Cocktails | 249×70 | on every row |
| `flowers` | Sunflowers | 249×70 | on every row |
| `rainbow` | Rainbow | 249×70 | on every row |
| `unicorns` | Unicorns | 249×70 | on every row |

The icons are rasterized the way Pillow's ImageDraw does it
(`src/AiUsage/Ui/Layouts/Raster.cs` is a port): the one on the boundary is
filled **partially** — exactly by the fraction spent, without jumping a whole
shape at a time. Each is drawn twice (gray and colored) and stitched along a
vertical line, at triple scale for anti-aliasing.

The last eight have **identical dimensions** under the same settings, so
switching between them does not move the widget. `rows` matches them in width.
It is the same gauge, only made of icons: colored ones are what has been spent,
gray ones are what is left. The color and the pace marker work the same way as
on a bar. The exception is `rainbow`: its arcs take their color from their own
gradient (green → red, stretched over any number of icons), so the last arcs
are red by themselves; the limit signal stays on the % and the pace tick.

## Status dot

After the percentage there is a dot colored **by the projection**: green — it
will last until reset, yellow — just barely, red — it will run out earlier.
Hollow while there is no projection yet: the window has just started and the
pace is noise.

It does not depend on the color mode, so even with `color_mode: usage` you can
see how things will end.

## Bar color

Two modes, switched in the menu ("Bar color") or with the `color_mode` key.

| Mode | Colored by | Thresholds |
|---|---|---|
| `pace` (default) | projection at reset time | <90 % green, 90–100 yellow, 100–115 orange, >115 red |
| `usage` | how much is already spent | <50 % green, 50–75 yellow, 75–90 orange, ≥90 red |

`pace` answers the question "will it last": green — it will, yellow — just
barely, red — it runs out before the reset. If there is no projection yet (the
window has just started), the color falls back to `usage`.

## Pace marker

A thin tick on the bar — where the fill would be if the limit were spent
evenly over the whole window (5 hours or 7 days).

- fill **left** of the tick — it will last until reset;
- fill **right** of the tick — you are spending faster than the limit resets.

The tooltip says the same in numbers: `~155% by reset ⚠ runs out ~19:19` — how
much it will add up to by reset time and when it hits the ceiling.

The pace is measured from the start of the window (`resets_at` minus its
length), because the widget polls the source rarely and keeps no history of
samples. Disable in the menu ("Pace marker") or with the `show_pace` key.

## Palettes (colors)

`palettes/<code>.json`, one file per palette: `dark` (default), `light`,
`candy`, `neon`, `matrix`, `mono`, `rainbow`, `unicorn`. Switched in the menu
("Palette").

There is also `system` — not a file but the current Windows colors: light or
dark theme is read from the same registry branch as the taskbar color
(`Themes\Personalize\SystemUsesLightTheme`), and the accent color from
`DWM\AccentColor` goes to the pace tick. The widget picks up a Windows theme
switch by itself, within half a minute.

A palette defines `bg`, `track`, `text`, `dim`, `levels`, `pace_levels` and is
applied on top of `theme` from the config. A new palette = a new file.

## Lines

Menu "Lines": `Auto`, 1, 2 or 3. How many lines the gauges take — the widget
computes the rest.

| Lines | rows (3 gauges) |
|---|---|
| 3 | 249×49 — a column |
| 2 | 498×36 |
| 1 | 747×23 — a single row |

`Auto` takes the skin's natural layout: `rows` and the icon skins — a column;
`cells`, `strip`, `bars`, `rings` — a single row. The config key is `lines`.

## Icon count

Menu "Icons": 5, 8 or 10 (default 5). Fewer icons — a narrower widget and a
coarser step.

| Icons | Width (3 gauges) |
|---|---|
| 5 | 201 px |
| 8 | 249 px |
| 10 | 281 px |

The fill step does not depend on the count: the icon on the boundary is filled
partially, so the gauge moves smoothly with any number.

The width of `rows` is computed from the same number, so the bar and the icons
line up.

## Shuffle

Menu "Shuffle": off, 10 min, 1 hour, 1 day. Changes the skin and the palette
together, never keeping the current ones. The config key is `shuffle_minutes`
(0 = off).

## Config

Defaults are in `config.default.json` in the repo; on first run they are
copied to `%LOCALAPPDATA%\al-data-ai-usage\config.json`. Edit it by hand or
through the menu.

Useful keys: `language` (`system` by default: the Windows display language, or a code such as
`uk`), `scale` (1.25 for large screens),
`refresh_seconds` (minimum 60), `theme.levels` (color thresholds),
`toasts.thresholds`.

The language can also be switched from the menu. Translations are in
`locales/<code>.json`, one file per language: en, de, es, fr, it, nl, pl,
pt-BR, tr, uk. A new language = a new file, no code changes; a missing key
falls back to English.

## Data source

`src/AiUsage/Providers/ClaudeUsage.cs` reads the Claude Code OAuth token from
`%USERPROFILE%\.claude\.credentials.json` **read-only** and calls
`GET https://api.anthropic.com/api/oauth/usage`.

The widget does not store the token, does not copy it and does not update the
file — Claude Code does that itself. No session key or cookies are used.

Which gauges you get depends on the plan:

| Gauge | When it appears | Window |
|---|---|---|
| `5h` / `7d` | present in the `/usage` response | 5 hours / 7 days |
| model (`Sonnet`, `Fable`…) | present in `limits[]` | 7 days |
| `Included` | Enterprise: the plan's money budget | the 1st of the month — the end date |
| `Extra` | a spend limit beyond the plan is enabled | calendar month (UTC) |

`Included` and `Extra` also show money in the tooltip (`$57 / $2000`) — the
gauge's `Detail` field. The start of the `Included` window can be set by hand
with the `providers.claude_usage.included_start` key (`YYYY-MM-DD`) if it is
not the 1st.

Failure behavior:
- **429** — not an expired token but frequent polling. Pause of 15 / 30 / 60
  min, or `Retry-After` if it is longer.
- **401/403** — the token on disk has expired. Fixed by running `claude` in a
  terminal.
- Network down — the last known values stay; the error is visible in the menu
  and the tooltip.

## Codex

`src/AiUsage/Providers/CodexUsage.cs` reads Codex CLI limits **without the
network**: Codex itself writes a snapshot to the session log
`%USERPROFILE%\.codex\sessions\YYYY\MM\DD\rollout-*.jsonl`:

```json
"rate_limits": {
  "primary":   {"used_percent": 5.0,  "window_minutes": 300,   "resets_at": 1790451451},
  "secondary": {"used_percent": 16.0, "window_minutes": 10080, "resets_at": 1790934450}
}
```

The last snapshot from the newest file is used, so the numbers are what Codex
saw at the time of its last reply. If the window has already rolled over since
then (`resets_at` in the past), 0 % is shown.

Disable with the check mark in the "Sources" menu or in the config:
`providers.codex_usage.enabled = false`.

## Usage analytics

Two menu items:

- **Show spending analytics** — opens the usage page on claude.ai.
- **Build usage analytics** — builds a report from **local** logs
  (`%USERPROFILE%\.claude\projects\*\*.jsonl`, read-only) and opens it in the
  browser.

The report is a single self-contained HTML file, `analytics.html`: the data is
embedded in the page, with period filters, active and inactive sessions with
titles and IDs, tokens by kind (input / output / cache write / cache read) and
recommendations. A build from source writes it to
`%LOCALAPPDATA%\al-data-ai-usage\`, the Store app to its package's
`LocalCache` folder. It is built in the background because the logs can be
large; it covers the last 90 days (`Days` in `src/AiUsage/Core/Report.cs`).

## Sources

The "Sources" menu lists the built-in providers, Claude and Codex, and lets you
turn either off with a check mark. A disabled source disappears from the gauges
right away; an enabled one appears after the next request.

The gauge label shows whose it is: `Claude 5h`, `Codex 7d`. The name comes
from the provider's `Title`.

A source may serve more than one account — Claude Code can be signed in to a
personal or a company account, with a different set of limits each. The widget
notices a switch (`/login` in Claude Code) within a minute and fetches the new
account's numbers instead of waiting for the next refresh; numbers cached from
another account are not shown after a restart. The per-gauge check marks are
kept per account, so hiding `Included` on one account does not hide it on the
other. The flyout header and the tooltip say which account is on screen.

## Writing a provider

A class in `src/AiUsage/Providers/` that implements `IProvider`:

```csharp
public sealed class MySource : IProvider
{
    public string Name => "my_source";    // the key under "providers" in the config
    public string Title => "My source";   // caption in gauge labels and in the "Sources" menu

    public Task<List<Gauge>> FetchAsync(JsonObject settings, CancellationToken ct) => ...;
}
```

Throw `ProviderError` for anything the user should read about. Then add an
instance to `ProviderRegistry` in `Providers.cs` and a line to `providers` in
`config.default.json`. The core knows nothing about specific sources.

## Versioning

The running version is shown in small text beside **Exit** in the right-click
menu. Releases are git tags `vX.Y.Z`; what changed in each is in
[CHANGELOG.md](CHANGELOG.md), and the release steps are in
[development_guides/dev_guide.md](development_guides/dev_guide.md).

The Store app updates itself in the background; "Check for updates" in the
menu asks the Store right away. A build from source updates with a `git pull`
and a rebuild. How the Store package is built and published is in
[development_guides/store_guide.md](development_guides/store_guide.md).

## Security

See [SECURITY.md](SECURITY.md) for what the widget reads and how to report a
vulnerability, and [PRIVACY.md](PRIVACY.md) for the short version: nothing leaves your machine.

## License

[MIT](LICENSE)
