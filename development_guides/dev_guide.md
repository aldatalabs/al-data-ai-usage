# Release Guide

Same flow as `al-data-utils` and `al-data-time-tracker-pro`. A release is a git tag `vX.Y.Z` on
`main`. The tag builds the MSIX in GitHub Actions and, once the Partner Center secrets are set,
submits it to the Store, which certifies it and updates users. The version number is what the
widget shows beside Exit in its menu, so a user can say which build they are looking at.

The version lives in one place, `VersionPrefix` in `src/AiUsage/AiUsage.csproj`, and is bumped
**only at release time**. Local builds carry `VersionSuffix` `dev` (`vX.Y.Z-dev` in the menu);
the Store build clears it.

## 0. Preflight

```bash
dotnet build src/AiUsage                                   # 0 warnings
src/AiUsage/bin/Debug/net10.0-windows10.0.19041.0/al-data-ai-usage.exe   # walk through the menu, build the report
dotnet run tools/build_msix.cs -- --sign build/dev.pfx --password dev    # sideload, see store_guide.md
```

- No new dependencies: the BCL and WPF only.
- A change to `config.default.json` must survive an existing user config: new keys get
  defaults, nothing is renamed without a fallback.

## 1. Release branch from main

```bash
git checkout main && git pull
git checkout -b release/X.Y.Z
git push -u origin release/X.Y.Z
git merge --no-ff <feature-branch>      # each finished feature lands here, not on main
```

A release branch stays open as long as it takes to collect what is going out; the bump is
the last thing that happens on it. Until then `CHANGELOG.md` collects entries under
`[Unreleased]`, written as the features merge, not reconstructed at the end.

## 2. Bump version + changelog

```bash
dotnet run tools/bump_version.cs -- X.Y.Z
```

That writes `VersionPrefix` in `src/AiUsage/AiUsage.csproj` and turns `[Unreleased]` in
`CHANGELOG.md` into `[X.Y.Z] - YYYY-MM-DD` with a fresh empty `[Unreleased]` above it. Don't edit
either by hand: the two are the only places the number appears, and the tool keeps them in step.

Read the new section once more; this is the text a user sees on GitHub.

```bash
git add src/AiUsage/AiUsage.csproj CHANGELOG.md
git commit -m "bump version to X.Y.Z"
git push
```

## 3. Tag and merge back

```bash
git tag vX.Y.Z
git push origin vX.Y.Z

git checkout main && git pull
git merge --no-ff release/X.Y.Z
git push
```

The tag push runs `.github/workflows/msix.yml`: it builds `al-data-ai-usage-X.Y.Z-x64.msix`,
keeps it as a workflow artifact and, with the secrets set, submits it (`store_guide.md`,
section 4). The Store rejects a version that is not above the published one. No GitHub Release,
no assets: the changelog section is the notes.

## Version numbers

SemVer, read from the user's side:

- **Patch** (`1.0.x`): a fix. Nothing in the menu or config changes.
- **Minor** (`1.x.0`): a new menu item, skin, provider or config key. Old configs keep working.
- **Major** (`x.0.0`): a config key renamed or removed, a provider dropped, the minimum Windows
  version raised.
