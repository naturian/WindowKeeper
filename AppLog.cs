namespace WindowKeeper;

internal static class AppLog
{
    private const long MaxLogBytes = 1_000_000;
    private static readonly object Sync = new();
    internal static readonly string LogDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WindowKeeper", "Logs");

    internal static readonly string LogPath = Path.Combine(LogDirectory, "windowkeeper.log");

    public static void Error(object? error)
    {
        try
        {
            lock (Sync)
            {
                Directory.CreateDirectory(LogDirectory);
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length >= MaxLogBytes)
                    File.Move(LogPath, Path.Combine(LogDirectory, "windowkeeper.previous.log"), true);

                File.AppendAllText(LogPath,
                    $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz}\r\n{error}\r\n\r\n");
            }
        }
        catch
        {
            // Logging must never bring down the background process.
        }
    }
}
