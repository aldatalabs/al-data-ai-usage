# AGENTS.md

This file provides guidance to coding agents working in this repository — Claude Code, OpenAI
Codex, GitHub Copilot, or any other. It is the **single source of truth**; the tool-specific
files (`CLAUDE.md`) only point here, so keep all instructions in this file.

## Setup

The .NET 10 SDK, Windows 10/11. Nothing else: there are no NuGet packages.

A per-user SDK install (`%LOCALAPPDATA%\Microsoft\dotnet`) needs `DOTNET_ROOT` set to that
folder, or the exe asks to install the Desktop Runtime. In Git Bash:

```bash
export PATH="$LOCALAPPDATA/Microsoft/dotnet:$PATH" DOTNET_ROOT="$LOCALAPPDATA/Microsoft/dotnet"
```

## Build

```bash
dotnet build src/AiUsage                                   # Debug
src/AiUsage/bin/Debug/net10.0-windows10.0.19041.0/al-data-ai-usage.exe
```

The build stays at 0 warnings; nullable is on, and there is no linter beyond the compiler.
Broad `catch (Exception)` is deliberate throughout: a bad provider or a missing Windows API must
not take the widget down. Do not "fix" it.

## Tests

There is no test suite. The widget is WPF and P/Invoke against live Windows; what counts as a
check is running it. After a change to anything the user sees, restart the widget and look
(the `restart-widget` skill has the commands). The widget does not reload code.

- **One at a time**: a single-instance mutex per session. Stop the installed package before
  starting a Debug build, and start the package again afterwards: it is the one that runs all day.
- **Debug builds** write the exceptions the app swallows to `%TEMP%\al-data-ai-usage.log`.
- **Show, with a screenshot.** Drive the widget with `PostMessage` to its own windows; never
  move the real cursor or send global input.

## Running

Config lives in `%LOCALAPPDATA%\al-data-ai-usage\config.json`, copied from
`config.default.json` on first run. The cache of the last successful fetch is beside it.
Deleting the folder resets the user; nothing else on the machine is written to.

## Architecture

`App.xaml.cs` takes the single-instance mutex and creates `Ui/Widget`. Sources are `IProvider`s;
everything the user can pick lives in JSON at the repo root (`locales/`, `palettes/`,
`config.default.json`), copied beside the exe by the build.

| Folder | Purpose |
|---|---|
| `src/AiUsage/Core/` | `Gauge`, `Model`, `Json`, `Config`, `Cache`, `Theme`, `I18n`, `Palettes`, `Refresher`, `Report`: everything without UI. `Report.html` is the usage report's page, embedded as a resource. |
| `src/AiUsage/Providers/` | `IProvider`, `ClaudeUsage`, `CodexUsage`; `ProviderRegistry` lists the built-in ones. |
| `src/AiUsage/Ui/` | `WidgetWindow` (the borderless window), `Widget` (state, refresh, menu), `Menu`, `Tray`, `Toast`, `Tooltip`, `Startup`, `Updates`, `Native`, `TkText`, `TkDraw`, `Icons`. |
| `src/AiUsage/Ui/Layouts/` | The skins. `Skins.Make` maps a config name to one; unknown names fall back to Rows. |
| `locales/`, `palettes/` | Translations and palettes, one JSON file each. |
| `tools/build_msix.cs`, `tools/msix/` | The Store build: a .NET file-based script, the `AppxManifest.xml` template, package identity, tile icons. Guide: `development_guides/store_guide.md`. |
| `tools/bump_version.cs` | The release bump: `VersionPrefix` in the csproj and the changelog header. |

### Drawing

The widget began as a Python and Tk prototype, kept outside this repository, and still draws
exactly what that prototype drew.

- **Pixel parity**: `Ui/TkText.cs` places text the way Tk does: widths and line boxes come
  from GDI, and the run is laid out by `TextFormatter` with every OpenType feature off, since
  Tk neither kerns nor ligates. Ovals, arcs and lines go through `Ui/TkDraw.cs`, which makes the
  GDI calls Tk 9 makes, into a bitmap: WPF geometry, aliased or not, lands on other pixels. Icon
  skins rasterize with `Layouts/Raster.cs`, a port of Pillow's ImageDraw. After a drawing change,
  compare with a screenshot from before it.
