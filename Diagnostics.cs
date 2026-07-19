using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Globalization;

namespace WindowKeeper;

internal static class Diagnostics
{
    public static string Build(int profileCount, int activePositionCount, int ruleCount)
    {
        var text = new StringBuilder();
        text.AppendLine("WindowKeeper diagnostics");
        text.AppendLine(CultureInfo.InvariantCulture, $"Version: {VersionText}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Runtime: {RuntimeInformation.FrameworkDescription}");
        text.AppendLine(CultureInfo.InvariantCulture, $"OS: {RuntimeInformation.OSDescription}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Architecture: {RuntimeInformation.ProcessArchitecture}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Display profiles: {profileCount}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Active saved positions: {activePositionCount}");
        text.AppendLine(CultureInfo.InvariantCulture, $"Application rules: {ruleCount}");
        text.AppendLine("Displays:");
        foreach (Screen screen in Screen.AllScreens)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"- {screen.Bounds.Width}x{screen.Bounds.Height} " +
                $"at {screen.Bounds.X},{screen.Bounds.Y}; " +
                $"work area {screen.WorkingArea.Width}x{screen.WorkingArea.Height}; " +
                $"DPI {Win32.DpiForScreen(screen)}");
        }
        text.AppendLine();
        text.AppendLine("This report intentionally contains no window titles, process names, file paths or identifiers.");
        return text.ToString();
    }

    public static string VersionText =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
}
