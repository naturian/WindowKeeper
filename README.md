# WindowKeeper

A small Windows utility (.NET 10 LTS, WinForms, no visible window) that
remembers where you close your windows — and reopens them right there.

## Why does this exist?

Windows does not center new windows, and it does not remember where you
put most of them either. When an application doesn't pick a position
itself, Windows places its window cascading from the **top-left corner**
of the screen — a default unchanged since the earliest Windows versions.
Most of the classic built-in tooling never opts out of it: the management
consoles, system dialogs, control-panel applets and console windows such
as Command Prompt or PowerShell all forget their position on every single
launch.

On a modern display this is genuinely annoying. On an ultrawide monitor
(this tool was born on a 5120×1440 one), "top-left" is half a meter away
from where you are looking, so every launch of one of these tools starts
with dragging the same window across the same screen to the same place —
again.

The usual fixes have trade-offs: PowerToys is a large suite for what is a
single missing feature, and script-based solutions add a runtime
dependency. WindowKeeper is the minimal alternative: one small executable
that watches windows come and go, remembers where you closed them, and
puts them back there the next time — including the open animation, which
plays at the target position instead of the corner.

## What it does

- **Position memory:** Every normal window is tracked on close and restored
  to the same position the next time it opens (including size and maximized
  state) — regardless of whether it opens in the corner, centers itself or
  appears anywhere else on screen.
- **Centering as a fallback:** On the very first open (no position saved
  yet), only top-left openers are centered; all other unknown windows are
  left alone.
- If a second window with the same key opens, it is not stacked onto the
  one that is already open.

## Download & install

Grab the latest release from the
[releases page](https://github.com/naturian/WindowKeeper/releases):

- `WindowKeeper-Setup-….exe` — **recommended**; self-contained, installs to
  Program Files, registers WindowKeeper for logon and provides a normal entry
  under Windows' installed apps.
- `…win-x64.zip` — small, requires the
  [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)
- `…win-x64-selfcontained.zip` — larger, runs without any prerequisites

For the normal installation, run the setup executable and follow the short
wizard. Updates can be installed directly over an existing version; uninstall
through Windows' **Installed apps** page.

For portable deployment, extract either zip and run
`WindowKeeper.exe --install` once. The files are copied to the protected
`%ProgramFiles%\WindowKeeper` directory before the elevated logon task is
registered, so the extracted download can be removed afterwards.
`--uninstall` removes the portable installation again. Releases are built,
tested, checksummed and supplied with build provenance by GitHub Actions.

## Usage

- `Win+Shift+Z` toggles automatic positioning. (`Win+Z` intentionally stays
  free — Windows snap layouts live there.)
- Tray icon with a menu (toggle automatic positioning, forget saved
  positions, exit). The icon is generated with `tools/create-icon.ps1` and
  embedded into the executable as `icon.ico`.

## Configuration

`%APPDATA%\WindowKeeper\settings.json` is created on first run and read at
startup (restart WindowKeeper after editing):

```json
{
  "Enabled": true,
  "TopLeftThreshold": 350,
  "MinLifetimeMs": 10000,
  "MaxAgeDays": 90,
  "Rules": [
    { "Process": "firefox", "Mode": "ignore" },
    { "Process": "notepad", "Mode": "center" },
    { "Process": "Code", "IgnoreTitle": true },
    { "Process": "msedge", "HashTitle": true }
  ]
}
```

- `Enabled` — persists the tray/hotkey toggle across restarts.
- `TopLeftThreshold` — distance (px) from the top-left corner up to which a
  window counts as a "top-left opener" (centering fallback).
- `MinLifetimeMs` — windows that close sooner and were never touched are not
  remembered (filters splash screens).
- `MaxAgeDays` — entries unused for this long are pruned at startup.
- `Rules` — per-process overrides (`Process` is the process name without
  `.exe`, matched case-insensitively):
  - `"Mode": "ignore"` — never touch and never remember this process.
  - `"Mode": "center"` — always center its windows, never remember them.
  - `"IgnoreTitle": true` — build the position key without the window title;
    useful for apps whose titles change with the open document (browsers,
    editors), so all their windows share one remembered position.
  - `"HashTitle": true` — keep separate title-based positions without writing
    document names, folder names or URLs to `positions.json` in plaintext.

Invalid numeric settings are clamped to safe ranges and unknown rule modes
fall back to `normal`. A malformed JSON file is preserved with a
`corrupt-<timestamp>` suffix and replaced with valid defaults.

## How it works

- An invisible window receives the shell messages
  `HSHELL_WINDOWCREATED`/`HSHELL_WINDOWDESTROYED` via
  `RegisterShellHookWindow`.
- Correction happens immediately when a window appears: hide, position,
  re-show — so the open animation also plays at the target position instead
  of the top-left corner. If the title is not final yet (MMC), a unique
  `process|class` match resolves the target; on ambiguity the delayed
  passes take over.
- Additional checks run after 150 ms and again after 700 ms (MMC sets its
  position late). "Top-left" criterion: distance to the top-left corner of
  the work area ≤ the configured threshold.
- Positions are tracked via `GetWindowPlacement` (every 4 s) and saved on
  close to `%APPDATA%\WindowKeeper\positions.json`.
  Key: `process|windowClass|title`.
- Data hygiene: short-lived windows the user never touched (splash screens,
  transient dialogs) are not remembered; open windows are flushed to disk
  periodically so nothing is lost on a hard process kill; entries unused for
  90 days are pruned at startup.
- Positions are stored **per monitor configuration** (resolution + layout):
  on a display change (`DisplaySettingsChanged`) WindowKeeper automatically
  switches to the matching profile, so different setups do not overwrite
  each other. Device name, DPI and working area are part of the profile, so a
  scaling or taskbar-layout change gets its own positions.
- Writes are atomic and retain one `.bak` file. Unexpected failures are logged
  with rotation under `%LOCALAPPDATA%\WindowKeeper\Logs`.

### Privacy

By default, the position key includes the visible window title. Depending on
the application this may contain a document, folder or page name. Use
`IgnoreTitle` when all windows of a process should share a position, or
`HashTitle` when separate positions are desired without storing titles in
plaintext. No data is transmitted anywhere.

## Building & setup

```powershell
dotnet publish -c Release -r win-x64 --self-contained `
  -p:PublishSingleFile=true -o publish-sc
.\tools\build-installer.ps1 -Version 2.1.0 `
  -SourceDir .\publish-sc -OutputDir .\installer-output
```

The installer build helper downloads the pinned Inno Setup 7.0.2 compiler,
verifies its SHA-256 hash and Authenticode publisher, and then creates
`WindowKeeper-Setup-2.1.0.exe`.

`--install` prompts for elevation once, copies the published files to
`%ProgramFiles%\WindowKeeper`, and registers the **WindowKeeper** scheduled
task (run at logon, highest privileges) pointing at that protected copy. Elevated rights
are required because e.g. Device Manager auto-elevates, and Windows (UIPI)
forbids normal processes from moving windows of elevated ones. Success is
silent; the appearing tray icon is the feedback.

`--uninstall` stops WindowKeeper and removes the scheduled task and installed
files. The setup-based installation should instead be removed through
Windows' **Installed apps** page.

Requires the .NET 10 Desktop Runtime (the published build is
framework-dependent).
