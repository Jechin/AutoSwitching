using System.Runtime.InteropServices;

namespace TestTarget;

/// <summary>
/// 测试目标程序：一个带文本框的窗口，用于验证跨进程 IME 切换。
/// 用法：TestTarget.exe [--report 输出文件]
/// </summary>
internal static class Program
{
    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetContext(IntPtr hWnd);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmReleaseContext(IntPtr hWnd, IntPtr hIMC);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmGetConversionStatus(IntPtr hIMC, out int conv, out int sent);

    [DllImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ImmGetOpenStatus(IntPtr hIMC);

    [DllImport("imm32.dll")]
    private static extern IntPtr ImmGetDefaultIMEWnd(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetGUIThreadInfo(uint idThread, ref GUITHREADINFO gti);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(
        IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeout, out IntPtr result);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GUITHREADINFO
    {
        public int cbSize;
        public int flags;
        public IntPtr hwndActive;
        public IntPtr hwndFocus;
        public IntPtr hwndCapture;
        public IntPtr hwndMenuOwner;
        public IntPtr hwndMoveSize;
        public IntPtr hwndCaret;
        public RECT rcCaret;
    }

    private const int WM_IME_CONTROL = 0x0283;
    private const int IMC_GETOPENSTATUS = 0x0005;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        string? reportPath = null;
        int reportIndex = Array.IndexOf(args, "--report");
        if (reportIndex >= 0 && reportIndex + 1 < args.Length)
        {
            reportPath = args[reportIndex + 1];
        }

        var form = new Form
        {
            Text = "AutoSwitchingTestTarget",
            ClientSize = new Size(520, 220),
            StartPosition = FormStartPosition.CenterScreen,
        };

        var box = new TextBox
        {
            Location = new Point(20, 40),
            Width = 460,
            Font = new Font("Microsoft YaHei UI", 14f),
        };

        var label = new Label
        {
            Text = "IME test target",
            Location = new Point(20, 100),
            AutoSize = true,
        };

        form.Controls.Add(box);
        form.Controls.Add(label);

        form.Shown += (_, _) => box.Focus();

        // 定时把当前 IME 状态写到报告文件，供外部验证。
        if (reportPath is not null)
        {
            var timer = new System.Windows.Forms.Timer { Interval = 300 };
            timer.Tick += (_, _) =>
            {
                try
                {
                    IntPtr hwnd = box.Handle;

                    // 读数 1：本进程直接读 IME 上下文。
                    string ctxState = "NOCTX";
                    IntPtr hImc = ImmGetContext(hwnd);
                    if (hImc != IntPtr.Zero)
                    {
                        ImmGetConversionStatus(hImc, out int conv, out _);
                        bool open = ImmGetOpenStatus(hImc);
                        ImmReleaseContext(hwnd, hImc);
                        ctxState = $"{(open ? "CN" : "EN")}(open={open},conv=0x{conv:X})";
                    }

                    // 读数 2：向默认 IME 窗口查询（与 AutoSwitching 相同方式）。
                    string msgState = "NOMSG";
                    IntPtr imeWnd = ImmGetDefaultIMEWnd(hwnd);
                    if (imeWnd != IntPtr.Zero)
                    {
                        IntPtr sent = SendMessageTimeout(
                            imeWnd, WM_IME_CONTROL, (IntPtr)IMC_GETOPENSTATUS, IntPtr.Zero,
                            0x0002 | 0x0001, 300, out IntPtr result);
                        if (sent != IntPtr.Zero)
                        {
                            msgState = result != IntPtr.Zero ? "CN" : "EN";
                        }
                    }

                    // 焦点信息
                    GetWindowThreadProcessId(hwnd, out uint tid);
                    var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf<GUITHREADINFO>() };
                    GetGUIThreadInfo(tid, ref gti);
                    bool isFg = GetForegroundWindow() == form.Handle;
                    bool boxFocused = gti.hwndFocus == hwnd;

                    File.WriteAllText(
                        reportPath,
                        $"ctx={ctxState} msg={msgState} fg={isFg} boxFocus={boxFocused}");
                }
                catch
                {
                    // 忽略
                }
            };
            timer.Start();
        }

        Application.Run(form);
    }
}
