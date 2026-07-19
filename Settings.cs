namespace WindowKeeper;

internal sealed class Settings
{
    public bool Enabled { get; set; } = true;
    public string Language { get; set; } = "auto"; // auto | en | de
    public int TopLeftThreshold { get; set; } = 350;
    public int MinLifetimeMs { get; set; } = 10_000;
    public int MaxAgeDays { get; set; } = 90;
    public int CascadeOffset { get; set; } = 32;
    public List<WindowRule> Rules { get; set; } = [];

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
        if (Language != "en" && Language != "de")
            Language = "auto";
        TopLeftThreshold = Math.Clamp(TopLeftThreshold, 0, 5_000);
        MinLifetimeMs = Math.Clamp(MinLifetimeMs, 0, 3_600_000);
        MaxAgeDays = Math.Clamp(MaxAgeDays, 1, 3_650);
        CascadeOffset = Math.Clamp(CascadeOffset, 0, 250);
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
            rule.Mode = rule.Mode.ToLowerInvariant();
            if (rule.IgnoreTitle)
                rule.HashTitle = false;
        }
        Rules.RemoveAll(rule => string.IsNullOrWhiteSpace(rule.Process));
        Rules = Rules
            .GroupBy(rule => rule.Process, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(rule => rule.Process, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public WindowRule? RuleFor(string process) =>
        Rules.FirstOrDefault(rule => string.Equals(
            rule.Process, process, StringComparison.OrdinalIgnoreCase));
}
