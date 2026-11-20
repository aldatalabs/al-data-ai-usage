# Microsoft Store Guide

How the widget becomes a Store app: `src/AiUsage/` published self-contained, packed as MSIX,
signed and updated by Microsoft. A build from source runs the same code unpackaged; the code
knows which of the two it is through `Startup.Packaged`.

## 0. One-time setup

The .NET 10 SDK (a per-user install needs `DOTNET_ROOT`, see `AGENTS.md`). `makeappx` and
`signtool` come from an installed Windows SDK, or the build script downloads the
`Microsoft.Windows.SDK.BuildTools` NuGet package into `build/sdk/` on first use (about 90 MB,
gitignored).

## 1. Build

```bash
dotnet run tools/build_msix.cs                     # dist/al-data-ai-usage-X.Y.Z-x64.msix, unsigned
dotnet run tools/build_msix.cs -- --skip-publish   # repack after a manifest or icon change
```

`tools/build_msix.cs` is a .NET file-based app: `dotnet run` compiles it on the fly, nothing to
install. What happens:

1. `dotnet publish` of `src/AiUsage` in Release, self-contained for `win-x64`, into
   `build/publish/`. The .NET desktop runtime has no framework package in the Store, so it
   ships inside: about 180 MB on disk, 75 MB packed. `VersionSuffix` is cleared, so the menu
   says `vX.Y.Z`, not `-dev`.
