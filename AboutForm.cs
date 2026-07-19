using System.Diagnostics;

namespace WindowKeeper;

internal sealed class AboutForm : Form
{
    private readonly string diagnostics;
    private readonly Button checkUpdates = new() { Text = "Check for updates", AutoSize = true };
    private readonly Label updateStatus = new() { AutoSize = true, Anchor = AnchorStyles.Left };

    public AboutForm(string diagnosticText)
    {
        diagnostics = diagnosticText;
        Text = "About WindowKeeper";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(660, 500);
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        Controls.Add(BuildLayout());
    }

    private TableLayoutPanel BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 5,
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(new Label
        {
            Text = $"WindowKeeper {Diagnostics.VersionText}",
            AutoSize = true,
            Font = new Font(FontFamily.GenericSansSerif, 16, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Text = "Remembers and restores Windows window positions. No telemetry is collected.",
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 12),
        }, 0, 1);

        var report = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Dock = DockStyle.Fill,
            Font = new Font(FontFamily.GenericMonospace, 9),
            Text = diagnostics,
        };
        root.Controls.Add(report, 0, 2);

        var updateRow = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 10, 0, 4),
        };
        checkUpdates.Click += CheckUpdatesClicked;
        updateRow.Controls.Add(checkUpdates);
        updateRow.Controls.Add(updateStatus);
        root.Controls.Add(updateRow, 0, 3);

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var close = new Button { Text = "Close", AutoSize = true, DialogResult = DialogResult.OK };
        var issues = new Button { Text = "Report a problem", AutoSize = true };
        issues.Click += (_, _) => OpenUrl("https://github.com/naturian/WindowKeeper/issues/new");
        var logs = new Button { Text = "Open logs", AutoSize = true };
        logs.Click += (_, _) => OpenLogs();
        var copy = new Button { Text = "Copy diagnostics", AutoSize = true };
        copy.Click += (_, _) => Clipboard.SetText(diagnostics);
        buttons.Controls.Add(close);
        buttons.Controls.Add(issues);
        buttons.Controls.Add(logs);
        buttons.Controls.Add(copy);
        root.Controls.Add(buttons, 0, 4);
        AcceptButton = close;
        return root;
    }

    private async void CheckUpdatesClicked(object? sender, EventArgs e)
    {
        checkUpdates.Enabled = false;
        updateStatus.Text = "Checking…";
        try
        {
            UpdateResult result = await UpdateChecker.CheckAsync();
            if (!result.Available)
            {
                updateStatus.Text = "You are up to date.";
                return;
            }

            updateStatus.Text = $"Version {result.LatestVersion} is available.";
            if (MessageBox.Show(
                $"WindowKeeper {result.LatestVersion} is available. Open the release page?",
                "WindowKeeper update", MessageBoxButtons.YesNo,
                MessageBoxIcon.Information) == DialogResult.Yes)
            {
                OpenUrl(result.ReleaseUrl);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            updateStatus.Text = "Update check failed.";
        }
        finally
        {
            checkUpdates.Enabled = true;
        }
    }

    private static void OpenLogs()
    {
        Directory.CreateDirectory(AppLog.LogDirectory);
        _ = Process.Start(new ProcessStartInfo(AppLog.LogDirectory) { UseShellExecute = true });
    }

    private static void OpenUrl(string url) =>
        _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
}
