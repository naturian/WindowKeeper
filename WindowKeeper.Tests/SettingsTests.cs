using WindowKeeper;
using Xunit;

namespace WindowKeeper.Tests;

public sealed class SettingsTests
{
    [Fact]
    public void NormalizeClampsValuesAndCleansRules()
    {
        var settings = new Settings
        {
            TopLeftThreshold = -10,
            MinLifetimeMs = 9_000_000,
            MaxAgeDays = 0,
            Rules =
            [
                new WindowRule { Process = "  notepad.exe ", Mode = "unexpected" },
                new WindowRule { Process = "  ", Mode = "center" },
            ],
        };

        settings.Normalize();

        Assert.Equal(0, settings.TopLeftThreshold);
        Assert.Equal(3_600_000, settings.MinLifetimeMs);
        Assert.Equal(1, settings.MaxAgeDays);
        WindowRule rule = Assert.Single(settings.Rules);
        Assert.Equal("notepad", rule.Process);
        Assert.Equal("normal", rule.Mode);
    }

    [Fact]
    public void RuleForIsCaseInsensitive()
    {
        var expected = new WindowRule { Process = "Code", HashTitle = true };
        var settings = new Settings { Rules = [expected] };

        Assert.Same(expected, settings.RuleFor("code"));
    }
}