2. `build/stage/`: that folder plus `tools/msix/Assets/` and `AppxManifest.xml` filled from
   `tools/msix/package.json` and `VersionPrefix` (`X.Y.Z` becomes `X.Y.Z.0`; the fourth part is
   the Store's).
3. `makeappx pack`.

The tiles in `tools/msix/Assets/` and the exe icon `src/AiUsage/app.ico` are the tray icon's
ring, rendered once; they are plain files. The version is `VersionPrefix` in
`src/AiUsage/AiUsage.csproj`. The Store rejects a package whose version is not above the
published one.

## 2. Test locally (sideload)

The Store signs what it publishes, so the shipped package is never signed here. To install the
package on your own machine it needs a signature the machine trusts. Once, as administrator:

```powershell
$cert = New-SelfSignedCertificate -Type Custom -Subject "CN=29223181-2CEA-413A-BEB8-F63E402D61B9" `
    -KeyUsage DigitalSignature -FriendlyName "al-data-ai-usage dev" `
    -CertStoreLocation Cert:\CurrentUser\My `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")
$pwd = ConvertTo-SecureString -String "dev" -Force -AsPlainText
Export-PfxCertificate -Cert $cert -FilePath build\dev.pfx -Password $pwd
Import-PfxCertificate -FilePath build\dev.pfx -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password $pwd
```

The `Subject` must equal `publisher` in `tools/msix/package.json`; a certificate made for an
earlier publisher no longer signs the package, so make a new one. Then:

```powershell
dotnet run tools/build_msix.cs -- --sign build/dev.pfx --password dev
Add-AppxPackage dist\al-data-ai-usage-X.Y.Z-x64.msix
```

Stop a running Debug build first: both take the single-instance mutex. Start the package from
Start, or with `explorer shell:AppsFolder\<PackageFamilyName>!Widget`. Check: gauges appear, the
tray icon works, the menu has Check for updates and says `vX.Y.Z` beside Exit, the usage report
opens in the browser, "Start with Windows" reflects Settings > Apps > Startup and clicking it
opens that page. Check for updates answers `update: 0x803FB112` here: the Store does not know a
sideloaded package, so the item works only in an install from the Store. The startup task state
is `State` (2 = enabled) under
`HKCU\Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\SystemAppData\<PackageFamilyName>\AlDataAiUsage`.

Remove with `Get-AppxPackage *AIUsage* | Remove-AppxPackage`.

## 3. First publication (by hand, once)

1. Account: https://storedeveloper.microsoft.com. A company account ("AL Data Labs", the FOP);
   Microsoft verifies the business document and a paid domain-registration invoice for
   `aldatalabs.com`. Done: verified 2026-11-19.
2. Partner Center > Apps and games > New product > MSIX or PWA app. Reserve the name. The name
   must not begin with a third-party mark: "AI Usage Monitor for Claude & Codex", not "Claude
   Usage Monitor". Done: the product is Store ID `9N5VRJ8TLQ46`, with four names reserved
   (Manage app names): AI Usage Monitor for Claude & Codex, AI Usage Limits, AI Usage Pro,
   AI Usage Limits Pro. The package ships the first; when a third source joins, an update
   switches `display_name` to AI Usage Limits, and the identity stays. A reservation lapses
   unless the product is submitted within three months (by 2027-02-19).
3. Product identity page: copy **Package/Identity/Name**, **Package/Identity/Publisher** and
   **Package/Properties/PublisherDisplayName** into `tools/msix/package.json`. Rebuild. Done.
4. Submission: upload the `.msix`, fill properties, age ratings, listing (screenshots, the
   description from `README.md`, "not affiliated with Anthropic or OpenAI"), pricing Free.
5. Certification takes from hours to a few days. Fix what it asks and resubmit.

The Store CLI cannot do this step; it only updates a published app.

### Listing text (ready to paste)

- **Name:** AI Usage Monitor for Claude & Codex
- **Short description:** A tiny always-on-top gauge of your Claude and Codex usage limits.
- **Description:**

  > A small desktop widget that shows how much of your Claude Code and Codex usage limits is
  > left: the five-hour and weekly windows, time to reset, and the pace you are burning them
  > at. It sits above other windows, lives in the tray, and warns with a toast when a limit
  > gets close.
  >
  > Everything runs on your machine. The widget reads the local credentials that Claude Code
  > and Codex already keep in your profile and asks their usage endpoints directly; nothing is
  > sent anywhere else. Ten languages, several skins and palettes, a usage report built from
  > your local session logs.
  >
  > Open source (MIT): https://github.com/aldatalabs/al-data-ai-usage
  >
  > Not affiliated with, endorsed by or sponsored by Anthropic or OpenAI. Claude and Codex are
  > trademarks of their respective owners.

- **Category:** Developer tools. **Price:** Free. **Age rating:** answer "no" to everything.
- **Privacy policy URL:** required by the Store even without data collection.
  `https://github.com/aldatalabs/al-data-ai-usage/blob/main/PRIVACY.md`.
- **Screenshots:** at least one, 1366x768 or larger: the widget over a desktop, the menu open,
  the report page.
- **Capabilities disclosure:** the full-trust capability asks for a sentence: "Reads the
  credential files of Claude Code and Codex in the user profile to query usage limits."

## 4. Updates

The Store installs updates in the background; Check for updates in the widget's menu asks it
on the spot, and the Store's dialog installs the update and restarts the widget. To publish one:

```bash
dotnet run tools/build_msix.cs        # after raising VersionPrefix in the csproj
msstore publish --inputFile dist/al-data-ai-usage-X.Y.Z-x64.msix --appId 9N5VRJ8TLQ46
```

`msstore` needs an Entra ID tenant linked to Partner Center and `msstore reconfigure` with
tenant, seller and client ids plus a client secret. `.github/workflows/msix.yml` does this on
every `v*` tag: it sets up .NET 10, builds the package, uploads it as a workflow artifact and, when the
repository has the secrets `PARTNER_CENTER_TENANT_ID`, `PARTNER_CENTER_SELLER_ID`,
`PARTNER_CENTER_CLIENT_ID`, `PARTNER_CENTER_CLIENT_SECRET` and `PARTNER_CENTER_APP_ID`,
submits it. Without the secrets the publish step is skipped and the build still runs.

## Runtime differences in a package

| What | Build from source | MSIX |
|---|---|---|
| Code location | `src/AiUsage/bin/` | `C:\Program Files\WindowsApps\<pfn>\` (read-only) |
| Config, cache | `%LOCALAPPDATA%\al-data-ai-usage\` | the same path when the folder already exists (a build from source ran here before); otherwise Windows may redirect it to the package's `LocalCache` |
| Usage report | `%LOCALAPPDATA%\al-data-ai-usage\analytics.html` | `%LOCALAPPDATA%\Packages\<pfn>\LocalCache\analytics.html`, written there directly: the browser runs outside the package and cannot see redirected files |
| Autostart | shortcut in `shell:startup` | `windows.startupTask` in the manifest, state read from the registry, switch in Settings |
| Updates | `git pull` and a rebuild | the Store, in the background or from Check for updates in the menu |
| Reads `~/.claude`, `~/.codex` | yes | yes, full trust |
