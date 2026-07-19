# WindowKeeper

A small Windows utility (.NET 9, WinForms, no visible window) that
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

Grab the latest zip from the
[releases page](https://github.com/naturian/WindowKeeper/releases):

- `…win-x64.zip` — small, requires the
  [.NET 9 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/9.0)
- `…win-x64-selfcontained.zip` — larger, runs without any prerequisites

Extract anywhere and run `WindowKeeper.exe --install` once (single
elevation prompt). `--uninstall` removes it again. Releases are built
automatically by GitHub Actions on every version tag.

## Usage

- `Win+Shift+Z` toggles automatic positioning. (`Win+Z` intentionally stays
  free — Windows snap layouts live there.)
- Tray icon with a menu (toggle automatic positioning, forget saved
  positions, exit). The icon is generated with `tools/create-icon.ps1` and
  embedded into the executable as `icon.ico`.

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
  the work area ≤ 350 px (constants in `Program.cs`).
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
  each other.

## Building & setup

```powershell
dotnet publish -c Release -o publish
.\publish\WindowKeeper.exe --install
```

`--install` prompts for elevation once and registers the **WindowKeeper**
scheduled task (run at logon, highest privileges) pointing at the
executable's own location — the folder can live anywhere. Elevated rights
are required because e.g. Device Manager auto-elevates, and Windows (UIPI)
forbids normal processes from moving windows of elevated ones. Success is
silent; the appearing tray icon is the feedback.

`--uninstall` stops WindowKeeper and removes the scheduled task.

Requires the .NET 9 Desktop Runtime (the published build is
framework-dependent).
