using AutoSwitching.Core;

namespace AutoSwitching.UI;

/// <summary>
/// 设置窗口：管理规则列表与全局选项。
/// </summary>
public sealed class SettingsForm : Form
{
    private readonly AppConfig _config;

    private readonly CheckBox _enabledCheck = new();
    private readonly ComboBox _defaultStateCombo = new();
    private readonly NumericUpDown _delayInput = new();
    private readonly ListView _ruleList = new();
    private readonly TextBox _processInput = new();
    private readonly ComboBox _stateCombo = new();
    private readonly TextBox _noteInput = new();
    private readonly CheckBox _ruleEnabledCheck = new();
    private readonly Button _addButton = new();
    private readonly Button _updateButton = new();
    private readonly Button _removeButton = new();
    private readonly Button _pickWindowButton = new();
    private readonly Button _saveButton = new();
    private readonly Button _cancelButton = new();
    private readonly Label _statusLabel = new();

    private int _editingIndex = -1;

    /// <summary>用户点击保存后触发，参数为新的配置。</summary>
    public event Action<AppConfig>? ConfigSaved;

    public SettingsForm(AppConfig config)
    {
        _config = config;
        BuildUi();
        LoadConfigIntoUi();
    }

    private void BuildUi()
    {
        Text = "自动输入法切换 — 设置";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(760, 580);
        Font = new Font("Microsoft YaHei UI", 9f);

        // ---------- 全局选项 ----------
        var globalGroup = new GroupBox
        {
            Text = "全局选项",
            Location = new Point(12, 12),
            Size = new Size(736, 96),
        };

        _enabledCheck.Text = "启用自动切换";
        _enabledCheck.Location = new Point(16, 26);
        _enabledCheck.AutoSize = true;

        var defaultLabel = new Label
        {
            Text = "未匹配规则时：",
            Location = new Point(16, 60),
            AutoSize = true,
        };

        _defaultStateCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _defaultStateCombo.Location = new Point(120, 56);
        _defaultStateCombo.Width = 150;
        _defaultStateCombo.Items.AddRange(new object[]
        {
            "不干预（保持原样）",
            "切换为中文",
            "切换为英文",
        });

        var delayLabel = new Label
        {
            Text = "延迟补切：",
            Location = new Point(300, 60),
            AutoSize = true,
        };

        _delayInput.Location = new Point(380, 56);
        _delayInput.Width = 70;
        _delayInput.Minimum = 0;
        _delayInput.Maximum = 2000;
        _delayInput.Increment = 20;

        var delayUnit = new Label
        {
            Text = "毫秒（0 = 不补切）",
            Location = new Point(456, 60),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };

        globalGroup.Controls.AddRange(new Control[]
        {
            _enabledCheck, defaultLabel, _defaultStateCombo, delayLabel, _delayInput, delayUnit,
        });

        // ---------- 规则列表 ----------
        var ruleGroup = new GroupBox
        {
            Text = "程序规则",
            Location = new Point(12, 118),
            Size = new Size(736, 340),
        };

        _ruleList.View = View.Details;
        _ruleList.FullRowSelect = true;
        _ruleList.GridLines = true;
        _ruleList.MultiSelect = false;
        _ruleList.HideSelection = false;
        _ruleList.Location = new Point(16, 24);
        _ruleList.Size = new Size(704, 200);
        _ruleList.Columns.Add("程序", 200);
        _ruleList.Columns.Add("输入法", 90);
        _ruleList.Columns.Add("启用", 60);
        _ruleList.Columns.Add("备注", 330);
        _ruleList.SelectedIndexChanged += OnRuleSelectionChanged;

        // ---------- 编辑区 ----------
        var processLabel = new Label
        {
            Text = "程序名：",
            Location = new Point(16, 240),
            AutoSize = true,
        };

        _processInput.Location = new Point(80, 236);
        _processInput.Width = 180;

        // .NET Framework 4.8 的 TextBox 没有 PlaceholderText，用灰色提示文字模拟。
        SetPlaceholder(_processInput, "例如 chrome.exe");

        _pickWindowButton.Text = "取当前窗口";
        _pickWindowButton.Location = new Point(268, 234);
        _pickWindowButton.Size = new Size(96, 26);
        _pickWindowButton.Click += OnPickWindowClick;

        var stateLabel = new Label
        {
            Text = "输入法：",
            Location = new Point(376, 240),
            AutoSize = true,
        };

        _stateCombo.DropDownStyle = ComboBoxStyle.DropDownList;
        _stateCombo.Location = new Point(436, 236);
        _stateCombo.Width = 90;
        _stateCombo.Items.AddRange(new object[] { "中文", "英文" });
        _stateCombo.SelectedIndex = 1;

        var noteLabel = new Label
        {
            Text = "备注：",
            Location = new Point(540, 240),
            AutoSize = true,
        };

        _noteInput.Location = new Point(584, 236);
        _noteInput.Width = 136;

        _ruleEnabledCheck.Text = "启用此规则";
        _ruleEnabledCheck.Location = new Point(16, 276);
        _ruleEnabledCheck.AutoSize = true;
        _ruleEnabledCheck.Checked = true;

        _addButton.Text = "添加";
        _addButton.Location = new Point(120, 272);
        _addButton.Size = new Size(80, 28);
        _addButton.Click += OnAddClick;

        _updateButton.Text = "更新选中";
        _updateButton.Location = new Point(208, 272);
        _updateButton.Size = new Size(96, 28);
        _updateButton.Enabled = false;
        _updateButton.Click += OnUpdateClick;

        _removeButton.Text = "删除选中";
        _removeButton.Location = new Point(312, 272);
        _removeButton.Size = new Size(96, 28);
        _removeButton.Enabled = false;
        _removeButton.Click += OnRemoveClick;

        var hintLabel = new Label
        {
            Text = "提示：程序名不区分大小写，可省略 .exe。",
            Location = new Point(424, 278),
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
        };

        ruleGroup.Controls.AddRange(new Control[]
        {
            _ruleList, processLabel, _processInput, _pickWindowButton,
            stateLabel, _stateCombo, noteLabel, _noteInput,
            _ruleEnabledCheck, _addButton, _updateButton, _removeButton, hintLabel,
        });

        // ---------- 底部 ----------
        _statusLabel.Location = new Point(16, 470);
        _statusLabel.Size = new Size(500, 20);
        _statusLabel.ForeColor = SystemColors.GrayText;

        _saveButton.Text = "保存";
        _saveButton.Location = new Point(556, 500);
        _saveButton.Size = new Size(90, 32);
        _saveButton.Click += OnSaveClick;

        _cancelButton.Text = "取消";
        _cancelButton.Location = new Point(656, 500);
        _cancelButton.Size = new Size(90, 32);
        _cancelButton.Click += (_, _) => Close();

        AcceptButton = _saveButton;
        CancelButton = _cancelButton;

        Controls.AddRange(new Control[]
        {
            globalGroup, ruleGroup, _statusLabel, _saveButton, _cancelButton,
        });
    }

