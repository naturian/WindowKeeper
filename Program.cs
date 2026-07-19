using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace WindowKeeper;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, "WindowKeeper_SingleInstance", out bool first);
        if (!first)
            return;
        // Log errors instead of crashing — a background utility must not die
        // because of a single misbehaving window
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => LogError(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => LogError(e.ExceptionObject);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.Run(new KeeperContext());
    }

    private static void LogError(object error)
    {
        try
        {
            File.AppendAllText(Path.Combine(Path.GetTempPath(), "windowkeeper-error.log"),
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}\r\n{error}\r\n\r\n");
        }
        catch
        {
        }
    }
}

// Remembered normal position of a window
internal sealed class Placement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public DateTime LastUsed { get; set; }
}

// A currently open window whose position is being tracked
internal sealed class TrackedWindow
{
    public string Key;
    public Placement Last;
    public long OpenedAt;      // Environment.TickCount64 at first sighting
    public bool UserMoved;     // set when an interactive move/resize ends
}

internal sealed class KeeperContext : ApplicationContext
{
    // ===== Settings ==========================================================
    // The top-left threshold decides which windows count as "opened in the
    // top-left corner" (typical for Device Manager and other MMC/system
    // tools that never manage their own position).
    private const int TopLeftThreshold = 350;
    private const int FirstPassMs = 150;
    // Some programs (e.g. MMC) set their position only after the window has
    // been created — hence a second delayed pass.
    private const int SecondPassMs = 700;
    private const int TrackingIntervalMs = 4000;
    // Positions of short-lived windows the user never touched (splash
    // screens, transient dialogs) are not worth remembering.
    private const int MinLifetimeMs = 10_000;
    // Entries that have not been used for this long are pruned at startup.
    private const int MaxAgeDays = 90;
    // =========================================================================

    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowKeeper", "positions.json");

    private readonly HookWindow hook;
    private readonly NotifyIcon tray;
    private ToolStripMenuItem enabledItem;
    private readonly System.Windows.Forms.Timer trackingTimer;
    // Positions are stored per monitor configuration (profile), so that e.g.
    // an ultrawide setup and a TV resolution do not overwrite each other
    private readonly Dictionary<string, Dictionary<string, Placement>> profiles;
    private Dictionary<string, Placement> saved; // active profile
    private string activeProfile;
    private readonly Dictionary<IntPtr, TrackedWindow> tracked = new();
    private bool enabled = true;

    public KeeperContext()
    {
        profiles = Load();
        activeProfile = ProfileKey();
        if (!profiles.TryGetValue(activeProfile, out saved))
        {
            saved = new Dictionary<string, Placement>();
            profiles[activeProfile] = saved;
        }

        hook = new HookWindow();
        hook.WindowCreated += OnWindowCreated;
        hook.WindowDestroyed += OnWindowDestroyed;
        hook.WindowMoved += OnWindowMoved;
        hook.HotkeyToggle += Toggle;

        trackingTimer = new System.Windows.Forms.Timer { Interval = TrackingIntervalMs };
        trackingTimer.Tick += (s, e) => UpdateTracked();
        trackingTimer.Start();

        Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        tray = CreateTray();

        Application.ApplicationExit += (s, e) => Cleanup();
    }

    // ---- Profiles per monitor configuration ---------------------------------

    private static string ProfileKey() =>
        string.Join(";", Screen.AllScreens
            .Select(s => $"{s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}")
            .OrderBy(x => x, StringComparer.Ordinal));

    private void OnDisplayChanged(object sender, EventArgs e)
    {
        try
        {
            string profile = ProfileKey();
            if (profile == activeProfile)
                return;
            Save(); // persist the profile we are leaving
            activeProfile = profile;
            if (!profiles.TryGetValue(profile, out saved))
            {
                saved = new Dictionary<string, Placement>();
                profiles[profile] = saved;
            }
            // open windows keep running; their closing position is stored in
            // the new profile — i.e. for the resolution that is now active
        }
        catch
        {
        }
    }

    // ---- Reacting to new windows --------------------------------------------

    private void OnWindowCreated(IntPtr hwnd)
    {
        if (!enabled)
            return;
        // immediately: the window has only just become visible — hiding,
        // positioning and re-showing it makes the open animation play at the
        // target position instead of the top-left corner
        CorrectImmediately(hwnd);
        // delayed passes as a safety net (MMC repositions itself late)
        Delay(FirstPassMs, () => CheckWindow(hwnd));
        Delay(SecondPassMs, () => CheckWindow(hwnd));
    }

