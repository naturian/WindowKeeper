using System.Diagnostics;
using System.Text;

namespace WindowKeeper;

internal static class WindowCatalog
{
    public static IReadOnlyList<OpenWindowInfo> Capture()
    {
        var windows = new List<OpenWindowInfo>();
        Win32.EnumWindows((hwnd, _) =>
        {
            try
            {
                if (!Win32.IsWindowVisible(hwnd))
                    return true;

                string title = TitleOf(hwnd);
                if (string.IsNullOrWhiteSpace(title))
                    return true;

                uint threadId = Win32.GetWindowThreadProcessId(hwnd, out uint pid);
                if (threadId == 0 || pid == 0 || pid == Environment.ProcessId)
                    return true;

                using Process process = Process.GetProcessById((int)pid);
                windows.Add(new OpenWindowInfo(process.ProcessName, title));
            }
            catch
            {
                // Protected or already closed windows are expected here.
            }
            return true;
        }, IntPtr.Zero);

        return windows
            .DistinctBy(window => (window.Process.ToUpperInvariant(), window.Title))
            .OrderBy(window => window.Process, StringComparer.OrdinalIgnoreCase)
            .ThenBy(window => window.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private static string TitleOf(IntPtr hwnd)
    {
        int length = Math.Clamp(Win32.GetWindowTextLength(hwnd), 0, 32_767);
        var buffer = new StringBuilder(length + 1);
        _ = Win32.GetWindowText(hwnd, buffer, buffer.Capacity);
        return buffer.ToString();
    }
}
