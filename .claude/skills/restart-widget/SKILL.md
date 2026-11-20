---
name: restart-widget
description: Restart the running al-data-ai-usage tray widget so it picks up code changes, and optionally screenshot the menu. Use after editing src/AiUsage, locales or palettes, or for "перезапусти віджет", "restart the widget", "покажи як виглядає".
---

# Restart the widget

The widget does not reload code, and only one runs per session. The installed package is what
package; whatever you start, the package is running again when you are done.

## Debug build: quick look

```powershell
Get-Process al-data-ai-usage -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
dotnet build src/AiUsage
Start-Process src\AiUsage\bin\Debug\net10.0-windows10.0.19041.0\al-data-ai-usage.exe
Start-Sleep -Seconds 3
(Get-Process al-data-ai-usage).Path
```

A path at the end means it is up. Swallowed exceptions are in `%TEMP%\al-data-ai-usage.log`;
a crash on start shows up in a terminal with `dotnet run --project src/AiUsage`. When done, stop
it and start the package (last command below).

## The package: what runs day to day

```powershell
Get-Process al-data-ai-usage -ErrorAction SilentlyContinue | Stop-Process -Force -Confirm:$false
dotnet run tools/build_msix.cs -- --sign build/dev.pfx --password dev
Get-AppxPackage *AIUsage* | Remove-AppxPackage
Add-AppxPackage (Get-ChildItem dist\*.msix | Sort-Object LastWriteTime | Select-Object -Last 1).FullName
explorer.exe "shell:AppsFolder\$((Get-AppxPackage *AIUsage*).PackageFamilyName)!Widget"
```

The same version cannot be added over itself, hence the remove. Config and cache survive it:
they live in `%LOCALAPPDATA%\al-data-ai-usage\`, outside the package. The dev certificate is in
`development_guides/store_guide.md`, section 2.

## Showing a menu change

The menu opens from the tray icon, so a throwaway script in the scratchpad can open it without
touching the mouse: find the process's `WindowsForms*` window (the tray icon's), post it the tray
callback message (`WM_USER + 1024`) with `WM_RBUTTONDOWN`, then `WM_RBUTTONUP`, as `lParam`, and
grab the bounding box of the process's visible windows with a few pixels of margin. Keys go to
the root menu window as `WM_KEYDOWN`/`WM_KEYUP`, all in one burst: the menu closes when it loses
the foreground. Never move the real cursor or send global input. Put every variant the user is
choosing between on one image.