    private void CorrectImmediately(IntPtr hwnd)
    {
        try
        {
            if (!IsNormalWindow(hwnd))
                return;
            if (Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd))
                return;
            if (!Win32.GetWindowRect(hwnd, out var r))
                return;
            var work = Screen.FromHandle(hwnd).WorkingArea;
            bool topLeft = r.Left - work.Left <= TopLeftThreshold && r.Top - work.Top <= TopLeftThreshold;

            // Saved positions apply everywhere — including windows that center
            // themselves (colorcpl etc.) or open beyond the threshold
            // (msinfo32). The title may still be generic at this point (MMC
            // sets the console title later) — for top-left openers a unique
            // process|class match resolves the target anyway.
            string key = KeyFor(hwnd);
            Placement target = null;
            if (!saved.TryGetValue(key, out target) && topLeft)
            {
                string prefix = key[..(key.LastIndexOf('|') + 1)];
                var matches = saved
                    .Where(e => e.Key.StartsWith(prefix, StringComparison.Ordinal))
                    .Take(2).ToList();
                if (matches.Count > 1)
                    return; // ambiguous -> the delayed passes will sort it out
                target = matches.Count == 1 ? matches[0].Value : null;
            }

            if (target != null)
            {
                if (target.Maximized || !IsVisibleOnAnyScreen(target))
                    return; // maximizing is handled by the delayed pass
                if (AnotherWindowWithKey(hwnd, key))
                {
                    Track(hwnd, key);
                    return; // don't stack onto the window that is already open
                }
                if (r.Left != target.X || r.Top != target.Y)
                {
                    Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                    Apply(hwnd, target);
                    Win32.ShowWindow(hwnd, Win32.SW_SHOW);
                }
            }
            else if (topLeft)
            {
                // unknown window without its own position management: center it
                Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                Center(hwnd);
                Win32.ShowWindow(hwnd, Win32.SW_SHOW);
            }
            // track in every case so the closing position gets stored — even
            // for windows we do not (yet) touch
            Track(hwnd, key);
        }
        catch
        {
        }
    }

    private void CheckWindow(IntPtr hwnd)
    {
        if (!enabled)
            return;
        try
        {
            if (!IsNormalWindow(hwnd))
                return;
            if (Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd))
                return;
            if (!Win32.GetWindowRect(hwnd, out var r))
                return;
            var work = Screen.FromHandle(hwnd).WorkingArea;
            bool topLeft = r.Left - work.Left <= TopLeftThreshold && r.Top - work.Top <= TopLeftThreshold;

            string key = KeyFor(hwnd);
            if (saved.TryGetValue(key, out var p) && IsVisibleOnAnyScreen(p))
            {
                if (!AnotherWindowWithKey(hwnd, key))
                    Apply(hwnd, p);
            }
            else if (topLeft)
            {
                Center(hwnd);
            }
            Track(hwnd, key);
        }
        catch
        {
        }
    }

    private void OnWindowDestroyed(IntPtr hwnd)
    {
        if (tracked.Remove(hwnd, out var entry) && ShouldRemember(entry))
        {
            Remember(entry);
            Save();
        }
    }

    // Is another window with the same key already open? Then don't move the
    // new one onto the same spot (e.g. a second Explorer window).
    private bool AnotherWindowWithKey(IntPtr hwnd, string key)
    {
        foreach (var (other, entry) in tracked)
        {
            if (other != hwnd && entry.Key == key && Win32.IsWindow(other))
                return true;
        }
        return false;
    }

    // ---- Tracking positions -------------------------------------------------

    // Update immediately when a move/resize ends — otherwise the new position
    // would be lost if the window is closed faster than the timer samples
    private void OnWindowMoved(IntPtr hwnd)
    {
        if (tracked.TryGetValue(hwnd, out var entry))
        {
            var p = CurrentPlacement(hwnd);
            if (p != null)
            {
                entry.Last = p;
                entry.UserMoved = true; // MOVESIZEEND fires for interactive moves only
            }
        }
    }

    private void Track(IntPtr hwnd, string key)
    {
        var p = CurrentPlacement(hwnd);
        if (p == null)
            return;
        if (tracked.TryGetValue(hwnd, out var entry))
        {
            entry.Key = key; // the title may have settled since the first pass
            entry.Last = p;
        }
        else
        {
            tracked[hwnd] = new TrackedWindow
            {
                Key = key,
                Last = p,
                OpenedAt = Environment.TickCount64,
            };
        }
    }

    // Windows worth remembering: anything that already has an entry, was
    // moved or resized by the user, or stayed open beyond the splash-screen
    // lifetime.
    private bool ShouldRemember(TrackedWindow entry) =>
        saved.ContainsKey(entry.Key)
        || entry.UserMoved
        || Environment.TickCount64 - entry.OpenedAt >= MinLifetimeMs;

    private void Remember(TrackedWindow entry)
    {
        entry.Last.LastUsed = DateTime.Now;
        saved[entry.Key] = entry.Last;
    }

    private static bool Differs(Placement a, Placement b) =>
        a.X != b.X || a.Y != b.Y || a.Width != b.Width || a.Height != b.Height || a.Maximized != b.Maximized;

    private void UpdateTracked()
    {
        bool changed = false;
        foreach (var (hwnd, entry) in tracked.ToList())
        {
            if (!Win32.IsWindow(hwnd))
            {
                tracked.Remove(hwnd);
                if (ShouldRemember(entry))
                {
                    Remember(entry);
                    changed = true;
                }
                continue;
            }
            var p = CurrentPlacement(hwnd);
            if (p != null)
                entry.Last = p;
            // flush open windows into the store as well, so a hard kill of
            // this process (task restart, crash) loses nothing
            if (ShouldRemember(entry)
                && (!saved.TryGetValue(entry.Key, out var current) || Differs(current, entry.Last)))
            {
                Remember(entry);
                changed = true;
            }
        }
        if (changed)
            Save();
    }

    // ---- Moving windows -----------------------------------------------------

    private static void Apply(IntPtr hwnd, Placement p)
    {
        var wp = Win32.WINDOWPLACEMENT.Create();
        if (!Win32.GetWindowPlacement(hwnd, ref wp))
            return;
        wp.rcNormalPosition = new Win32.RECT
        {
            Left = p.X,
            Top = p.Y,
            Right = p.X + p.Width,
            Bottom = p.Y + p.Height,
        };
        wp.showCmd = p.Maximized ? Win32.SW_SHOWMAXIMIZED : Win32.SW_SHOWNORMAL;
        Win32.SetWindowPlacement(hwnd, ref wp);
    }

    private static void Center(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !Win32.IsWindow(hwnd))
            return;
        if (Win32.IsIconic(hwnd) || Win32.IsZoomed(hwnd))
            return;
        if (!Win32.GetWindowRect(hwnd, out var r))
            return;
        var wa = Screen.FromHandle(hwnd).WorkingArea;
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        Win32.SetWindowPos(hwnd, IntPtr.Zero,
            wa.Left + (wa.Width - w) / 2, wa.Top + (wa.Height - h) / 2, 0, 0,
            Win32.SWP_NOSIZE | Win32.SWP_NOZORDER | Win32.SWP_NOACTIVATE);
    }

    // ---- Helpers ------------------------------------------------------------

    private static bool IsNormalWindow(IntPtr hwnd)
    {
        if (!Win32.IsWindow(hwnd) || !Win32.IsWindowVisible(hwnd))
            return false;
        long style = Win32.GetWindowLongPtr(hwnd, Win32.GWL_STYLE).ToInt64();
        if ((style & Win32.WS_CAPTION) != Win32.WS_CAPTION)
            return false;
        long exStyle = Win32.GetWindowLongPtr(hwnd, Win32.GWL_EXSTYLE).ToInt64();
        if ((exStyle & Win32.WS_EX_TOOLWINDOW) != 0)
            return false;
        return WindowTitle(hwnd).Length > 0;
    }

    private static string KeyFor(IntPtr hwnd)
    {
        Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        string process = "?";
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            process = proc.ProcessName;
        }
        catch
        {
        }
        return process + "|" + ClassName(hwnd) + "|" + WindowTitle(hwnd);
    }

    private static Placement CurrentPlacement(IntPtr hwnd)
    {
        var wp = Win32.WINDOWPLACEMENT.Create();
        if (!Win32.GetWindowPlacement(hwnd, ref wp))
            return null;
        var r = wp.rcNormalPosition;
        bool max = wp.showCmd == Win32.SW_SHOWMAXIMIZED
            || (wp.showCmd == Win32.SW_SHOWMINIMIZED && (wp.flags & Win32.WPF_RESTORETOMAXIMIZED) != 0);
        return new Placement
        {
            X = r.Left,
            Y = r.Top,
            Width = r.Right - r.Left,
            Height = r.Bottom - r.Top,
            Maximized = max,
        };
    }

    private static bool IsVisibleOnAnyScreen(Placement p)
    {
        var rect = new Rectangle(p.X, p.Y, p.Width, p.Height);
        return Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(rect));
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        var sb = new StringBuilder(512);
        _ = Win32.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ClassName(IntPtr hwnd)
    {
        var sb = new StringBuilder(256);
        _ = Win32.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static void Delay(int ms, Action action)
    {
        var t = new System.Windows.Forms.Timer { Interval = ms };
        t.Tick += (s, e) =>
        {
            t.Stop();
            t.Dispose();
            action();
        };
        t.Start();
    }

    // ---- Persistence --------------------------------------------------------

    private static Dictionary<string, Dictionary<string, Placement>> Load()
    {
        try
        {
            if (File.Exists(DataPath))
            {
                var profiles = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, Placement>>>(
                    File.ReadAllText(DataPath));
                if (profiles != null)
                {
                    Prune(profiles);
                    return profiles;
                }
            }
        }
        catch
        {
        }
        return new Dictionary<string, Dictionary<string, Placement>>();
    }

    // Drop entries that have not been used in a long time; entries from
    // files predating the LastUsed field get a fresh grace period.
    private static void Prune(Dictionary<string, Dictionary<string, Placement>> profiles)
    {
        var cutoff = DateTime.Now.AddDays(-MaxAgeDays);
        foreach (var profile in profiles.Values)
        {
            foreach (var (key, p) in profile.ToList())
            {
                if (p.LastUsed == default)
                    p.LastUsed = DateTime.Now;
                else if (p.LastUsed < cutoff)
                    profile.Remove(key);
            }
        }
        foreach (var (name, profile) in profiles.ToList())
        {
            if (profile.Count == 0)
                profiles.Remove(name);
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DataPath));
            File.WriteAllText(DataPath,
                JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
        }
    }

    // ---- Tray & lifecycle ---------------------------------------------------

    private NotifyIcon CreateTray()
    {
        var menu = new ContextMenuStrip();
        enabledItem = new ToolStripMenuItem("Automatic positioning") { Checked = true, CheckOnClick = true };
        enabledItem.CheckedChanged += (s, e) => enabled = enabledItem.Checked;
        var forget = new ToolStripMenuItem("Forget saved positions");
        forget.Click += (s, e) =>
        {
            saved.Clear();
            Save();
        };
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (s, e) => ExitThread();
        menu.Items.Add(enabledItem);
        menu.Items.Add(forget);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);
        return new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "WindowKeeper – Win+Shift+Z toggles automatic positioning",
            Visible = true,
            ContextMenuStrip = menu,
        };
    }

    private static Icon LoadTrayIcon()
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

    private void Toggle()
    {
        enabledItem.Checked = !enabledItem.Checked; // also updates "enabled" via CheckedChanged
        tray.ShowBalloonTip(1500, "WindowKeeper",
            enabled ? "Automatic positioning enabled" : "Automatic positioning disabled", ToolTipIcon.Info);
    }

    private void Cleanup()
    {
        Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        trackingTimer.Stop();
        foreach (var entry in tracked.Values)
        {
            if (ShouldRemember(entry))
                Remember(entry);
        }
        Save();
        tray.Visible = false;
        tray.Dispose();
        hook.Release();
    }
}

