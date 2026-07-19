using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WindowKeeper;

internal static class Programm
{
    [STAThread]
    private static void Main()
    {
        using var einzelinstanz = new Mutex(true, "WindowKeeper_EinzelInstanz", out bool erste);
        if (!erste)
            return;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.Run(new MerkerKontext());
    }
}

// Gemerkte Normalposition eines Fensters
internal sealed class Platzierung
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Breite { get; set; }
    public int Hoehe { get; set; }
    public bool Maximiert { get; set; }
}

internal sealed class MerkerKontext : ApplicationContext
{
    // ===== Einstellungen =====================================================
    // Nur Fenster anfassen, die nahe der linken oberen Ecke aufgehen (typisch
    // für Geräte-Manager & andere MMC-/System-Tools). Programme, die ihre
    // Position selbst verwalten, öffnen woanders und bleiben unberührt.
    private const int Schwelle = 350;
    private const int ErsterDurchlaufMs = 150;
    // Manche Programme (z. B. MMC) setzen ihre Position erst verzögert nach
    // der Fenstererstellung — daher ein zweiter Durchlauf.
    private const int ZweiterDurchlaufMs = 700;
    private const int VerfolgungsIntervallMs = 4000;
    // =========================================================================

    private static readonly string DatenPfad = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowKeeper", "positionen.json");

    private readonly HookFenster hook;
    private readonly NotifyIcon tray;
    private ToolStripMenuItem aktivEintrag;
    private readonly System.Windows.Forms.Timer verfolgungsTimer;
    private readonly Dictionary<string, Platzierung> gespeichert;
    private readonly Dictionary<IntPtr, (string Schluessel, Platzierung Letzte)> verfolgt = new();
    private readonly HashSet<IntPtr> vorabPositioniert = new();
    private readonly Dictionary<IntPtr, (Platzierung Ziel, int Uebrig)> vorabKorrektur = new();
    private bool aktiv = true;

    public MerkerKontext()
    {
        gespeichert = Laden();

        hook = new HookFenster();
        hook.FensterAngelegt += FruehPositionieren;
        hook.FensterGezeigt += FensterGezeigt;
        hook.FensterOrtGeaendert += FensterOrtGeaendert;
        hook.FensterErstellt += NeuesFenster;
        hook.FensterZerstoert += FensterGeschlossen;
        hook.FensterBewegt += FensterBewegt;
        hook.HotkeyUmschalten += Umschalten;

        verfolgungsTimer = new System.Windows.Forms.Timer { Interval = VerfolgungsIntervallMs };
        verfolgungsTimer.Tick += (s, e) => VerfolgteAktualisieren();
        verfolgungsTimer.Start();

        tray = TrayErstellen();

        Application.ApplicationExit += (s, e) => Aufraeumen();
    }

    // ---- Reaktion auf neue Fenster -----------------------------------------

