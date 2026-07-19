using System.ComponentModel;

namespace WindowKeeper;

internal sealed class SettingsForm : Form
{
    private static readonly string[] RuleModes = ["normal", "ignore", "center"];
    private static readonly string[] LanguageCodes = ["auto", "en", "de"];
    private readonly Settings target;
    private readonly BindingList<WindowRule> rules;
    private readonly CheckBox enabled = new() { Text = Loc.T("Settings.Enable"), AutoSize = true };
    private readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly NumericUpDown topLeftThreshold = Number(0, 5_000, 10);
    private readonly NumericUpDown minimumLifetimeSeconds = Number(0, 3_600, 1);
    private readonly NumericUpDown maximumAgeDays = Number(1, 3_650, 1);
    private readonly NumericUpDown cascadeOffset = Number(0, 250, 1);
    private readonly ComboBox openWindows = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly DataGridView ruleGrid = new()
    {
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = true,
        AutoGenerateColumns = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.Fixed3D,
        Dock = DockStyle.Fill,
        MultiSelect = false,
        RowHeadersVisible = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
    };

    public SettingsForm(Settings settings, IReadOnlyList<OpenWindowInfo> windows)
    {
        target = settings;
        rules = new BindingList<WindowRule>(settings.Rules.Select(rule => rule.Copy()).ToList());

        Text = Loc.T("Settings.Title");
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(760, 560);
        Size = new Size(860, 650);
        ShowInTaskbar = true;
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

        enabled.Checked = settings.Enabled;
        language.Items.AddRange([Loc.T("Settings.LanguageAuto"), "English", "Deutsch"]);
        language.SelectedIndex = Math.Max(0, Array.IndexOf(LanguageCodes, settings.Language));
        topLeftThreshold.Value = settings.TopLeftThreshold;
        minimumLifetimeSeconds.Value = settings.MinLifetimeMs / 1_000m;
        maximumAgeDays.Value = settings.MaxAgeDays;
        cascadeOffset.Value = settings.CascadeOffset;

        foreach (OpenWindowInfo window in windows)
            openWindows.Items.Add(window);
        if (openWindows.Items.Count > 0)
            openWindows.SelectedIndex = 0;

        ConfigureRuleGrid();
        Controls.Add(BuildLayout());
        AcceptButton = FindButton(Loc.T("Common.Save"));
        CancelButton = FindButton(Loc.T("Common.Cancel"));
    }

