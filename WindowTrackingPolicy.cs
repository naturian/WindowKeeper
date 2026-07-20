namespace WindowKeeper;

internal static class WindowTrackingPolicy
{
    public static int NextAvailableSlot(IEnumerable<int> occupiedSlots)
    {
        HashSet<int> occupied = [.. occupiedSlots.Where(slot => slot >= 0)];
        int slot = 0;
        while (occupied.Contains(slot))
            slot++;
        return slot;
    }

    public static bool SameWindowFamily(string firstKey, string secondKey)
    {
        int firstTitleSeparator = firstKey.LastIndexOf('|');
        int secondTitleSeparator = secondKey.LastIndexOf('|');
        return firstTitleSeparator >= 0
            && secondTitleSeparator >= 0
            && firstKey.AsSpan(0, firstTitleSeparator)
                .Equals(secondKey.AsSpan(0, secondTitleSeparator), StringComparison.Ordinal);
    }

    public static bool TryRemember(
        IDictionary<string, Placement> saved,
        TrackedWindow entry,
        DateTimeOffset timestamp)
    {
        if (entry.CascadeSlot != 0)
            return false;

        entry.Last.LastUsed = timestamp;
        saved[entry.Key] = entry.Last;
        return true;
    }
}