    // Wird bei EVENT_OBJECT_CREATE aufgerufen, solange das Fenster noch
    // unsichtbar ist: Zielposition schon jetzt setzen, damit die
    // Öffnungsanimation dort abläuft statt oben links. Der Titel wird oft
    // erst kurz nach der Erstellung gesetzt, daher kurze Wiederholungen.
    private void FruehPositionieren(IntPtr hwnd)
    {
        if (!aktiv)
            return;
        try
        {
            if (!Win32.IsWindow(hwnd) || Win32.IsWindowVisible(hwnd))
                return; // schon sichtbar -> die normale Prüfung übernimmt
            long stil = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE).ToInt64();
            if ((stil & Win32.WS_CHILD) != 0 || (stil & Win32.WS_CAPTION) != Win32.WS_CAPTION)
                return;
            if ((Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64() & Win32.WS_EX_TOOLWINDOW) != 0)
                return;
            if (!Win32.GetWindowRect(hwnd, out var r))
                return;
            var arbeit = Screen.FromHandle(hwnd).WorkingArea;
            if (r.Left - arbeit.Left > Schwelle || r.Top - arbeit.Top > Schwelle)
                return;

            // SetWindowPos zeigt/versteckt nichts — sicher, selbst wenn das
            // Fenster zwischen Prüfung und Aufruf sichtbar geworden ist
            var gemerkt = GemerktesZiel(hwnd);
            if (gemerkt != null)
            {
                Win32.SetWindowPos(hwnd, IntPtr.Zero, gemerkt.X, gemerkt.Y, gemerkt.Breite, gemerkt.Hoehe,
                    Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                vorabKorrektur[hwnd] = (gemerkt, 5);
            }
            else
            {
                // Ziel (noch) nicht auflösbar oder unbekannt: vorerst nur
                // zentrieren; die Auflösung versucht es gleich erneut, denn
                // der endgültige Titel wird oft erst später gesetzt
                int b = r.Right - r.Left, h = r.Bottom - r.Top;
                Win32.SetWindowPos(hwnd, IntPtr.Zero,
                    arbeit.Left + (arbeit.Width - b) / 2, arbeit.Top + (arbeit.Height - h) / 2, 0, 0,
                    Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                vorabKorrektur[hwnd] = (null, 5);
                Verzoegert(25, () => FruehAufloesen(hwnd, 1));
            }
            vorabPositioniert.Add(hwnd);
        }
        catch
        {
        }
    }

    // Gemerkte Platzierung zum Fenster: erst voller Schlüssel, sonst über
    // Prozess|Klasse, wenn genau ein Eintrag passt (der Titel stimmt beim
    // Erstellen oft noch nicht — MMC trägt anfangs den generischen Titel)
    private Platzierung GemerktesZiel(IntPtr hwnd)
    {
        string schluessel = SchluesselFuer(hwnd);
        if (!gespeichert.TryGetValue(schluessel, out var p))
        {
            string praefix = schluessel[..(schluessel.LastIndexOf('|') + 1)];
            var passende = gespeichert
                .Where(e => e.Key.StartsWith(praefix, StringComparison.Ordinal))
                .Take(2).ToList();
            p = passende.Count == 1 ? passende[0].Value : null;
        }
        if (p == null || p.Maximiert || !IstSichtbar(p))
            return null; // Maximieren übernimmt die normale Prüfung
        return p;
    }

    // Solange das Fenster unsichtbar ist, weiter versuchen, das Ziel über den
    // (inzwischen vielleicht endgültigen) Titel aufzulösen
    private void FruehAufloesen(IntPtr hwnd, int versuch)
    {
        try
        {
            if (!vorabKorrektur.TryGetValue(hwnd, out var eintrag) || eintrag.Ziel != null)
                return;
            if (!Win32.IsWindow(hwnd) || Win32.IsWindowVisible(hwnd))
                return; // beim Anzeigen übernimmt FensterGezeigt
            var gemerkt = GemerktesZiel(hwnd);
            if (gemerkt != null)
            {
                Win32.SetWindowPos(hwnd, IntPtr.Zero, gemerkt.X, gemerkt.Y, gemerkt.Breite, gemerkt.Hoehe,
                    Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
                vorabKorrektur[hwnd] = (gemerkt, eintrag.Uebrig);
            }
            else if (versuch < 12)
            {
                Verzoegert(25, () => FruehAufloesen(hwnd, versuch + 1));
            }
        }
        catch
        {
        }
    }

    // Manche Programme (MMC) verschieben ihr Fenster und zeigen es im selben
    // Zug an — dieser Wettlauf ist mit asynchronen WinEvents nicht zu gewinnen.
    // Wird ein vorpositioniertes Fenster an der falschen Stelle sichtbar,
    // verstecken wir es sofort, setzen es ans Ziel und zeigen es erneut:
    // die Öffnungsanimation läuft dann am Ziel ab.
    private void FensterGezeigt(IntPtr hwnd)
    {
        if (!vorabKorrektur.TryGetValue(hwnd, out var eintrag))
            return;
        try
        {
            if (!Win32.IsWindow(hwnd) || eintrag.Uebrig <= 0)
            {
                vorabKorrektur.Remove(hwnd);
                return;
            }
            // beim Anzeigen ist der endgültige Titel gesetzt — Ziel neu auflösen
            var ziel = GemerktesZiel(hwnd) ?? eintrag.Ziel;
            if (ziel == null)
            {
                // Titel noch nicht endgültig? Gleich noch einmal versuchen.
                vorabKorrektur[hwnd] = (null, eintrag.Uebrig - 1);
                Verzoegert(40, () => FensterGezeigt(hwnd));
                return;
            }
            if (Win32.GetWindowRect(hwnd, out var r) && r.Left == ziel.X && r.Top == ziel.Y)
            {
                vorabKorrektur.Remove(hwnd); // am Ziel sichtbar geworden -> fertig
                return;
            }
            vorabKorrektur[hwnd] = (ziel, eintrag.Uebrig - 1);
            Win32.ShowWindow(hwnd, Win32.SW_HIDE);
            Win32.SetWindowPos(hwnd, IntPtr.Zero, ziel.X, ziel.Y, ziel.Breite, ziel.Hoehe,
                Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
            Win32.ShowWindow(hwnd, Win32.SW_SHOW);
        }
        catch
        {
        }
    }

    // Solange ein vorpositioniertes Fenster unsichtbar ist, wird jede
    // Eigenbewegung der App sofort ans Ziel zurückkorrigiert; mit dem ersten
    // Anzeigen endet die Überwachung
    private void FensterOrtGeaendert(IntPtr hwnd)
    {
        if (!vorabKorrektur.TryGetValue(hwnd, out var eintrag) || eintrag.Ziel == null)
            return;
        try
        {
            if (!Win32.IsWindow(hwnd) || Win32.IsWindowVisible(hwnd) || eintrag.Uebrig <= 0)
                return; // sichtbar -> FensterGezeigt übernimmt bzw. hat übernommen
            if (!Win32.GetWindowRect(hwnd, out var r))
                return;
            if (r.Left == eintrag.Ziel.X && r.Top == eintrag.Ziel.Y)
                return; // steht am Ziel (auch nach unserem eigenen SetWindowPos)
            vorabKorrektur[hwnd] = (eintrag.Ziel, eintrag.Uebrig - 1);
            Win32.SetWindowPos(hwnd, IntPtr.Zero,
                eintrag.Ziel.X, eintrag.Ziel.Y, eintrag.Ziel.Breite, eintrag.Ziel.Hoehe,
                Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
        }
        catch
        {
        }
    }

    private void NeuesFenster(IntPtr hwnd)
    {
        if (!aktiv)
            return;
        Verzoegert(ErsterDurchlaufMs, () => FensterPruefen(hwnd));
        Verzoegert(ZweiterDurchlaufMs, () => FensterPruefen(hwnd));
    }

    private void FensterPruefen(IntPtr hwnd)
    {
        if (!aktiv)
            return;
        try
        {
            if (!IstNormalesFenster(hwnd))
                return;
            if (Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd))
                return;
            if (!Win32.GetWindowRect(hwnd, out var r))
                return;
            var arbeit = Screen.FromHandle(hwnd).WorkingArea;
            // vorab positionierte Fenster stehen schon am Ziel, brauchen aber
            // trotzdem die Verfolgung; alle anderen nur, wenn sie oben links öffnen
            if (!vorabPositioniert.Contains(hwnd)
                && (r.Left - arbeit.Left > Schwelle || r.Top - arbeit.Top > Schwelle))
                return; // öffnet nicht oben links -> in Ruhe lassen

            string schluessel = SchluesselFuer(hwnd);
            if (gespeichert.TryGetValue(schluessel, out var p) && IstSichtbar(p))
                Anwenden(hwnd, p);
            else
                Zentrieren(hwnd);
            Verfolgen(hwnd, schluessel);
        }
        catch
        {
        }
    }

    private void FensterGeschlossen(IntPtr hwnd)
    {
        vorabPositioniert.Remove(hwnd); // Handles werden wiederverwendet
        vorabKorrektur.Remove(hwnd);
        if (verfolgt.Remove(hwnd, out var eintrag))
        {
            gespeichert[eintrag.Schluessel] = eintrag.Letzte;
            Speichern();
        }
    }

    // ---- Positionen verfolgen ----------------------------------------------

    // Sofort beim Ende eines Verschiebens/Vergrößerns aktualisieren — sonst
    // ginge die neue Position verloren, wenn das Fenster schneller geschlossen
    // wird, als der Timer abtastet
    private void FensterBewegt(IntPtr hwnd)
    {
        if (verfolgt.TryGetValue(hwnd, out var eintrag))
        {
            var p = AktuellePlatzierung(hwnd);
            if (p != null)
                verfolgt[hwnd] = (eintrag.Schluessel, p);
        }
    }

    private void Verfolgen(IntPtr hwnd, string schluessel)
    {
        var p = AktuellePlatzierung(hwnd);
        if (p != null)
            verfolgt[hwnd] = (schluessel, p);
    }

    private void VerfolgteAktualisieren()
    {
        bool geaendert = false;
        foreach (var (hwnd, eintrag) in verfolgt.ToList())
        {
            if (!Win32.IsWindow(hwnd))
            {
                verfolgt.Remove(hwnd);
                gespeichert[eintrag.Schluessel] = eintrag.Letzte;
                geaendert = true;
                continue;
            }
            var p = AktuellePlatzierung(hwnd);
            if (p != null)
                verfolgt[hwnd] = (eintrag.Schluessel, p);
        }
        if (geaendert)
            Speichern();
    }

    // ---- Fenster bewegen ----------------------------------------------------

    private static void Anwenden(IntPtr hwnd, Platzierung p)
    {
        var wp = Win32.WINDOWPLACEMENT.Neu();
        if (!Win32.GetWindowPlacement(hwnd, ref wp))
            return;
        wp.rcNormalPosition = new Win32.RECT
        {
            Left = p.X,
            Top = p.Y,
            Right = p.X + p.Breite,
            Bottom = p.Y + p.Hoehe,
        };
        wp.showCmd = p.Maximiert ? Win32.SW_SHOWMAXIMIZED : Win32.SW_SHOWNORMAL;
        Win32.SetWindowPlacement(hwnd, ref wp);
    }

    private static void Zentrieren(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd))
            return;
        if (Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd))
            return;
        if (!Win32.GetWindowRect(hwnd, out var r))
            return;
        var wa = Screen.FromHandle(hwnd).WorkingArea;
        int b = r.Right - r.Left, h = r.Bottom - r.Top;
        Win32.SetWindowPos(hwnd, IntPtr.Zero,
            wa.Left + (wa.Width - b) / 2, wa.Top + (wa.Height - h) / 2, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    // ---- Hilfsfunktionen ----------------------------------------------------

    private static bool IstNormalesFenster(IntPtr hwnd)
    {
        if (!Win32.IsWindow(hwnd) || !Win32.IsWindowVisible(hwnd))
            return false;
        long stil = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE).ToInt64();
        if ((stil & Win32.WS_CAPTION) != Win32.WS_CAPTION)
            return false;
        long exStil = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        if ((exStil & Win32.WS_EX_TOOLWINDOW) != 0)
            return false;
        return FensterTitel(hwnd).Length > 0;
    }

    private static string SchluesselFuer(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        string prozess = "?";
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            prozess = proc.ProcessName;
        }
        catch
        {
        }
        return prozess + "|" + KlassenName(hwnd) + "|" + FensterTitel(hwnd);
    }

