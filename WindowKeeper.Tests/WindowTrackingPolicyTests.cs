using WindowKeeper;
using Xunit;

namespace WindowKeeper.Tests;

public sealed class WindowTrackingPolicyTests
{
    [Fact]
    public void NextAvailableSlotUsesFirstGap()
    {
        Assert.Equal(1, WindowTrackingPolicy.NextAvailableSlot([0, 2, 3]));
    }

    [Theory]
    [InlineData("mmc|MMCMainFrame|Console", "mmc|MMCMainFrame|Geräte-Manager", true)]
    [InlineData("mmc|MMCMainFrame|Geräte-Manager", "mmc|OtherClass|Geräte-Manager", false)]
    [InlineData("explorer|CabinetWClass|A", "notepad|Notepad|A", false)]
    public void WindowFamilyIgnoresOnlyTheSettlingTitle(
        string first, string second, bool expected)
    {
        Assert.Equal(expected, WindowTrackingPolicy.SameWindowFamily(first, second));
    }

    [Fact]
    public void SecondaryWindowCannotDriftCanonicalPositionAcrossCycles()
    {
        const string key = "mmc|MMCMainFrame|Geräte-Manager";
        var canonical = new Placement
        {
            X = 2_400,
            Y = 500,
            Width = 800,
            Height = 600,
        };
        var saved = new Dictionary<string, Placement> { [key] = canonical };

        for (int cycle = 0; cycle < 5; cycle++)
        {
            Placement secondaryPosition = PlacementGeometry.Cascade(saved[key], 1, 32);
            var secondary = new TrackedWindow
            {
                Key = key,
                CascadeSlot = 1,
                Last = secondaryPosition,
            };

            Assert.False(WindowTrackingPolicy.TryRemember(
                saved, secondary, DateTimeOffset.UtcNow));
            Assert.Equal((2_400, 500), (saved[key].X, saved[key].Y));
            Assert.Equal((2_432, 532), (secondaryPosition.X, secondaryPosition.Y));
        }
    }

    [Fact]
    public void PrimaryWindowStillUpdatesCanonicalPosition()
    {
        const string key = "mmc|MMCMainFrame|Geräte-Manager";
        var saved = new Dictionary<string, Placement>();
        var timestamp = new DateTimeOffset(2026, 7, 20, 8, 0, 0, TimeSpan.Zero);
        var primary = new TrackedWindow
        {
            Key = key,
            CascadeSlot = 0,
            Last = new Placement { X = 100, Y = 200, Width = 800, Height = 600 },
        };

        Assert.True(WindowTrackingPolicy.TryRemember(saved, primary, timestamp));
        Assert.Same(primary.Last, saved[key]);
        Assert.Equal(timestamp, saved[key].LastUsed);
    }
}
