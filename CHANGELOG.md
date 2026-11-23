# Changelog

All notable changes to this project are documented in this file.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), versions follow
[SemVer](https://semver.org/). Release process: `development_guides/dev_guide.md`.

## [Unreleased]

### Changed

- **Feedback without an account**: Report a bug and Suggest an idea open the feedback form on
  aldatalabs.com instead of the issue form on GitHub; the version and the environment travel in
  the link and are shown before sending. New Send an email copies support@aldatalabs.com to
  the clipboard and opens the mail program. GitHub is one entry now, Issues on GitHub. The
  widget itself still sends nothing; PRIVACY.md describes the three ways.
- **README**: the app is in the Microsoft Store now; added the Store link and the winget
  commands, by name and by Store ID.

## [1.0.1] - 2026-11-20

### Added

- **The widget**: gauges for Claude Code and Codex CLI usage limits, always on top in the free
  space of the taskbar. A tooltip gives the exact percentage, the reset time, the plan and the
  last refresh; a double-click refreshes; the tray icon hides and shows the widget.
- **Sources**: Claude and Codex are built in, each switched on or off in the menu, and so is
  every gauge of a source. The Claude gauges follow the account Claude Code is signed in to.
- **Appearance**: thirteen skins, from plain rows, cells, strip, bars and rings to hearts,
  lollipops, coffee and unicorns; palettes; one to three lines; bar color by usage or by pace;
  a transparent background that shows the taskbar's own material; shuffle, which changes the
  skin and the palette on a timer.
- **Pace**: a marker for where the usage should be by now, a projection of where it will be at
  the reset, and threshold alerts as toasts.
- **Usage analytics**: a local HTML report built from the Claude Code and Codex logs on this
  machine, and a shortcut to the usage page on claude.ai.
- **Ten languages**, and System language, which follows the Windows display language and is the
  default for a new install.
- **Microsoft Store package**: "AI Usage Monitor for Claude & Codex", published by AL Data Labs.
  The Store installs updates in the background; Check for updates in the menu asks it on the
  spot. Start with Windows is a startup task in the package and a shortcut in a build from
  source.
- **Feedback** in the menu: Report a bug and Suggest an idea open the issue form on GitHub with
  the version and the environment already filled in, Rate in the Store (the Store app only) opens
  the review page, Microsoft Store page opens the product page, Source code on GitHub opens the
  repository. The widget itself sends nothing: the browser does, once you submit the form.
- **Version in the menu**: the Exit row shows the running version; every bug report starts with
  it.
