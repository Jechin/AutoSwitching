using System.Text.Json;
using System.Text.Json.Serialization;

namespace AutoSwitching.Core;

/// <summary>一条程序规则。</summary>
public sealed class AppRule
{
    /// <summary>进程名，例如 chrome.exe。比较时不区分大小写。</summary>
    public string ProcessName { get; set; } = string.Empty;

    /// <summary>该程序激活时希望使用的输入法状态。</summary>
    public ImeState State { get; set; } = ImeState.English;

    /// <summary>是否启用这条规则。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>备注，仅用于界面显示。</summary>
    public string Note { get; set; } = string.Empty;

    public AppRule Clone() => new()
    {
        ProcessName = ProcessName,
        State = State,
        Enabled = Enabled,
        Note = Note,
    };
}

/// <summary>应用配置。</summary>
public sealed class AppConfig
{
    /// <summary>总开关。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>没有匹配到规则时使用的状态；为 null 表示不干预。</summary>
    public ImeState? DefaultState { get; set; } = null;

    /// <summary>切换后延迟校验的毫秒数，用于应对程序自身重置输入法。</summary>
    public int ReapplyDelayMs { get; set; } = 120;

    /// <summary>规则列表。</summary>
    public List<AppRule> Rules { get; set; } = new();

    /// <summary>按进程名查找规则（不区分大小写）。</summary>
    public AppRule? FindRule(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        foreach (AppRule rule in Rules)
        {
            if (!rule.Enabled || string.IsNullOrWhiteSpace(rule.ProcessName))
            {
                continue;
            }

            if (string.Equals(rule.ProcessName.Trim(), processName.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return rule;
            }
        }

        return null;
    }

    /// <summary>深拷贝，供设置窗口编辑时使用，取消后不影响运行中的配置。</summary>
    public AppConfig Clone() => new()
    {
        Enabled = Enabled,
        DefaultState = DefaultState,
        ReapplyDelayMs = ReapplyDelayMs,
        Rules = Rules.Select(r => r.Clone()).ToList(),
    };
}

/// <summary>配置读写。</summary>
public static class ConfigStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>配置文件所在目录。</summary>
    public static string ConfigDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "AutoSwitching");

    /// <summary>配置文件完整路径。</summary>
    public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, "config.json");

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                AppConfig? config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                if (config is not null)
                {
                    config.Rules ??= new List<AppRule>();
                    return config;
                }
            }
        }
        catch
        {
            // 配置损坏时回退到默认配置，避免程序无法启动。
        }

        return CreateDefault();
    }

    public static void Save(AppConfig config)
    {
        Directory.CreateDirectory(ConfigDirectory);
        string json = JsonSerializer.Serialize(config, JsonOptions);

        // 先写临时文件再替换，避免写入中断导致配置损坏。
        string tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json);
        File.Move(tempPath, ConfigPath, overwrite: true);
    }

    private static AppConfig CreateDefault() => new()
    {
        Enabled = true,
        DefaultState = null,
        ReapplyDelayMs = 120,
        Rules = new List<AppRule>
        {
            new() { ProcessName = "Code.exe", State = ImeState.English, Note = "VS Code 默认英文" },
            new() { ProcessName = "WindowsTerminal.exe", State = ImeState.English, Note = "终端默认英文" },
            new() { ProcessName = "WeChat.exe", State = ImeState.Chinese, Note = "微信默认中文" },
        },
    };
}
