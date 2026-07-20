using System.Globalization;

namespace WindowKeeper;

// Lightweight two-column string table instead of resx satellite assemblies:
// no resource pipeline, works unchanged with single-file publishing, and
// adding a language is a matter of adding one column.
internal static class Loc
{
    private static int column; // 0 = English, 1 = German

    static Loc() => Initialize("auto");

    public static void Initialize(string? language)
    {
        string effective = string.IsNullOrWhiteSpace(language) || language == "auto"
            ? CultureInfo.CurrentUICulture.TwoLetterISOLanguageName
            : language;
        column = effective.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
    }

    public static string T(string key) =>
        Table.TryGetValue(key, out string[]? row) ? row[column] : key;

    public static string F(string key, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, T(key), args);

    private static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal)
    {
        // common
        ["Common.Save"] = ["Save", "Speichern"],
        ["Common.Cancel"] = ["Cancel", "Abbrechen"],
        ["Common.Close"] = ["Close", "Schließen"],

        // tray
        ["Tray.Settings"] = ["Settings…", "Einstellungen…"],
        ["Tray.Enabled"] = ["Automatic positioning", "Automatische Positionierung"],
        ["Tray.Advanced"] = ["Advanced", "Erweitert"],
        ["Tray.ForgetCurrent"] = [
            "Forget positions for this display setup",
            "Positionen dieser Monitor-Konfiguration vergessen"],
        ["Tray.ForgetAll"] = ["Forget all saved positions", "Alle gespeicherten Positionen vergessen"],
        ["Tray.ForgetAllConfirm"] = [
            "Forget positions for every display setup?",
            "Positionen für alle Monitor-Konfigurationen vergessen?"],
        ["Tray.OpenData"] = ["Open data folder", "Datenordner öffnen"],
        ["Tray.About"] = ["About and diagnostics…", "Info und Diagnose…"],
        ["Tray.Exit"] = ["Exit", "Beenden"],
        ["Tray.Tooltip"] = [
            "WindowKeeper – Win+Shift+Z toggles automatic positioning",
            "WindowKeeper – Win+Umschalt+Z schaltet die Automatik um"],
        ["Tray.BalloonEnabled"] = ["Automatic positioning enabled", "Automatische Positionierung aktiviert"],
        ["Tray.BalloonDisabled"] = ["Automatic positioning disabled", "Automatische Positionierung deaktiviert"],

        // settings dialog
        ["Settings.Title"] = ["WindowKeeper settings", "WindowKeeper-Einstellungen"],
        ["Settings.General"] = ["General", "Allgemein"],
        ["Settings.Enable"] = ["Enable automatic positioning", "Automatische Positionierung aktivieren"],
        ["Settings.Threshold"] = ["Top-left detection distance", "Erkennungsabstand zur linken oberen Ecke"],
        ["Settings.Pixels"] = ["pixels", "Pixel"],
        ["Settings.MinLifetime"] = [
            "Ignore untouched windows shorter than",
            "Unberührte Fenster ignorieren, wenn kürzer offen als"],
        ["Settings.Seconds"] = ["seconds", "Sekunden"],
        ["Settings.MaxAge"] = ["Forget unused positions after", "Ungenutzte Positionen vergessen nach"],
        ["Settings.Days"] = ["days", "Tagen"],
        ["Settings.Cascade"] = ["Offset additional matching windows by", "Weitere gleiche Fenster versetzen um"],
        ["Settings.Language"] = ["Language", "Sprache"],
        ["Settings.LanguageAuto"] = ["Automatic (system)", "Automatisch (System)"],
        ["Settings.AddOpen"] = ["Add an open application:", "Offene Anwendung hinzufügen:"],
        ["Settings.AddRule"] = ["Add rule", "Regel hinzufügen"],
        ["Settings.Rules"] = ["Application rules", "Anwendungsregeln"],
        ["Settings.AddManual"] = ["Add manually", "Manuell hinzufügen"],
        ["Settings.RemoveSelected"] = ["Remove selected", "Auswahl entfernen"],
        ["Settings.ColProcess"] = ["Process", "Prozess"],
        ["Settings.ColMode"] = ["Mode", "Modus"],
        ["Settings.ColShare"] = ["Share position", "Position teilen"],
        ["Settings.ColHide"] = ["Hide titles", "Titel verbergen"],
        ["Settings.RuleNeedsProcess"] = [
            "Every rule needs a process name.",
            "Jede Regel braucht einen Prozessnamen."],

        // about dialog
        ["About.Title"] = ["About WindowKeeper", "Über WindowKeeper"],
        ["About.Tagline"] = [
            "Remembers and restores Windows window positions. No telemetry is collected.",
            "Merkt sich Fensterpositionen und stellt sie wieder her. Es werden keine Telemetriedaten erhoben."],
        ["About.CheckUpdates"] = ["Check for updates", "Nach Updates suchen"],
        ["About.Checking"] = ["Checking…", "Wird geprüft…"],
        ["About.UpToDate"] = ["You are up to date.", "Alles aktuell."],
        ["About.UpdateAvailable"] = ["Version {0} is available.", "Version {0} ist verfügbar."],
        ["About.UpdatePrompt"] = [
            "WindowKeeper {0} is available. Open the release page?",
            "WindowKeeper {0} ist verfügbar. Release-Seite öffnen?"],
        ["About.UpdateTitle"] = ["WindowKeeper update", "WindowKeeper-Update"],
        ["About.UpdateFailed"] = ["Update check failed.", "Update-Prüfung fehlgeschlagen."],
        ["About.ReportProblem"] = ["Report a problem", "Problem melden"],
        ["About.OpenLogs"] = ["Open logs", "Logs öffnen"],
        ["About.CopyDiagnostics"] = ["Copy diagnostics", "Diagnose kopieren"],

        // installer / arguments
        ["Install.Failed"] = ["Installation failed:", "Installation fehlgeschlagen:"],
        ["Install.UninstallFailed"] = ["Uninstall failed:", "Deinstallation fehlgeschlagen:"],
        ["Args.Unknown"] = [
            "Unknown argument: {0}\r\n\r\nSupported arguments:\r\n" +
                "--install     register and start the logon task\r\n" +
                "--uninstall   stop WindowKeeper and remove the task\r\n" +
                "--register-task / --unregister-task   installer integration",
            "Unbekanntes Argument: {0}\r\n\r\nUnterstützte Argumente:\r\n" +
                "--install     Anmeldeaufgabe registrieren und starten\r\n" +
                "--uninstall   WindowKeeper beenden und Aufgabe entfernen\r\n" +
                "--register-task / --unregister-task   Installer-Integration"],
    };
}
