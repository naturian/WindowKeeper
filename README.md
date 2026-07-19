# WindowKeeper

Kleines Windows-Tool (.NET 9, WinForms, ohne sichtbares Fenster), das das
Standard-Verhalten von Windows korrigiert, Fenster kaskadierend oben links
zu öffnen (z. B. Geräte-Manager und andere MMC-/System-Tools):

- **Positions-Gedächtnis:** Jedes normale Fenster wird beim Schließen
  verfolgt und beim nächsten Öffnen an derselben Position wiederhergestellt
  (inkl. Größe und Maximiert-Status) — auch Fenster, die sich selbst
  zentrieren (colorcpl) oder abseits der Ecke öffnen (msinfo32).
- **Zentrieren als Fallback:** Beim allerersten Öffnen (noch keine Position
  gemerkt) werden nur Oben-links-Öffner zentriert; alle anderen unbekannten
  Fenster bleiben unangetastet.
- Öffnet ein zweites Fenster mit demselben Schlüssel, wird es nicht auf das
  bereits offene gestapelt.

## Bedienung

- `Win+Umschalt+Z` schaltet die Automatik ein/aus. (`Win+Z` bleibt absichtlich
  frei — dort liegen die Snap-Layouts von Windows.)
- Tray-Symbol mit Menü (Automatik umschalten, gemerkte Positionen löschen,
  Beenden). Das Icon wird mit `tools/create-icon.ps1` generiert und ist als
  `icon.ico` in die Exe eingebettet.

## Funktionsweise

- Ein unsichtbares Fenster empfängt über `RegisterShellHookWindow` die
  Shell-Nachrichten `HSHELL_WINDOWCREATED`/`HSHELL_WINDOWDESTROYED`.
- Beim Erscheinen wird sofort korrigiert: verstecken, positionieren, neu
  zeigen — so läuft auch die Öffnungsanimation an der Zielposition ab statt
  oben links. Ist der Titel noch nicht endgültig (MMC), ordnet ein eindeutiger
  `Prozess|Klasse`-Abgleich zu; bei Mehrdeutigkeit greifen die Nachläufe.
- Zusätzlich wird nach 150 ms und nochmal nach 700 ms geprüft (MMC setzt
  seine Position verzögert). Kriterium „oben links": Abstand zur linken oberen
  Ecke des Arbeitsbereichs ≤ 350 px (Konstanten in `Program.cs`).
- Positionen werden per `GetWindowPlacement` verfolgt (alle 4 s) und beim
  Schließen unter `%APPDATA%\WindowKeeper\positionen.json` gespeichert.
  Schlüssel: `Prozessname|Fensterklasse|Titel`.

## Bauen & Einrichten

```powershell
dotnet publish -c Release -o publish
powershell -ExecutionPolicy Bypass -File .\Setup-Aufgabe.ps1   # als Administrator
```

`Setup-Aufgabe.ps1` registriert die geplante Aufgabe **WindowKeeper**
(Start bei Anmeldung, höchste Privilegien). Die erhöhten Rechte sind nötig,
weil z. B. der Geräte-Manager automatisch erhöht läuft und Windows (UIPI)
normalen Prozessen das Verschieben solcher Fenster verbietet.
