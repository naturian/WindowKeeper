using System.Text;
using System.Text.Json;

namespace WindowKeeper;

internal static class AtomicJsonFile
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(false);
    internal static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public static T? Read<T>(string path)
    {
        if (!File.Exists(path))
            return default;

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path, Utf8WithoutBom));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Error(ex);
            Quarantine(path);
            return ReadBackup<T>(path + ".bak");
        }
    }

    public static void Write<T>(string path, T value)
    {
        string? directory = Path.GetDirectoryName(path);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException($"No directory is available for '{path}'.");

        Directory.CreateDirectory(directory);
        string temporary = path + ".tmp-" + Environment.ProcessId;
        string backup = path + ".bak";

        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(value, Options), Utf8WithoutBom);
            if (File.Exists(path))
            {
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Replace(temporary, path, backup, true);
            }
            else
            {
                File.Move(temporary, path);
            }
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static T? ReadBackup<T>(string backupPath)
    {
        if (!File.Exists(backupPath))
            return default;
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(backupPath, Utf8WithoutBom));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Error(ex);
            return default;
        }
    }

    private static void Quarantine(string path)
    {
        try
        {
            string? directory = Path.GetDirectoryName(path);
            if (string.IsNullOrEmpty(directory) || !File.Exists(path))
                return;

            string name = Path.GetFileNameWithoutExtension(path);
            string extension = Path.GetExtension(path);
            string quarantine = Path.Combine(directory,
                $"{name}.corrupt-{DateTimeOffset.Now:yyyyMMdd-HHmmss}{extension}");
            File.Move(path, quarantine, true);
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
        }
    }
}
