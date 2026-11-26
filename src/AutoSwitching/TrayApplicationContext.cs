using AutoSwitching.Core;
using AutoSwitching.UI;

namespace AutoSwitching;

/// <summary>
/// 托盘常驻的应用上下文。
/// </summary>
internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ForegroundWatcher _watcher;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly SynchronizationContext? _syncContext;
    private SettingsForm? _settingsForm;
    private AppConfig _config;

    public TrayApplicationContext(bool openSettingsOnStart = false)
    {
        _syncContext = SynchronizationContext.Current;
        _config = ConfigStore.Load();

        // 首次运行时把默认配置落盘，方便用户直接编辑。
        if (!File.Exists(ConfigStore.ConfigPath))
        {
            try
            {
                ConfigStore.Save(_config);
            }
            catch
            {
                // 写不了就算了，不影响运行。
            }
        }

        _watcher = new ForegroundWatcher { Config = _config };
        _watcher.RuleApplied += OnRuleApplied;
        _watcher.Error += OnWatcherError;

        _enabledItem = new ToolStripMenuItem("启用自动切换")
        {
            Checked = _config.Enabled,
            CheckOnClick = true,
        };
        _enabledItem.Click += (_, _) => SetEnabled(_enabledItem.Checked);

        var menu = new ContextMenuStrip();
        menu.Items.Add(_enabledItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("设置…", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitApplication());

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Create(),
            Text = "自动输入法切换",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _notifyIcon.DoubleClick += (_, _) => ShowSettings();

        _watcher.Start();
        UpdateTooltip();

        if (openSettingsOnStart)
        {
            ShowSettings();
        }
    }

    private void SetEnabled(bool enabled)
    {
        _config.Enabled = enabled;
        _enabledItem.Checked = enabled;
        SaveConfig();
        UpdateTooltip();
    }

    private void ShowSettings()
    {
        if (_settingsForm is not null && !_settingsForm.IsDisposed)
        {
            _settingsForm.Activate();
            if (_settingsForm.WindowState == FormWindowState.Minimized)
            {
                _settingsForm.WindowState = FormWindowState.Normal;
            }

            return;
        }

        _settingsForm = new SettingsForm(_config.Clone());
        _settingsForm.ConfigSaved += OnConfigSaved;
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private void OnConfigSaved(AppConfig config)
    {
        // SettingsForm 已经写入磁盘，这里只同步内存状态。
        _config = config;
        _watcher.Config = config;
        _enabledItem.Checked = config.Enabled;
        UpdateTooltip();
    }

    private void SaveConfig()
    {
        try
        {
            ConfigStore.Save(_config);
        }
        catch (Exception ex)
        {
            _notifyIcon.ShowBalloonTip(
                3000,
                "保存配置失败",
                ex.Message,
                ToolTipIcon.Error);
        }
    }

    private void OnRuleApplied(string processName, ImeState state)
    {
        // 事件来自后台工作线程，切回 UI 线程再更新托盘。
        if (_notifyIcon is null)
        {
            return;
        }

        string label = state == ImeState.Chinese ? "中文" : "英文";
        string text = Truncate($"自动输入法切换 — {processName} → {label}", 63);

        if (_syncContext is not null)
        {
            _syncContext.Post(_ => _notifyIcon.Text = text, null);
        }
    }

    private void OnWatcherError(string message)
    {
        if (_syncContext is not null)
        {
            _syncContext.Post(
                _ => _notifyIcon.ShowBalloonTip(3000, "自动输入法切换", message, ToolTipIcon.Warning),
                null);
        }
    }

    private void UpdateTooltip()
    {
        string status = _config.Enabled ? "已启用" : "已暂停";
        _notifyIcon.Text = Truncate($"自动输入法切换 — {status}", 63);
    }

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..max];

    private void ExitApplication()
    {
        _watcher.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        ExitThread();
    }
}
