# FensterMerker

Kleines Windows-Tool (.NET 9, WinForms, ohne sichtbares Fenster), das das
Standard-Verhalten von Windows korrigiert, Fenster kaskadierend oben links
zu öffnen (z. B. Geräte-Manager und andere MMC-/System-Tools):

- **Positions-Gedächtnis:** Fenster, die oben links aufgehen, werden an der
  Position wiederhergestellt, an der sie zuletzt geschlossen wurden
  (inkl. Größe und Maximiert-Status).
- **Zentrieren als Fallback:** Nur beim allerersten Öffnen (wenn noch keine
  Position gemerkt ist) wird das Fenster auf dem aktuellen Monitor zentriert.
- Fenster, die nicht oben links aufgehen (Programme, die ihre Position selbst
  verwalten), werden **nicht angefasst**.

## Hotkeys

| Hotkey | Funktion |
|---|---|
| `Win+Z` | Aktives Fenster zentrieren |
| `Win+Umschalt+Z` | Automatik ein-/ausschalten |

Dazu gibt es ein Tray-Symbol mit Menü (Automatik umschalten, gemerkte
Positionen löschen, Beenden).

## Funktionsweise

- Ein unsichtbares Fenster empfängt über `RegisterShellHookWindow` die
  Shell-Nachrichten `HSHELL_WINDOWCREATED`/`HSHELL_WINDOWDESTROYED`.
- Neue Fenster werden nach 150 ms und nochmal nach 700 ms geprüft (MMC setzt
  seine Position verzögert). Kriterium „oben links": Abstand zur linken oberen
  Ecke des Arbeitsbereichs ≤ 350 px (Konstanten in `Program.cs`).
- Positionen werden per `GetWindowPlacement` verfolgt (alle 4 s) und beim
  Schließen unter `%APPDATA%\FensterMerker\positionen.json` gespeichert.
  Schlüssel: `Prozessname|Fensterklasse|Titel`.

## Bauen & Einrichten

```powershell
dotnet publish -c Release -o publish
powershell -ExecutionPolicy Bypass -File .\Setup-Aufgabe.ps1   # als Administrator
```

`Setup-Aufgabe.ps1` registriert die geplante Aufgabe **FensterMerker**
(Start bei Anmeldung, höchste Privilegien). Die erhöhten Rechte sind nötig,
weil z. B. der Geräte-Manager automatisch erhöht läuft und Windows (UIPI)
normalen Prozessen das Verschieben solcher Fenster verbietet.