    private static Platzierung AktuellePlatzierung(IntPtr hwnd)
    {
        var wp = Win32.WINDOWPLACEMENT.Neu();
        if (!Win32.GetWindowPlacement(hwnd, ref wp))
            return null;
        var r = wp.rcNormalPosition;
        bool max = wp.showCmd == Win32.SW_SHOWMAXIMIZED
            || (wp.showCmd == Win32.SW_SHOWMINIMIZED && (wp.flags & Win32.WPF_RESTORETOMAXIMIZED) != 0);
        return new Platzierung
        {
            X = r.Left,
            Y = r.Top,
            Breite = r.Right - r.Left,
            Hoehe = r.Bottom - r.Top,
            Maximiert = max,
        };
    }

    private static bool IstSichtbar(Platzierung p)
    {
        var rect = new Rectangle(p.X, p.Y, p.Breite, p.Hoehe);
        return Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect));
    }

    private static string FensterTitel(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        _ = Win32.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string KlassenName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        _ = Win32.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static void Verzoegert(int ms, Action aktion)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += (s, e) =>
        {
            t.Stop();
            t.Dispose();
            aktion();
        };
        t.Start();
    }

    // ---- Speichern / Laden --------------------------------------------------

    private static Dictionary<string, Platzierung> Laden()
    {
        try
        {
            if (File.Exists(DatenPfad))
                return JsonSerializer.Deserialize<Dictionary<string, Platzierung>>(File.ReadAllText(DatenPfad))
                    ?? new Dictionary<string, Platzierung>();
        }
        catch
        {
        }
        return new Dictionary<string, Platzierung>();
    }

    private void Speichern()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DatenPfad));
            File.WriteAllText(DatenPfad,
                JsonSerializer.Serialize(gespeichert, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    // ---- Tray & Lebenszyklus ------------------------------------------------

    private NotifyIcon TrayErstellen()
    {
        var menue = new ContextMenuStrip();
        aktivEintrag = new ToolStripMenuItem("Automatik aktiv") { Checked = true, CheckOnClick = true };
        aktivEintrag.CheckedChanged += (s, e) => aktiv = aktivEintrag.Checked;
        var vergessen = new ToolStripMenuItem("Gemerkte Positionen löschen");
        vergessen.Click += (s, e) =>
        {
            gespeichert.Clear();
            Speichern();
        };
        var beenden = new ToolStripMenuItem("Beenden");
        beenden.Click += (s, e) => ExitThread();
        menue.Items.Add(aktivEintrag);
        menue.Items.Add(vergessen);
        menue.Items.Add(new ToolStripSeparator());
        menue.Items.Add(beenden);
        return new NotifyIcon
        {
            Icon = LadeSymbol(),
            Text = "WindowKeeper – Win+Umschalt+Z schaltet die Automatik um",
            Visible = true,
            ContextMenuStrip = menue,
        };
    }

    private static Icon LadeSymbol()
    {
        try
        {
            return Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        }
        catch
        {
            return SystemIcons.Application;
        }
    }

    private void Umschalten()
    {
        aktivEintrag.Checked = !aktivEintrag.Checked; // setzt über CheckedChanged auch "aktiv"
        tray.ShowBalloonTip(1500, "WindowKeeper", aktiv ? "Automatik aktiviert" : "Automatik deaktiviert", ToolTipIcon.Info);
    }

    private void Aufraeumen()
    {
        verfolgungsTimer.Stop();
        foreach (var eintrag in verfolgt.Values)
            gespeichert[eintrag.Schluessel] = eintrag.Letzte;
        Speichern();
        tray.Visible = false;
        tray.Dispose();
        hook.Freigeben();
    }
}

// Unsichtbares Fenster, das Shell-Hook-Nachrichten und Hotkeys empfängt
internal sealed class HookFenster : NativeWindow
{
    private readonly uint shellNachricht;
    private readonly Win32.WinEventDelegate winEventRueckruf; // Referenz halten, sonst räumt der GC den Delegaten ab
    private readonly IntPtr bewegtHook;
    private readonly IntPtr erstelltHook;
    private readonly IntPtr gezeigtHook;
    private readonly IntPtr ortHook;

    public event Action<IntPtr> FensterErstellt;
    public event Action<IntPtr> FensterAngelegt;
    public event Action<IntPtr> FensterGezeigt;
    public event Action<IntPtr> FensterOrtGeaendert;
    public event Action<IntPtr> FensterZerstoert;
    public event Action<IntPtr> FensterBewegt;
    public event Action HotkeyUmschalten;

    public HookFenster()
    {
        CreateHandle(new CreateParams());
        Win32.RegisterShellHookWindow(Handle);
        shellNachricht = Win32.RegisterWindowMessage("SHELLHOOK");
        // Win+Z bleibt frei (dort liegen die Windows-Snap-Layouts);
        // nur Win+Umschalt+Z zum Umschalten der Automatik
        Win32.RegisterHotKey(Handle, 2, Win32.MOD_WIN | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, Win32.VK_Z);
        winEventRueckruf = BeiWinEvent;
        bewegtHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_MOVESIZEEND, Win32.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, winEventRueckruf, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        // EVENT_OBJECT_CREATE kommt, bevor das Fenster sichtbar wird — nur so
        // lässt es sich vor der Öffnungsanimation an die Zielposition setzen.
        // SHOW und LOCATIONCHANGE steuern den Vorab-Wächter.
        erstelltHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_CREATE, Win32.EVENT_OBJECT_CREATE,
            IntPtr.Zero, winEventRueckruf, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        gezeigtHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_SHOW, Win32.EVENT_OBJECT_SHOW,
            IntPtr.Zero, winEventRueckruf, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        ortHook = Win32.SetWinEventHook(Win32.EVENT_OBJECT_LOCATIONCHANGE, Win32.EVENT_OBJECT_LOCATIONCHANGE,
            IntPtr.Zero, winEventRueckruf, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
    }

    private void BeiWinEvent(IntPtr hook, uint ereignis, IntPtr hwnd, int idObject, int idChild, uint thread, uint zeit)
    {
        if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) // nur OBJID_WINDOW selbst
            return;
        switch (ereignis)
        {
            case Win32.EVENT_OBJECT_CREATE:
                FensterAngelegt?.Invoke(hwnd);
                break;
            case Win32.EVENT_OBJECT_SHOW:
                FensterGezeigt?.Invoke(hwnd);
                break;
            case Win32.EVENT_OBJECT_LOCATIONCHANGE:
                FensterOrtGeaendert?.Invoke(hwnd);
                break;
            default:
                FensterBewegt?.Invoke(hwnd);
                break;
        }
    }

    public void Freigeben()
    {
        Win32.UnhookWinEvent(ortHook);
        Win32.UnhookWinEvent(gezeigtHook);
        Win32.UnhookWinEvent(erstelltHook);
        Win32.UnhookWinEvent(bewegtHook);
        Win32.UnregisterHotKey(Handle, 2);
        Win32.DeregisterShellHookWindow(Handle);
        DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == shellNachricht)
        {
            long ereignis = (long)m.WParam & 0x7FFF;
            if (ereignis == Win32.HSHELL_WINDOWCREATED)
                FensterErstellt?.Invoke(m.LParam);
            else if (ereignis == Win32.HSHELL_WINDOWDESTROYED)
                FensterZerstoert?.Invoke(m.LParam);
        }
        else if (m.Msg == Win32.WM_HOTKEY)
        {
            if ((long)m.WParam == 2)
                HotkeyUmschalten?.Invoke();
        }
        base.WndProc(ref m);
    }
}

