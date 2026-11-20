---
name: release
description: Cut a release of the widget - release branch, version bump via tools/bump_version.cs, changelog, tag vX.Y.Z, merge to main. Use for "зроби реліз", "release 1.0.1", "bump version", "tag a release".
---

# Release

Follow [development_guides/dev_guide.md](../../../development_guides/dev_guide.md); this skill
is the checklist. A release is a tag on `main`: the tag builds the MSIX for the Store, and the
number is what the widget shows beside Exit.

## Before anything

1. `git status` is clean and you are on `main` or on an open `release/X.Y.Z` branch.
2. `CHANGELOG.md` has content under `[Unreleased]`. If it is empty, stop and ask what is going
   out: entries are written with the change, not invented at release time.
3. Pick the number by the rule in the guide: patch for a fix, minor for anything the user can see
   or set, major when an old config stops working.

## Steps

```bash
git checkout main && git pull
git checkout -b release/X.Y.Z             # skip if it already exists
git push -u origin release/X.Y.Z
dotnet run tools/bump_version.cs -- X.Y.Z # never edit VersionPrefix or the changelog header by hand
dotnet build src/AiUsage
```

Read the new `[X.Y.Z]` section once; it is the public text. Then:

```bash
git add src/AiUsage/AiUsage.csproj CHANGELOG.md
git commit -m "bump version to X.Y.Z"
git tag vX.Y.Z
git checkout main && git merge --no-ff release/X.Y.Z
```

Pushing the release branch, `main` and the tag changes things outside the machine: show the
user the commands and run them only when told to. The tag push runs the msix workflow, which
submits to the Store once its secrets are set. There is no GitHub Release step.

## After

- Rebuild and reinstall the package (the `restart-widget` skill) and confirm the menu says
  `vX.Y.Z`.
- Report: tag, commit, and the changelog section, in a few lines.