    private void LoadConfigIntoUi()
    {
        _enabledCheck.Checked = _config.Enabled;
        _defaultStateCombo.SelectedIndex = _config.DefaultState switch
        {
            ImeState.Chinese => 1,
            ImeState.English => 2,
            _ => 0,
        };
        _delayInput.Value = Clamp(_config.ReapplyDelayMs, 0, 2000);

        RefreshRuleList();
    }

    /// <summary>.NET Framework 4.8 没有 Math.Clamp。</summary>
    private static int Clamp(int value, int min, int max) =>
        value < min ? min : value > max ? max : value;

    /// <summary>
    /// 用覆盖在 TextBox 上的灰色 Label 模拟 PlaceholderText
    /// （.NET Framework 4.8 的 TextBox 没有该属性）。
    /// 这样不会污染 TextBox.Text，读取时无需特殊处理。
    /// </summary>
    private static void SetPlaceholder(TextBox box, string placeholder)
    {
        var hint = new Label
        {
            Text = placeholder,
            ForeColor = SystemColors.GrayText,
            BackColor = SystemColors.Window,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(box.Left + 2, box.Top + 1),
            Size = new Size(box.Width - 4, box.Height - 2),
            Cursor = Cursors.IBeam,
        };

        void Sync() => hint.Visible = box.Text.Length == 0 && box.Visible;

        box.TextChanged += (_, _) => Sync();
        box.GotFocus += (_, _) => Sync();
        box.LostFocus += (_, _) => Sync();

        // 点击提示文字时把焦点交给输入框。
        hint.Click += (_, _) => box.Focus();

        // 调用时 TextBox 可能还没加入父容器，等它有了父容器再挂上去。
        void Attach()
        {
            if (box.Parent is null || hint.Parent is not null)
            {
                return;
            }

            box.Parent.Controls.Add(hint);
            hint.BringToFront();
            Sync();
        }

        box.ParentChanged += (_, _) => Attach();
        Attach();
    }

    private void RefreshRuleList()
    {
        _ruleList.BeginUpdate();
        _ruleList.Items.Clear();

        foreach (AppRule rule in _config.Rules)
        {
            var item = new ListViewItem(rule.ProcessName)
            {
                Tag = rule,
            };
            item.SubItems.Add(rule.State == ImeState.Chinese ? "中文" : "英文");
            item.SubItems.Add(rule.Enabled ? "是" : "否");
            item.SubItems.Add(rule.Note);
            _ruleList.Items.Add(item);
        }

        _ruleList.EndUpdate();
        UpdateButtonStates();
    }

    private void OnRuleSelectionChanged(object? sender, EventArgs e)
    {
        if (_ruleList.SelectedItems.Count == 0)
        {
            _editingIndex = -1;
            UpdateButtonStates();
            return;
        }

        _editingIndex = _ruleList.SelectedItems[0].Index;
        AppRule rule = _config.Rules[_editingIndex];

        _processInput.Text = rule.ProcessName;
        _stateCombo.SelectedIndex = rule.State == ImeState.Chinese ? 0 : 1;
        _noteInput.Text = rule.Note;
        _ruleEnabledCheck.Checked = rule.Enabled;

        UpdateButtonStates();
    }