    private TableLayoutPanel BuildLayout()
    {
        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            RowCount = 4,
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var general = new GroupBox { Text = Loc.T("Settings.General"), Dock = DockStyle.Top, AutoSize = true };
        var generalGrid = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Padding = new Padding(10),
        };
        generalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 245));
        generalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        generalGrid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        generalGrid.Controls.Add(enabled, 0, 0);
        generalGrid.SetColumnSpan(enabled, 3);
        AddNumberRow(generalGrid, 1, Loc.T("Settings.Threshold"), topLeftThreshold, Loc.T("Settings.Pixels"));
        AddNumberRow(generalGrid, 2, Loc.T("Settings.MinLifetime"), minimumLifetimeSeconds, Loc.T("Settings.Seconds"));
        AddNumberRow(generalGrid, 3, Loc.T("Settings.MaxAge"), maximumAgeDays, Loc.T("Settings.Days"));
        AddNumberRow(generalGrid, 4, Loc.T("Settings.Cascade"), cascadeOffset, Loc.T("Settings.Pixels"));
        AddNumberRow(generalGrid, 5, Loc.T("Settings.Language"), language, "");
        general.Controls.Add(generalGrid);

        var picker = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 12, 0, 8),
        };
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        picker.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        picker.Controls.Add(new Label { Text = Loc.T("Settings.AddOpen"), AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        picker.Controls.Add(openWindows, 1, 0);
        var add = new Button { Text = Loc.T("Settings.AddRule"), AutoSize = true };
        add.Click += (_, _) => AddSelectedWindow();
        picker.Controls.Add(add, 2, 0);

        var rulesGroup = new GroupBox { Text = Loc.T("Settings.Rules"), Dock = DockStyle.Fill };
        var rulesLayout = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill, Padding = new Padding(8) };
        rulesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rulesLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        rulesLayout.Controls.Add(ruleGrid, 0, 0);
        var ruleButtons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Dock = DockStyle.Fill };
        var addManual = new Button { Text = Loc.T("Settings.AddManual"), AutoSize = true };
        addManual.Click += (_, _) => rules.Add(new WindowRule());
        var remove = new Button { Text = Loc.T("Settings.RemoveSelected"), AutoSize = true };
        remove.Click += (_, _) => RemoveSelectedRule();
        ruleButtons.Controls.Add(addManual);
        ruleButtons.Controls.Add(remove);
        rulesLayout.Controls.Add(ruleButtons, 0, 1);
        rulesGroup.Controls.Add(rulesLayout);

        var bottom = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0),
        };
        var cancel = new Button { Text = Loc.T("Common.Cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = Loc.T("Common.Save"), AutoSize = true };
        save.Click += (_, _) => SaveAndClose();
        bottom.Controls.Add(cancel);
        bottom.Controls.Add(save);

        root.Controls.Add(general, 0, 0);
        root.Controls.Add(picker, 0, 1);
        root.Controls.Add(rulesGroup, 0, 2);
        root.Controls.Add(bottom, 0, 3);
        return root;
    }

    private void ConfigureRuleGrid()
    {
        ruleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(WindowRule.Process),
            HeaderText = Loc.T("Settings.ColProcess"),
            FillWeight = 120,
        });
        ruleGrid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(WindowRule.Mode),
            DataSource = RuleModes,
            HeaderText = Loc.T("Settings.ColMode"),
            FillWeight = 75,
            FlatStyle = FlatStyle.Flat,
        });
        ruleGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(WindowRule.IgnoreTitle),
            HeaderText = Loc.T("Settings.ColShare"),
            FillWeight = 80,
        });
        ruleGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(WindowRule.HashTitle),
            HeaderText = Loc.T("Settings.ColHide"),
            FillWeight = 70,
        });
        ruleGrid.DataSource = rules;
        ruleGrid.DataError += (_, _) => { };
    }

    private void AddSelectedWindow()
    {
        if (openWindows.SelectedItem is not OpenWindowInfo selected)
            return;
        WindowRule? existing = rules.FirstOrDefault(rule => string.Equals(
            rule.Process, selected.Process, StringComparison.OrdinalIgnoreCase));
        if (existing == null)
        {
            existing = new WindowRule { Process = selected.Process };
            rules.Add(existing);
        }
        int index = rules.IndexOf(existing);
        if (index >= 0)
        {
            ruleGrid.ClearSelection();
            ruleGrid.Rows[index].Selected = true;
            ruleGrid.FirstDisplayedScrollingRowIndex = index;
        }
    }

    private void RemoveSelectedRule()
    {
        if (ruleGrid.CurrentRow?.DataBoundItem is WindowRule rule)
            rules.Remove(rule);
    }

    private void SaveAndClose()
    {
        ruleGrid.EndEdit();
        if (rules.Any(rule => string.IsNullOrWhiteSpace(rule.Process)))
        {
            MessageBox.Show(Loc.T("Settings.RuleNeedsProcess"), "WindowKeeper",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        target.Enabled = enabled.Checked;
        target.Language = LanguageCodes[Math.Max(0, language.SelectedIndex)];
        target.TopLeftThreshold = Decimal.ToInt32(topLeftThreshold.Value);
        target.MinLifetimeMs = Decimal.ToInt32(minimumLifetimeSeconds.Value * 1_000m);
        target.MaxAgeDays = Decimal.ToInt32(maximumAgeDays.Value);
        target.CascadeOffset = Decimal.ToInt32(cascadeOffset.Value);
        target.Rules = rules.Select(rule => rule.Copy()).ToList();
        target.Save();
        DialogResult = DialogResult.OK;
        Close();
    }

    private Button? FindButton(string text) =>
        Descendants(this).OfType<Button>().FirstOrDefault(button => button.Text == text);

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal increment) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Dock = DockStyle.Fill,
        ThousandsSeparator = true,
    };

    private static void AddNumberRow(
        TableLayoutPanel grid, int row, string label, Control input, string suffix)
    {
        grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        grid.Controls.Add(input, 1, row);
        grid.Controls.Add(new Label { Text = suffix, AutoSize = true, Anchor = AnchorStyles.Left }, 2, row);
    }
}
