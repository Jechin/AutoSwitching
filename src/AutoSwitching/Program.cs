namespace AutoSwitching;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // 无界面模式：把「解析后的有效配置」写到文件，供自动化测试断言。
        //
        // 为什么需要它：程序启动时只在配置文件不存在时才写盘，已存在的文件不会被重写。
        // 因此「启动一次再看文件内容」无法验证解析逻辑——文件本来就不会变。
        // 这个模式直接输出 ConfigStore.Load() 的结果，能真正测出解析是否正确。
        //
        // 必须在单实例互斥体之前处理，否则已有实例在跑时会弹窗阻塞。
        // 用法：AutoSwitching.exe --dump-config <输出文件>
        int dumpIndex = Array.FindIndex(
            args,
            a => string.Equals(a, "--dump-config", StringComparison.OrdinalIgnoreCase));

        if (dumpIndex >= 0)
        {
            if (dumpIndex + 1 >= args.Length)
            {
                return;
            }

            Core.AppConfig config = Core.ConfigStore.Load();
            File.WriteAllText(
                args[dumpIndex + 1],
                Core.ConfigStore.Serialize(config),
                new System.Text.UTF8Encoding(false));
            return;
        }

        // .NET Framework 4.8 没有 ApplicationConfiguration.Initialize()，
        // 这里手动做等价设置。高 DPI 感知由 app.manifest 声明。
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 单实例运行，避免多个托盘图标互相抢输入法。
        using var mutex = new Mutex(true, @"Global\AutoSwitching.SingleInstance", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "自动输入法切换已经在运行了。\n请在系统托盘里找到它的图标。",
                "自动输入法切换",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        bool openSettings = args.Any(a =>
            string.Equals(a, "--settings", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(a, "-s", StringComparison.OrdinalIgnoreCase));

        bool selfTest = args.Any(a =>
            string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));

        if (selfTest)
        {
            Application.Run(new UI.SelfTestForm());
            return;
        }

        Application.Run(new TrayApplicationContext(openSettings));
    }
}
