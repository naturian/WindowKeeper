using WindowKeeper;
using Xunit;

namespace WindowKeeper.Tests;

public sealed class DialogConstructionTests
{
    [Fact]
    public void SettingsDialogConstructsWithOpenWindowChoices()
    {
        var settings = new Settings
        {
            Rules = [new WindowRule { Process = "notepad", Mode = "center" }],
        };

        using var dialog = new SettingsForm(settings,
            [new OpenWindowInfo("explorer", "Documents")]);

        Assert.Equal("WindowKeeper settings", dialog.Text);
        Assert.True(dialog.Controls.Count > 0);
    }

    [Fact]
    public void AboutDialogConstructsWithDiagnostics()
    {
        using var dialog = new AboutForm("diagnostic report");

        Assert.Equal("About WindowKeeper", dialog.Text);
        Assert.True(dialog.Controls.Count > 0);
    }
}