// Invisible window that receives shell hook messages and hotkeys
internal sealed class HookWindow : NativeWindow
{
    private readonly uint shellMessage;
    private readonly Win32.WinEventDelegate winEventCallback; // keep a reference so the GC doesn't collect the delegate
    private readonly IntPtr moveHook;

    public event Action<IntPtr> WindowCreated;
    public event Action<IntPtr> WindowDestroyed;
    public event Action<IntPtr> WindowMoved;
    public event Action HotkeyToggle;

    public HookWindow()
    {
        CreateHandle(new CreateParams());
        Win32.RegisterShellHookWindow(Handle);
        shellMessage = Win32.RegisterWindowMessage("SHELLHOOK");
        // Win+Z stays free (Windows snap layouts live there);
        // only Win+Shift+Z toggles automatic positioning
        Win32.RegisterHotKey(Handle, 2, Win32.MOD_WIN | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, Win32.VK_Z);
        winEventCallback = OnWinEvent;
        moveHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_MOVESIZEEND, Win32.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, winEventCallback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(IntPtr hook, uint eventId, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject == 0 && idChild == 0 && hwnd != IntPtr.Zero) // OBJID_WINDOW itself only
            WindowMoved?.Invoke(hwnd);
    }

    public void Release()
    {
        Win32.UnhookWinEvent(moveHook);
        Win32.UnregisterHotKey(Handle, 2);
        Win32.DeregisterShellHookWindow(Handle);
        DestroyHandle();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == shellMessage)
        {
            long eventId = (long)m.WParam & 0x7FFF;
            if (eventId == Win32.HSHELL_WINDOWCREATED)
                WindowCreated?.Invoke(m.LParam);
            else if (eventId == Win32.HSHELL_WINDOWDESTROYED)
                WindowDestroyed?.Invoke(m.LParam);
        }
        else if (m.Msg == Win32.WM_HOTKEY)
        {
            if ((long)m.WParam == 2)
                HotkeyToggle?.Invoke();
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
    public const uint WINEVENT_OUTOFCONTEXT = 0;

    public delegate void WinEventDelegate(IntPtr hook, uint eventId, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

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

        public static WINDOWPLACEMENT Create() => new() { length = Marshal.SizeOf<WINDOWPLACEMENT>() };
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
    [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
}
