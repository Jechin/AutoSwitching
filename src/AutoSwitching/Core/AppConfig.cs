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
    /// <summary>
    /// 覆盖配置目录的环境变量名。
    /// 测试脚本用它把配置重定向到临时目录，从而完全不触碰用户的真实配置。
    /// </summary>
    public const string ConfigDirectoryOverrideVariable = "AUTOSWITCHING_CONFIG_DIR";

    /// <summary>配置文件所在目录。</summary>
    public static string ConfigDirectory { get; } = ResolveConfigDirectory();

    /// <summary>配置文件完整路径。</summary>
    public static string ConfigPath { get; } = Path.Combine(ConfigDirectory, "config.json");

    private static string ResolveConfigDirectory()
    {
        // 允许通过环境变量重定向，供自动化测试使用。
        // 这样测试永远不需要备份/还原真实配置，也就不存在「中断后配置丢失」的问题。
        string? overridden = Environment.GetEnvironmentVariable(ConfigDirectoryOverrideVariable);
        if (!string.IsNullOrWhiteSpace(overridden))
        {
            return overridden.Trim();
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "AutoSwitching");
    }

    public static AppConfig Load()
    {
        try
        {
            if (File.Exists(ConfigPath))
            {
                string json = File.ReadAllText(ConfigPath);
                AppConfig? config = Deserialize(json);
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
        string json = Serialize(config);

        // 先写临时文件再替换，避免写入中断导致配置损坏。
        string tempPath = ConfigPath + ".tmp";
        File.WriteAllText(tempPath, json, new System.Text.UTF8Encoding(false));

        if (File.Exists(ConfigPath))
        {
            File.Replace(tempPath, ConfigPath, null);
        }
        else
        {
            File.Move(tempPath, ConfigPath);
        }
    }

    /// <summary>把配置序列化为 JSON 文本。</summary>
    internal static string Serialize(AppConfig config)
    {
        var w = new JsonWriter();
        w.BeginObject();

        w.Name("enabled");
        w.Value(config.Enabled);

        w.Name("defaultState");
        if (config.DefaultState is null)
        {
            w.Null();
        }
        else
        {
            w.Value(config.DefaultState.Value.ToString());
        }

        w.Name("reapplyDelayMs");
        w.Value(config.ReapplyDelayMs);

        w.Name("rules");
        w.BeginArray();
        foreach (AppRule rule in config.Rules)
        {
            w.BeginObject();
            w.Name("processName");
            w.Value(rule.ProcessName);
            w.Name("state");
            w.Value(rule.State.ToString());
            w.Name("enabled");
            w.Value(rule.Enabled);
            w.Name("note");
            w.Value(rule.Note);
            w.EndObject();
        }

        w.EndArray();
        w.EndObject();

        return w.ToString();
    }

    /// <summary>从 JSON 文本还原配置，格式不合法时返回 null。</summary>
    internal static AppConfig? Deserialize(string json)
    {
        Dictionary<string, object?>? root;
        try
        {
            root = JsonReader.Parse(json) as Dictionary<string, object?>;
        }
        catch (FormatException)
        {
            // 配置被手工改坏时不应让程序崩溃，交给调用方回退到默认配置。
            return null;
        }

        if (root is null)
        {
            return null;
        }

        var config = new AppConfig
        {
            Enabled = ReadBool(root, "enabled", true),
            DefaultState = ReadState(root, "defaultState"),
            ReapplyDelayMs = ReadInt(root, "reapplyDelayMs", 120),
        };

        if (root.TryGetValue("rules", out object? rulesValue) && rulesValue is List<object?> rules)
        {
            foreach (object? item in rules)
            {
                if (item is not Dictionary<string, object?> ruleObj)
                {
                    continue;
                }

                config.Rules.Add(new AppRule
                {
                    ProcessName = ReadString(ruleObj, "processName"),
                    State = ReadState(ruleObj, "state") ?? ImeState.English,
                    Enabled = ReadBool(ruleObj, "enabled", true),
                    Note = ReadString(ruleObj, "note"),
                });
            }
        }

        return config;
    }

    private static string ReadString(Dictionary<string, object?> obj, string name) =>
        obj.TryGetValue(name, out object? value) && value is string s ? s : string.Empty;

    private static bool ReadBool(Dictionary<string, object?> obj, string name, bool fallback) =>
        obj.TryGetValue(name, out object? value) && value is bool b ? b : fallback;

    private static int ReadInt(Dictionary<string, object?> obj, string name, int fallback) =>
        obj.TryGetValue(name, out object? value) && value is double d ? (int)d : fallback;

    private static ImeState? ReadState(Dictionary<string, object?> obj, string name)
    {
        if (!obj.TryGetValue(name, out object? value))
        {
            return null;
        }

        // 兼容三种写法：枚举名（"Chinese"/"English"）、数字（0/1）、以及 JSON 数字。
        if (value is double d)
        {
            return d == 0 ? ImeState.Chinese : d == 1 ? ImeState.English : null;
        }

        if (value is not string s)
        {
            return null;
        }

        if (string.Equals(s, nameof(ImeState.Chinese), StringComparison.OrdinalIgnoreCase) || s == "0")
        {
            return ImeState.Chinese;
        }

        if (string.Equals(s, nameof(ImeState.English), StringComparison.OrdinalIgnoreCase) || s == "1")
        {
            return ImeState.English;
        }

        return null;
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
