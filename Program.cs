using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace WindowKeeper;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length > 0)
        {
            return args[0].ToLowerInvariant() switch
            {
                "--install" => Installer.Install(),
                "--uninstall" => Installer.Uninstall(),
                "--register-task" => Installer.RegisterTask(),
                "--unregister-task" => Installer.UnregisterTask(),
                _ => UnknownArgument(args[0]),
            };
        }

        using var singleInstance = new Mutex(true, "WindowKeeper_SingleInstance", out bool first);
        if (!first)
            return 0;
        // Log errors instead of crashing — a background utility must not die
        // because of a single misbehaving window
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (s, e) => AppLog.Error(e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (s, e) => AppLog.Error(e.ExceptionObject);
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.Run(new KeeperContext());
        return 0;
    }

    private static int UnknownArgument(string arg)
    {
        MessageBox.Show($"Unknown argument: {arg}\r\n\r\nSupported arguments:\r\n" +
             "--install     register and start the logon task\r\n" +
             "--uninstall   stop WindowKeeper and remove the task\r\n" +
             "--register-task / --unregister-task   installer integration",
            "WindowKeeper", MessageBoxButtons.OK, MessageBoxIcon.Information);
        return 1;
    }

}

// Self-installation: registers a scheduled task that starts WindowKeeper at
// logon with highest privileges. Elevated rights are required because e.g.
// Device Manager auto-elevates, and UIPI would otherwise block moving its
// windows. Success is silent — the appearing tray icon is the feedback.
internal static class Installer
{
    private const string LegacyTaskName = "WindowKeeper";
    private static readonly string TaskName = LegacyTaskName + "-" +
        (System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName);
    private static readonly string InstallDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowKeeper");
    internal static readonly string InstalledExecutablePath = Path.Combine(InstallDirectory, "WindowKeeper.exe");

    public static int Install() => InstallCore(deployApplication: true, "--install");

    public static int RegisterTask() => InstallCore(deployApplication: false, "--register-task");

