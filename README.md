# WindowKeeper

A small Windows utility (.NET 9, WinForms, no visible window) that fixes
Windows' default behavior of opening windows cascading from the top-left
corner (e.g. Device Manager and other MMC/system tools):

- **Position memory:** Every normal window is tracked on close and restored
  to the same position the next time it opens (including size and maximized
  state) — even windows that center themselves (colorcpl) or open away from
  the corner (msinfo32).
- **Centering as a fallback:** On the very first open (no position saved
  yet), only top-left openers are centered; all other unknown windows are
  left alone.
- If a second window with the same key opens, it is not stacked onto the
  one that is already open.

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