    private void UpdateButtonStates()
    {
        bool hasSelection = _ruleList.SelectedItems.Count > 0;
        _updateButton.Enabled = hasSelection;
        _removeButton.Enabled = hasSelection;
    }

    private void OnPickWindowClick(object? sender, EventArgs e)
    {
        // 隐藏自己，否则前台窗口永远是本窗口。
        _statusLabel.Text = "请在 3 秒内切换到目标程序窗口…";
        _statusLabel.ForeColor = Color.DarkOrange;
        Hide();

        var timer = new System.Windows.Forms.Timer { Interval = 3000 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();

            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            string? name = ForegroundWatcher.GetProcessName(hwnd);

            Show();
            Activate();

            if (string.IsNullOrEmpty(name))
            {
                _statusLabel.Text = "没能识别到窗口所属程序。";
                _statusLabel.ForeColor = Color.Firebrick;
                return;
            }

            if (string.Equals(name, "AutoSwitching.exe", StringComparison.OrdinalIgnoreCase))
            {
                _statusLabel.Text = "取到的是本程序自己，请切换到别的窗口再试。";
                _statusLabel.ForeColor = Color.Firebrick;
                return;
            }

            _processInput.Text = name;
            _statusLabel.Text = $"已获取：{name}";
            _statusLabel.ForeColor = Color.ForestGreen;
        };
        timer.Start();
    }

    private void OnAddClick(object? sender, EventArgs e)
    {
        if (!TryReadEditor(out AppRule rule))
        {
            return;
        }

        if (_config.Rules.Any(r =>
                string.Equals(r.ProcessName, rule.ProcessName, StringComparison.OrdinalIgnoreCase)))
        {
            _statusLabel.Text = $"规则里已经有 {rule.ProcessName} 了，请直接更新它。";
            _statusLabel.ForeColor = Color.Firebrick;
            return;
        }

        _config.Rules.Add(rule);
        RefreshRuleList();
        ClearEditor();
        _statusLabel.Text = $"已添加 {rule.ProcessName}。";
        _statusLabel.ForeColor = Color.ForestGreen;
    }

    private void OnUpdateClick(object? sender, EventArgs e)
    {
        if (_editingIndex < 0 || _editingIndex >= _config.Rules.Count)
        {
            return;
        }

        if (!TryReadEditor(out AppRule rule))
        {
            return;
        }

        AppRule existing = _config.Rules[_editingIndex];
        existing.ProcessName = rule.ProcessName;
        existing.State = rule.State;
        existing.Note = rule.Note;
        existing.Enabled = rule.Enabled;

        RefreshRuleList();
        _statusLabel.Text = $"已更新 {rule.ProcessName}。";
        _statusLabel.ForeColor = Color.ForestGreen;
    }

    private void OnRemoveClick(object? sender, EventArgs e)
    {
        if (_editingIndex < 0 || _editingIndex >= _config.Rules.Count)
        {
            return;
        }

        string name = _config.Rules[_editingIndex].ProcessName;
        _config.Rules.RemoveAt(_editingIndex);
        _editingIndex = -1;
        RefreshRuleList();
        ClearEditor();
        _statusLabel.Text = $"已删除 {name}。";
        _statusLabel.ForeColor = Color.ForestGreen;
    }

    private bool TryReadEditor(out AppRule rule)
    {
        rule = new AppRule();

        string processName = _processInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(processName))
        {
            _statusLabel.Text = "请先填写程序名，例如 chrome.exe。";
            _statusLabel.ForeColor = Color.Firebrick;
            _processInput.Focus();
            return false;
        }

        if (!processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            processName += ".exe";
        }

        rule.ProcessName = processName;
        rule.State = _stateCombo.SelectedIndex == 0 ? ImeState.Chinese : ImeState.English;
        rule.Note = _noteInput.Text.Trim();
        rule.Enabled = _ruleEnabledCheck.Checked;
        return true;
    }

    private void ClearEditor()
    {
        _processInput.Clear();
        _noteInput.Clear();
        _stateCombo.SelectedIndex = 1;
        _ruleEnabledCheck.Checked = true;
        _ruleList.SelectedItems.Clear();
        _editingIndex = -1;
        UpdateButtonStates();
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        _config.Enabled = _enabledCheck.Checked;
        _config.DefaultState = _defaultStateCombo.SelectedIndex switch
        {
            1 => ImeState.Chinese,
            2 => ImeState.English,
            _ => null,
        };
        _config.ReapplyDelayMs = (int)_delayInput.Value;

        try
        {
            ConfigStore.Save(_config);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                this,
                "保存配置失败：" + ex.Message,
                "自动输入法切换",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return;
        }

        ConfigSaved?.Invoke(_config);
        Close();
    }
}