    private static int InstallCore(bool deployApplication, string elevationArgument)
    {
        if (!IsElevated())
            return RelaunchElevated(elevationArgument);
        try
        {
            dynamic service = CreateSchedulerService();
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            TryStopTask(folder, TaskName);
            TryStopTask(folder, LegacyTaskName);
            DeleteTaskIfPresent(folder, LegacyTaskName);
            StopCurrentSessionInstances();
            if (deployApplication)
                DeployApplication();
            else
                EnsureRunningFromInstallDirectory();

            dynamic definition = service.NewTask(0);
            definition.RegistrationInfo.Description = "Restores windows to the position they were last closed at.";
            definition.Principal.RunLevel = 1;                    // TASK_RUNLEVEL_HIGHEST
            definition.Settings.DisallowStartIfOnBatteries = false;
            definition.Settings.StopIfGoingOnBatteries = false;
            definition.Settings.ExecutionTimeLimit = "PT0S";      // no time limit
            definition.Settings.MultipleInstances = 2;            // TASK_INSTANCES_IGNORE_NEW
            definition.Settings.StartWhenAvailable = true;
            definition.Settings.RestartCount = 3;
            definition.Settings.RestartInterval = "PT1M";
            dynamic trigger = definition.Triggers.Create(9);      // TASK_TRIGGER_LOGON
            trigger.UserId = Environment.UserDomainName + "\\" + Environment.UserName;
            dynamic action = definition.Actions.Create(0);        // TASK_ACTION_EXEC
            action.Path = InstalledExecutablePath;
            folder.RegisterTaskDefinition(TaskName, definition,
                6 /* TASK_CREATE_OR_UPDATE */, null, null, 3 /* TASK_LOGON_INTERACTIVE_TOKEN */, null);
            folder.GetTask("\\" + TaskName).Run(null);
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            MessageBox.Show("Installation failed:\r\n" + ex.Message,
                "WindowKeeper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    public static int Uninstall() => UninstallCore(removeInstalledFiles: true, "--uninstall");

    public static int UnregisterTask() => UninstallCore(removeInstalledFiles: false, "--unregister-task");

    private static int UninstallCore(bool removeInstalledFiles, string elevationArgument)
    {
        if (!IsElevated())
            return RelaunchElevated(elevationArgument);
        try
        {
            dynamic service = CreateSchedulerService();
            service.Connect();
            dynamic folder = service.GetFolder("\\");
            TryStopTask(folder, TaskName);
            TryStopTask(folder, LegacyTaskName);
            DeleteTaskIfPresent(folder, TaskName);
            DeleteTaskIfPresent(folder, LegacyTaskName);
            StopCurrentSessionInstances();
            if (removeInstalledFiles)
                CleanupInstalledFiles();
            return 0;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            MessageBox.Show("Uninstall failed:\r\n" + ex.Message,
                "WindowKeeper", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    private static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        return new System.Security.Principal.WindowsPrincipal(identity)
            .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    private static int RelaunchElevated(string argument)
    {
        try
        {
            using Process? child = Process.Start(new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = argument,
                UseShellExecute = true,
                Verb = "runas", // UAC prompt
            });
            if (child is null)
                return 1;
            child.WaitForExit();
            return child.ExitCode;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return 1; // elevation declined
        }
    }

    private static dynamic CreateSchedulerService()
    {
        Type type = Type.GetTypeFromProgID("Schedule.Service")
            ?? throw new InvalidOperationException("Windows Task Scheduler is not available.");
        return Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("Windows Task Scheduler could not be started.");
    }

    private static void TryStopTask(dynamic folder, string taskName)
    {
        try
        {
            folder.GetTask("\\" + taskName).Stop(0);
        }
        catch
        {
            // The task may not exist during a first install or repeated uninstall.
        }
    }

    private static void DeleteTaskIfPresent(dynamic folder, string taskName)
    {
        try
        {
            folder.DeleteTask(taskName, 0);
        }
        catch (COMException ex) when (ex.HResult == unchecked((int)0x80070002))
        {
            // Deletion is idempotent; a missing task is the desired state.
        }
    }

    private static void DeployApplication()
    {
        string sourceDirectory = AppContext.BaseDirectory;
        if (string.Equals(
            Path.GetFullPath(sourceDirectory).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(InstallDirectory).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Directory.CreateDirectory(InstallDirectory);
        string[] allowedExtensions = [".exe", ".dll", ".json", ".pdb"];
        foreach (string source in Directory.EnumerateFiles(sourceDirectory, "WindowKeeper.*"))
        {
            if (!allowedExtensions.Contains(Path.GetExtension(source), StringComparer.OrdinalIgnoreCase))
                continue;
            File.Copy(source, Path.Combine(InstallDirectory, Path.GetFileName(source)), true);
        }

        if (!File.Exists(InstalledExecutablePath))
            throw new FileNotFoundException("The installed executable was not created.", InstalledExecutablePath);
    }

    private static void EnsureRunningFromInstallDirectory()
    {
        if (!string.Equals(
            Path.GetFullPath(Application.ExecutablePath),
            Path.GetFullPath(InstalledExecutablePath),
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Installer integration is only allowed from '{InstalledExecutablePath}'.");
        }
    }

    private static void StopCurrentSessionInstances()
    {
        int currentSession = Process.GetCurrentProcess().SessionId;
        foreach (Process process in Process.GetProcessesByName("WindowKeeper"))
        {
            using (process)
            {
                if (process.Id == Environment.ProcessId)
                    continue;
                try
                {
                    if (process.SessionId != currentSession)
                        continue;
                    process.Kill();
                    process.WaitForExit(5_000);
                }
                catch (Exception ex)
                {
                    AppLog.Error(ex);
                }
            }
        }
    }

    private static void CleanupInstalledFiles()
    {
        if (!Directory.Exists(InstallDirectory))
            return;

        string currentExecutable = Path.GetFullPath(Application.ExecutablePath);
        bool runningFromInstallDirectory = string.Equals(
            currentExecutable, InstalledExecutablePath, StringComparison.OrdinalIgnoreCase);

        if (!runningFromInstallDirectory)
        {
            Directory.Delete(InstallDirectory, true);
            return;
        }

        string commandDirectory = InstallDirectory.Replace("\"", "\"\"");
        var cleanup = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
        };
        cleanup.ArgumentList.Add("/d");
        cleanup.ArgumentList.Add("/c");
        cleanup.ArgumentList.Add($"ping 127.0.0.1 -n 3 > nul & rmdir /s /q \"{commandDirectory}\"");
        _ = Process.Start(cleanup);
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
    public DateTimeOffset LastUsed { get; set; }
}

// A currently open window whose position is being tracked
internal sealed class TrackedWindow
{
    public string Key = "";
    public Placement Last = new();
    public long OpenedAt;      // Environment.TickCount64 at first sighting
    public bool UserMoved;     // set when an interactive move/resize ends
}

// Per-process rule from settings.json
internal sealed class WindowRule
{
    public string Process { get; set; } = "";     // process name, without .exe
    public string Mode { get; set; } = "normal";  // normal | ignore | center
    public bool IgnoreTitle { get; set; }         // key becomes process|class| — useful
                                                  // for apps with document titles
    public bool HashTitle { get; set; }           // avoid storing document names/URLs in plaintext
}

// User configuration, loaded once at startup from settings.json next to the
// position store; a default file is written on first run
internal sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public int TopLeftThreshold { get; set; } = 350;
    public int MinLifetimeMs { get; set; } = 10_000;
    public int MaxAgeDays { get; set; } = 90;
    public List<WindowRule> Rules { get; set; } = new();

    internal static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowKeeper", "settings.json");

    public static Settings Load()
    {
        try
        {
            Settings loaded = AtomicJsonFile.Read<Settings>(SettingsPath) ?? new Settings();
            loaded.Normalize();
            if (!File.Exists(SettingsPath))
                loaded.Save();
            return loaded;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            return new Settings();
        }
    }

    public void Save()
    {
        try
        {
            Normalize();
            AtomicJsonFile.Write(SettingsPath, this);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    internal void Normalize()
    {
        TopLeftThreshold = Math.Clamp(TopLeftThreshold, 0, 5_000);
        MinLifetimeMs = Math.Clamp(MinLifetimeMs, 0, 3_600_000);
        MaxAgeDays = Math.Clamp(MaxAgeDays, 1, 3_650);
        Rules ??= [];
        foreach (WindowRule rule in Rules)
        {
            rule.Process = Path.GetFileNameWithoutExtension(rule.Process?.Trim() ?? "");
            if (!string.Equals(rule.Mode, "normal", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(rule.Mode, "ignore", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(rule.Mode, "center", StringComparison.OrdinalIgnoreCase))
            {
                rule.Mode = "normal";
            }
        }
        Rules.RemoveAll(rule => string.IsNullOrWhiteSpace(rule.Process));
    }

    public WindowRule? RuleFor(string process) =>
        Rules.FirstOrDefault(r => string.Equals(r.Process, process, StringComparison.OrdinalIgnoreCase));
}

internal sealed class KeeperContext : ApplicationContext
{
    private const int FirstPassMs = 150;
    // Some programs (e.g. MMC) set their position only after the window has
    // been created — hence a second delayed pass.
    private const int SecondPassMs = 700;
    private const int TrackingIntervalMs = 4000;
    // Threshold, minimum lifetime and entry expiry are user-configurable —
    // see the Settings class and settings.json.

    private static readonly string DataPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "WindowKeeper", "positions.json");

    private readonly Settings settings;
    private readonly HookWindow hook;
    private readonly NotifyIcon tray;
    private ToolStripMenuItem enabledItem = null!;
    private readonly System.Windows.Forms.Timer trackingTimer;
    // Positions are stored per monitor configuration (profile), so that e.g.
    // an ultrawide setup and a TV resolution do not overwrite each other
    private readonly Dictionary<string, Dictionary<string, Placement>> profiles;
    private Dictionary<string, Placement> saved = null!; // active profile
    private string activeProfile = "";
    private readonly Dictionary<IntPtr, TrackedWindow> tracked = new();
    private bool enabled;

    public KeeperContext()
    {
        settings = Settings.Load();
        enabled = settings.Enabled;
        profiles = Load(settings.MaxAgeDays);
        activeProfile = ProfileKey();
        if (!profiles.TryGetValue(activeProfile, out Dictionary<string, Placement>? activeSaved))
        {
            activeSaved = new Dictionary<string, Placement>();
            profiles[activeProfile] = activeSaved;
        }
        saved = activeSaved;

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

        // windows that are already open when WindowKeeper starts (logon race,
        // tool restart) should have their closing position saved as well
        AdoptExistingWindows();

        Application.ApplicationExit += (s, e) => Cleanup();
    }

    private void AdoptExistingWindows()
    {
        try
        {
            Win32.EnumWindows((hwnd, _) =>
            {
                try
                {
                    if (IsNormalWindow(hwnd))
                    {
                        string key = KeyFor(hwnd);
                        var rule = settings.RuleFor(ProcessOf(key));
                        if (!IsMode(rule, "ignore") && !IsMode(rule, "center"))
                            Track(hwnd, key);
                    }
                }
                catch
                {
                }
                return true;
            }, IntPtr.Zero);
        }
        catch
        {
        }
    }

    private static string ProcessOf(string key) => key[..key.IndexOf('|')];

    private static bool IsMode(WindowRule? rule, string mode) =>
        rule != null && string.Equals(rule.Mode, mode, StringComparison.OrdinalIgnoreCase);

    // ---- Profiles per monitor configuration ---------------------------------

    internal static string ProfileKey() =>
        string.Join(";", Screen.AllScreens
            .Select(s => $"{s.DeviceName}:{s.Bounds.Width}x{s.Bounds.Height}@{s.Bounds.X},{s.Bounds.Y}" +
                $":work={s.WorkingArea.X},{s.WorkingArea.Y},{s.WorkingArea.Width}x{s.WorkingArea.Height}" +
                $":dpi={Win32.DpiForScreen(s)}")
            .OrderBy(x => x, StringComparer.Ordinal));

    private void OnDisplayChanged(object? sender, EventArgs e)
    {
        try
        {
            string profile = ProfileKey();
            if (profile == activeProfile)
                return;
            Save(); // persist the profile we are leaving
            activeProfile = profile;
            if (!profiles.TryGetValue(profile, out Dictionary<string, Placement>? profileSaved))
            {
                profileSaved = new Dictionary<string, Placement>();
                profiles[profile] = profileSaved;
            }
            saved = profileSaved;
            // Refresh immediately so a window closed before the next timer tick
            // cannot write coordinates from the previous display profile.
            RefreshTrackedPlacements();
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    private void RefreshTrackedPlacements()
    {
        foreach (var (hwnd, entry) in tracked)
        {
            Placement? placement = CurrentPlacement(hwnd);
            if (placement != null)
                entry.Last = placement;
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
            bool topLeft = PlacementGeometry.IsNearTopLeft(
                Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom),
                work,
                settings.TopLeftThreshold);

            // Saved positions apply everywhere — including windows that center
            // themselves or open beyond the threshold. The title may still be
            // generic at this point (MMC sets the console title later) — for
            // top-left openers a unique process|class match resolves the
            // target anyway.
            string key = KeyFor(hwnd);
            var rule = settings.RuleFor(ProcessOf(key));
            if (IsMode(rule, "ignore"))
                return;
            if (IsMode(rule, "center"))
            {
                Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                Center(hwnd);
                Win32.ShowWindow(hwnd, Win32.SW_SHOWNA);
                return; // always centered, never remembered
            }
            Placement? target = null;
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
                target = EnsureVisible(target);
                if (target.Maximized)
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
                    Win32.ShowWindow(hwnd, Win32.SW_SHOWNA);
                }
            }
            else if (topLeft)
            {
                // unknown window without its own position management: center it
                Win32.ShowWindow(hwnd, Win32.SW_HIDE);
                Center(hwnd);
                Win32.ShowWindow(hwnd, Win32.SW_SHOWNA);
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
            bool topLeft = PlacementGeometry.IsNearTopLeft(
                Rectangle.FromLTRB(r.Left, r.Top, r.Right, r.Bottom),
                work,
                settings.TopLeftThreshold);

            string key = KeyFor(hwnd);
            var rule = settings.RuleFor(ProcessOf(key));
            if (IsMode(rule, "ignore"))
                return;
            if (IsMode(rule, "center"))
            {
                Center(hwnd);
                return;
            }
            if (saved.TryGetValue(key, out var p))
            {
                if (!AnotherWindowWithKey(hwnd, key))
                    Apply(hwnd, EnsureVisible(p));
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

    private static Placement EnsureVisible(Placement placement)
    {
        Point workspaceOffset = Screen.PrimaryScreen?.WorkingArea.Location ?? Point.Empty;
        Rectangle[] workAreas = Screen.AllScreens.Select(screen => screen.WorkingArea).ToArray();
        return PlacementGeometry.EnsureVisible(placement, workAreas, workspaceOffset);
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
        || Environment.TickCount64 - entry.OpenedAt >= settings.MinLifetimeMs;

    private void Remember(TrackedWindow entry)
    {
        entry.Last.LastUsed = DateTimeOffset.UtcNow;
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

    private string KeyFor(IntPtr hwnd)
    {
        _ = Win32.GetWindowThreadProcessId(hwnd, out uint pid);
        string process = "?";
        try
        {
            using var proc = Process.GetProcessById((int)pid);
            process = proc.ProcessName;
        }
        catch
        {
        }
        var rule = settings.RuleFor(process);
        string title = rule?.IgnoreTitle == true ? "" : WindowTitle(hwnd);
        if (rule?.HashTitle == true && title.Length > 0)
            title = "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(title)));
        return process + "|" + ClassName(hwnd) + "|" + title;
    }

    private static Placement? CurrentPlacement(IntPtr hwnd)
    {
        var wp = Win32.WINDOWPLACEMENT.Create();
        if (!Win32.GetWindowPlacement(hwnd, ref wp))
            return null;
        var r = wp.rcNormalPosition;
        int width = r.Right - r.Left;
        int height = r.Bottom - r.Top;
        if (width <= 0 || height <= 0)
            return null;
        bool max = wp.showCmd == Win32.SW_SHOWMAXIMIZED
            || (wp.showCmd == Win32.SW_SHOWMINIMIZED && (wp.flags & Win32.WPF_RESTORETOMAXIMIZED) != 0);
        return new Placement
        {
            X = r.Left,
            Y = r.Top,
            Width = width,
            Height = height,
            Maximized = max,
        };
    }

    private static string WindowTitle(IntPtr hwnd)
    {
        int length = Math.Clamp(Win32.GetWindowTextLength(hwnd), 0, 32_767);
        var sb = new StringBuilder(length + 1);
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

    private static Dictionary<string, Dictionary<string, Placement>> Load(int maxAgeDays)
    {
        try
        {
            var profiles = AtomicJsonFile.Read<Dictionary<string, Dictionary<string, Placement>>>(DataPath);
            if (profiles != null)
            {
                Prune(profiles, maxAgeDays);
                return profiles;
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
        return new Dictionary<string, Dictionary<string, Placement>>();
    }

    // Drop entries that have not been used in a long time; entries from
    // files predating the LastUsed field get a fresh grace period.
    private static void Prune(Dictionary<string, Dictionary<string, Placement>> profiles, int maxAgeDays)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
        foreach (var profile in profiles.Values)
        {
            foreach (var (key, p) in profile.ToList())
            {
                if (p.LastUsed == default)
                    p.LastUsed = DateTimeOffset.UtcNow;
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
            AtomicJsonFile.Write(DataPath, profiles);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }

    // ---- Tray & lifecycle ---------------------------------------------------

    private NotifyIcon CreateTray()
    {
        var menu = new ContextMenuStrip();
        enabledItem = new ToolStripMenuItem("Automatic positioning") { Checked = enabled, CheckOnClick = true };
        enabledItem.CheckedChanged += (s, e) =>
        {
            enabled = enabledItem.Checked;
            settings.Enabled = enabled;
            settings.Save();
        };
        var forgetCurrent = new ToolStripMenuItem("Forget positions for this display setup");
        forgetCurrent.Click += (s, e) =>
        {
            saved.Clear();
            Save();
        };
        var forgetAll = new ToolStripMenuItem("Forget all saved positions");
        forgetAll.Click += (s, e) =>
        {
            if (MessageBox.Show("Forget positions for every display setup?", "WindowKeeper",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }
            profiles.Clear();
            saved = new Dictionary<string, Placement>();
            profiles[activeProfile] = saved;
            Save();
        };
        var openData = new ToolStripMenuItem("Open data folder");
        openData.Click += (s, e) =>
        {
            string dataDirectory = Path.GetDirectoryName(DataPath)!;
            Directory.CreateDirectory(dataDirectory);
            _ = Process.Start(new ProcessStartInfo(dataDirectory) { UseShellExecute = true });
        };
        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (s, e) => ExitThread();
        menu.Items.Add(enabledItem);
        menu.Items.Add(forgetCurrent);
        menu.Items.Add(forgetAll);
        menu.Items.Add(openData);
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
    private readonly bool shellHookRegistered;
    private readonly bool hotkeyRegistered;

    public event Action<IntPtr>? WindowCreated;
    public event Action<IntPtr>? WindowDestroyed;
    public event Action<IntPtr>? WindowMoved;
    public event Action? HotkeyToggle;

    public HookWindow()
    {
        CreateHandle(new CreateParams());
        shellHookRegistered = Win32.RegisterShellHookWindow(Handle);
        if (!shellHookRegistered)
            AppLog.Error(new Win32Exception(Marshal.GetLastWin32Error(), "RegisterShellHookWindow failed."));
        shellMessage = Win32.RegisterWindowMessage("SHELLHOOK");
        if (shellMessage == 0)
            AppLog.Error(new Win32Exception(Marshal.GetLastWin32Error(), "RegisterWindowMessage failed."));
        // Win+Z stays free (Windows snap layouts live there);
        // only Win+Shift+Z toggles automatic positioning
        hotkeyRegistered = Win32.RegisterHotKey(
            Handle, 2, Win32.MOD_WIN | Win32.MOD_SHIFT | Win32.MOD_NOREPEAT, Win32.VK_Z);
        if (!hotkeyRegistered)
            AppLog.Error(new Win32Exception(Marshal.GetLastWin32Error(), "Win+Shift+Z could not be registered."));
        winEventCallback = OnWinEvent;
        moveHook = Win32.SetWinEventHook(Win32.EVENT_SYSTEM_MOVESIZEEND, Win32.EVENT_SYSTEM_MOVESIZEEND,
            IntPtr.Zero, winEventCallback, 0, 0, Win32.WINEVENT_OUTOFCONTEXT);
        if (moveHook == IntPtr.Zero)
            AppLog.Error(new Win32Exception(Marshal.GetLastWin32Error(), "SetWinEventHook failed."));
    }

    private void OnWinEvent(IntPtr hook, uint eventId, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (idObject == 0 && idChild == 0 && hwnd != IntPtr.Zero) // OBJID_WINDOW itself only
            WindowMoved?.Invoke(hwnd);
    }

    public void Release()
    {
        if (moveHook != IntPtr.Zero)
            _ = Win32.UnhookWinEvent(moveHook);
        if (hotkeyRegistered)
            _ = Win32.UnregisterHotKey(Handle, 2);
        if (shellHookRegistered)
            _ = Win32.DeregisterShellHookWindow(Handle);
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
    public const int SW_SHOWNA = 8;
    public const int WPF_RESTORETOMAXIMIZED = 0x2;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B;
    public const uint WINEVENT_OUTOFCONTEXT = 0;

    public delegate void WinEventDelegate(IntPtr hook, uint eventId, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);
    public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

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

    public static uint DpiForScreen(Screen screen)
    {
        try
        {
            var center = new POINT
            {
                X = screen.Bounds.Left + screen.Bounds.Width / 2,
                Y = screen.Bounds.Top + screen.Bounds.Height / 2,
            };
            IntPtr monitor = MonitorFromPoint(center, 2 /* MONITOR_DEFAULTTONEAREST */);
            if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out uint dpiX, out _) == 0)
                return dpiX;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            AppLog.Error(ex);
        }
        return 96;
    }

    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterShellHookWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool DeregisterShellHookWindow(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] public static extern uint RegisterWindowMessage(string msg);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mod, uint vk);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);
#pragma warning disable CA1838 // StringBuilder is adequate for these small, infrequent window metadata calls.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
#pragma warning restore CA1838
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll", SetLastError = true)] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventDelegate callback, uint pid, uint tid, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool UnhookWinEvent(IntPtr hook);
    [DllImport("user32.dll")] public static extern bool SetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT wp);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT point, uint flags);
    [DllImport("shcore.dll")] private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
}
