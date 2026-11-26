using AutoSwitching.Core;

namespace AutoSwitching.UI;

/// <summary>
/// 自检窗口：验证 IME 切换逻辑在本机是否可用。
/// 通过 --selftest 参数启动。
/// </summary>
internal sealed class SelfTestForm : Form
{
    private readonly TextBox _input = new();
    private readonly TextBox _log = new();

    public SelfTestForm()
    {
        Text = "自动输入法切换 — 自检";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 420);
        Font = new Font("Microsoft YaHei UI", 9f);

        var label = new Label
        {
            Text = "下面这个输入框用于测试。请先点进输入框，再点「开始自检」。",
            Location = new Point(12, 12),
            AutoSize = true,
        };

        _input.Location = new Point(12, 40);
        _input.Width = 580;
        _input.Font = new Font("Microsoft YaHei UI", 12f);

        var runButton = new Button
        {
            Text = "开始自检",
            Location = new Point(12, 78),
            Size = new Size(100, 30),
        };
        runButton.Click += (_, _) => RunTest();

        _log.Location = new Point(12, 118);
        _log.Multiline = true;
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.Size = new Size(580, 280);
        _log.Font = new Font("Consolas", 9f);

        Controls.AddRange(new Control[] { label, _input, runButton, _log });

        Shown += (_, _) =>
        {
            _input.Focus();

            // 带 --auto 参数时自动跑一遍并写结果文件，便于无人值守验证。
            if (Environment.GetCommandLineArgs().Any(a =>
                    string.Equals(a, "--auto", StringComparison.OrdinalIgnoreCase)))
            {
                var timer = new System.Windows.Forms.Timer { Interval = 1200 };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    timer.Dispose();
                    RunTest();
                    WriteResultFile();
                    Close();
                };
                timer.Start();
            }
        };
    }

    private void WriteResultFile()
    {
        try
        {
            string path = Path.Combine(Path.GetTempPath(), "autoswitching-selftest.txt");
            File.WriteAllText(path, _log.Text);
        }
        catch
        {
            // 忽略
        }
    }

    private void RunTest()
    {
        _log.Clear();
        _input.Focus();

        IntPtr hwnd = _input.Handle;
        Log($"输入框句柄: 0x{hwnd.ToInt64():X}");

        ImeState? initial = ImeController.TryGetState(hwnd);
        Log($"初始状态: {Describe(initial)}");

        // 切到中文
        bool okChinese = ImeController.TryApply(hwnd, ImeState.Chinese);
        Application.DoEvents();
        ImeState? afterChinese = ImeController.TryGetState(hwnd);
        Log($"切到中文: 调用={okChinese}, 读回={Describe(afterChinese)}  {(afterChinese == ImeState.Chinese ? "✓" : "✗")}");

        // 切到英文
        bool okEnglish = ImeController.TryApply(hwnd, ImeState.English);
        Application.DoEvents();
        ImeState? afterEnglish = ImeController.TryGetState(hwnd);
        Log($"切到英文: 调用={okEnglish}, 读回={Describe(afterEnglish)}  {(afterEnglish == ImeState.English ? "✓" : "✗")}");

        // 再切回中文，确认可反复切换
        ImeController.TryApply(hwnd, ImeState.Chinese);
        Application.DoEvents();
        ImeState? again = ImeController.TryGetState(hwnd);
        Log($"再切中文: 读回={Describe(again)}  {(again == ImeState.Chinese ? "✓" : "✗")}");

        Log(string.Empty);
        bool pass = afterChinese == ImeState.Chinese && afterEnglish == ImeState.English;
        Log(pass ? "结论：本机 IME 切换可用。" : "结论：切换未生效，请检查输入法是否为微软拼音/搜狗等标准 IME。");
    }

    private static string Describe(ImeState? state) => state switch
    {
        ImeState.Chinese => "中文",
        ImeState.English => "英文",
        _ => "未知",
    };

    private void Log(string text)
    {
        _log.AppendText(text + Environment.NewLine);
    }
}