internal static class Win32
{
    public const long HSHELL_WINDOWCREATED = 1;
    public const long HSHELL_WINDOWDESTROYED = 2;
    public const int WM_HOTKEY = 0x0312;
    public const uint MOD_SHIFT = 0x4;
    public const uint MOD_WIN = 0x8;
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint VK_Z = 0x5A;
    public const int GWL_STYLE = -16;
    public const int GWL_EXSTYLE = -20;
    public const long WS_CAPTION = 0x00C00000;
    public const long WS_EX_TOOLWINDOW = 0x00000080;
    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const int SW_HIDE = 0;
    public const int SW_SHOW = 5;
    public const int SW_SHOWNORMAL = 1;
    public const int SW_SHOWMINIMIZED = 2;
    public const int SW_SHOWMAXIMIZED = 3;
    public const int WPF_RESTORETOMAXIMIZED = 0x2;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint WINEVENT_OUTOFCONTEXT = 0;
    public const long WS_CHILD = 0x40000000;

    public delegate void WinEventDelegate(IntPtr hook, uint ereignis, IntPtr hwnd, int idObject, int idChild, uint thread, uint zeit);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X, Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WINDOWPLACEMENT
    {
        public int length;
        public int flags;
        public int showCmd;
        public POINT ptMinPosition;
        public POINT ptMaxPosition;
        public RECT rcNormalPosition;

        public static WINDOWPLACEMENT Neu() => new() { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
    }

    [DllImport("user32.dll")] public static extern bool RegisterShellHookWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool DeregisterShellHookWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string msg);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mod, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr modul, WinEventDelegate rueckruf, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
}