- **Context menu**: `Ui/Menu.cs` draws it by hand. The root window takes the foreground and the
  mouse capture; flyouts never activate. The check mark is a GDI stamp in MS Gothic, the face Tk
  fell back to for it. The menu opens after the click handler returns: opened inside it, the
  popup cannot take the mouse.

### Store package

`dotnet run tools/build_msix.cs` publishes self-contained (the desktop runtime has no Store
framework package) and packs `dist/*.msix`. In a package, `Startup.Packaged` turns autostart
into the manifest's startup task, the usage report goes to the package's `LocalCache` (a browser
outside the package cannot see AppData writes redirected into it), and the menu gets Check for
updates, which asks the Store (`Ui/Updates.cs`). Guide: `development_guides/store_guide.md`.

### Conventions

- **Everything in the repo is in English**: code, comments, docs, commit messages.
- **No new dependencies.** The BCL and WPF only. WinForms is referenced for `NotifyIcon` alone,
  and the Windows Runtime projection (the `10.0.19041.0` in the target framework) for the Store's
  update API alone; other Windows APIs go through `LibraryImport` in `Ui/Native.cs`.
- **Two ways to run, one code path.** A build from source and the Store package run the same
  code; whatever differs asks `Startup.Packaged`.
- **A new UI string goes into all ten `locales/*.json`.** A missing key falls back to English, so
  nothing breaks, but a reader of any other language sees English in the middle of their menu.
- **Config keys are append-only.** A new key gets a default in `config.default.json` and a loose
  getter (`Num`, `Int`, `Str`, `Flag` in `Core/Config.cs`) at the use site. Renaming or removing
  a key is a major version.
- **The core knows nothing about specific sources.** Anything Claude- or Codex-specific belongs
  in its provider; `Core/` sees only `Gauge`s.
- **Icons are Segoe Fluent Icons code points** in `Ui/Icons.cs`. On a machine without that font
  the menu draws an empty gutter rather than boxes; keep it that way.

## Release Flow

Same shape as `al-data-utils` and `al-data-time-tracker-pro`. A release is a tag; the tag builds
the MSIX and the Store ships it. Full process in `development_guides/dev_guide.md`. Summary:

1. Branch off `main`: `git checkout -b release/X.Y.Z`; merge finished feature branches into it,
   never into `main`.
2. `dotnet run tools/bump_version.cs -- X.Y.Z`. Never by hand: it writes `VersionPrefix` in
   `src/AiUsage/AiUsage.csproj` and closes `[Unreleased]` in `CHANGELOG.md` as `[X.Y.Z] - date`.
3. Commit `bump version to X.Y.Z`, tag `vX.Y.Z`, push the branch and the tag, merge back to
   `main` with `--no-ff`, push `main`.
4. The tag runs `.github/workflows/msix.yml`: it builds the package and, once the Partner Center
   secrets are set, submits it to the Store. No GitHub Release, no assets.

Rules: changelog entries go under `[Unreleased]` as part of the change itself, not at release
time. The version beside Exit in the menu is how a user tells you which build they are on;
every bug report starts with it.

## Agent Tooling Layout

| What | Where | Shared? |
|---|---|---|
| Repo instructions | `AGENTS.md` | **Yes** — Codex and Copilot read it natively; `CLAUDE.md` imports it with `@AGENTS.md`. |
| Skills | `.claude/skills/` | Claude Code only. `release` walks the release flow; `restart-widget` runs the new code and puts the installed widget back. Anything a skill teaches that other agents also need belongs in this file. |
| Permissions | `.claude/settings*.json` | No. `settings.local.json` is gitignored. |
| Long-term memory | Claude Code's per-project memory dir, outside the repo | No. Durable project facts go in this file or `development_guides/`. |

Rule of thumb: if a fact should survive a change of agent, it goes in `AGENTS.md`, not in
tool-specific storage.
