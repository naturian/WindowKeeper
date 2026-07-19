namespace WindowKeeper;

internal static class PlacementGeometry
{
    internal const int MinimumVisibleWidth = 100;
    internal const int MinimumVisibleHeight = 50;

    public static bool IsNearTopLeft(Rectangle window, Rectangle workArea, int threshold)
    {
        int dx = window.Left - workArea.Left;
        int dy = window.Top - workArea.Top;
        if (dx < 0 || dy < 0 || threshold < 0)
            return false;

        long squaredDistance = (long)dx * dx + (long)dy * dy;
        return squaredDistance <= (long)threshold * threshold;
    }

    public static Placement EnsureVisible(
        Placement placement,
        IReadOnlyList<Rectangle> workAreas,
        Point workspaceOffset)
    {
        int width = Math.Clamp(placement.Width, MinimumVisibleWidth, 32_767);
        int height = Math.Clamp(placement.Height, MinimumVisibleHeight, 32_767);
        var screenRect = new Rectangle(
            placement.X + workspaceOffset.X,
            placement.Y + workspaceOffset.Y,
            width,
            height);

        if (workAreas.Count == 0 || workAreas.Any(area => HasMinimumVisibleArea(screenRect, area)))
            return Copy(placement, placement.X, placement.Y, width, height);

        Rectangle target = workAreas
            .OrderBy(area => DistanceSquared(screenRect, area))
            .First();
        width = Math.Min(width, target.Width);
        height = Math.Min(height, target.Height);
        int screenX = Math.Clamp(screenRect.Left, target.Left, target.Right - width);
        int screenY = Math.Clamp(screenRect.Top, target.Top, target.Bottom - height);
        return Copy(placement,
            screenX - workspaceOffset.X,
            screenY - workspaceOffset.Y,
            width,
            height);
    }

    public static Placement Cascade(Placement placement, int duplicateIndex, int offset)
    {
        int safeIndex = Math.Max(0, duplicateIndex);
        int safeOffset = Math.Max(0, offset);
        long delta = (long)safeIndex * safeOffset;
        var result = Copy(
            placement,
            ClampCoordinate((long)placement.X + delta),
            ClampCoordinate((long)placement.Y + delta),
            placement.Width,
            placement.Height);
        if (safeIndex > 0)
            result.Maximized = false;
        return result;
    }

    private static int ClampCoordinate(long value) =>
        (int)Math.Clamp(value, -32_767L, 32_767L);

    private static bool HasMinimumVisibleArea(Rectangle window, Rectangle workArea)
    {
        Rectangle intersection = Rectangle.Intersect(window, workArea);
        return intersection.Width >= Math.Min(MinimumVisibleWidth, window.Width)
            && intersection.Height >= Math.Min(MinimumVisibleHeight, window.Height);
    }

    private static long DistanceSquared(Rectangle first, Rectangle second)
    {
        long dx = first.Left + first.Width / 2L - (second.Left + second.Width / 2L);
        long dy = first.Top + first.Height / 2L - (second.Top + second.Height / 2L);
        return dx * dx + dy * dy;
    }

    private static Placement Copy(Placement source, int x, int y, int width, int height) => new()
    {
        X = x,
        Y = y,
        Width = width,
        Height = height,
        Maximized = source.Maximized,
        LastUsed = source.LastUsed,
    };
}
