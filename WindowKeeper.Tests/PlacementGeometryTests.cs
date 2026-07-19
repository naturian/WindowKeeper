using System.Drawing;
using WindowKeeper;
using Xunit;

namespace WindowKeeper.Tests;

public sealed class PlacementGeometryTests
{
    private static readonly Rectangle WorkArea = new(100, 40, 1_900, 1_040);

    [Theory]
    [InlineData(100, 40, true)]
    [InlineData(400, 40, true)]
    [InlineData(401, 40, false)]
    [InlineData(99, 40, false)]
    [InlineData(100, 39, false)]
    [InlineData(350, 290, false)]
    public void IsNearTopLeftUsesBoundedDistance(int x, int y, bool expected)
    {
        var window = new Rectangle(x, y, 800, 600);

        Assert.Equal(expected, PlacementGeometry.IsNearTopLeft(window, WorkArea, 300));
    }

    [Fact]
    public void EnsureVisibleKeepsAlreadyVisiblePlacement()
    {
        var placement = new Placement { X = 200, Y = 100, Width = 800, Height = 600 };

        Placement result = PlacementGeometry.EnsureVisible(placement, [WorkArea], new Point(100, 40));

        Assert.Equal((200, 100, 800, 600), (result.X, result.Y, result.Width, result.Height));
    }

    [Fact]
    public void EnsureVisibleMovesOffscreenPlacementIntoWorkingArea()
    {
        var placement = new Placement { X = -5_000, Y = -5_000, Width = 800, Height = 600 };

        Placement result = PlacementGeometry.EnsureVisible(placement, [WorkArea], new Point(100, 40));

        Assert.Equal(0, result.X);
        Assert.Equal(0, result.Y);
        Assert.Equal(800, result.Width);
        Assert.Equal(600, result.Height);
    }

    [Fact]
    public void EnsureVisibleConstrainsCorruptDimensions()
    {
        var placement = new Placement { X = 0, Y = 0, Width = -1, Height = 500_000 };

        Placement result = PlacementGeometry.EnsureVisible(placement, [WorkArea], Point.Empty);

        Assert.Equal(PlacementGeometry.MinimumVisibleWidth, result.Width);
        Assert.Equal(WorkArea.Height, result.Height);
    }

    [Fact]
    public void CascadeOffsetsDuplicateAndRestoresItNormally()
    {
        var placement = new Placement
        {
            X = 100,
            Y = 200,
            Width = 800,
            Height = 600,
            Maximized = true,
        };

        Placement result = PlacementGeometry.Cascade(placement, 2, 32);

        Assert.Equal((164, 264), (result.X, result.Y));
        Assert.False(result.Maximized);
    }
}
