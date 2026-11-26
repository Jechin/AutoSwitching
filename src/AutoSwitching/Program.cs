namespace AutoSwitching;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

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
