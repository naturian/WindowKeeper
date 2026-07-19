namespace WindowKeeper;

internal sealed class Placement
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
    public DateTimeOffset LastUsed { get; set; }
}

internal sealed class TrackedWindow
{
    public string Key = "";
    public Placement Last = new();
    public long OpenedAt;
    public bool UserMoved;
}

internal sealed class WindowRule
{
    public string Process { get; set; } = "";
    public string Mode { get; set; } = "normal";
    public bool IgnoreTitle { get; set; }
    public bool HashTitle { get; set; }

    public WindowRule Copy() => new()
    {
        Process = Process,
        Mode = Mode,
        IgnoreTitle = IgnoreTitle,
        HashTitle = HashTitle,
    };
}

internal sealed record OpenWindowInfo(string Process, string Title)
{
    public override string ToString() => $"{Process} — {Title}";
}
