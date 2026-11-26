using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;

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
        // .NET Framework 4.8 没有 ApplicationConfiguration.Initialize()。
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

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

        // 只做一次聚焦，不要反复 Activate/Select。
        // 反复激活会让 Windows 重置该窗口的 IME 状态，导致测出的结果不稳定。
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

                    // .NET Framework 4.8 没有 Marshal.SizeOf<T>()，用非泛型重载。
                    var gti = new GUITHREADINFO { cbSize = Marshal.SizeOf(typeof(GUITHREADINFO)) };
                    GetGUIThreadInfo(tid, ref gti);
                    bool isFg = GetForegroundWindow() == form.Handle;
                    bool boxFocused = gti.hwndFocus == hwnd;

                    // 诊断用：把焦点窗口、活动窗口、文本框、窗体、IME 窗口的句柄都写出来，
                    // 便于判断 IME 状态到底挂在哪个窗口上。
                    // 带上时间戳，便于判断报告是否还在被刷新（定时器是否已停止）。
                    File.WriteAllText(
                        reportPath,
                        $"[{DateTime.Now:HH:mm:ss.fff}] " +
                        $"ctx={ctxState} msg={msgState} fg={isFg} boxFocus={boxFocused} " +
                        $"focus=0x{gti.hwndFocus.ToInt64():X} active=0x{gti.hwndActive.ToInt64():X} " +
                        $"box=0x{hwnd.ToInt64():X} form=0x{form.Handle.ToInt64():X} " +
                        $"ime=0x{imeWnd.ToInt64():X} boxFocused={box.Focused} " +
                        $"formIme=0x{ImmGetDefaultIMEWnd(form.Handle).ToInt64():X}");
                }
                catch (Exception ex)
                {
                    // 定时器里抛异常会让报告停止刷新，导致外部读到过期数据。
                    // 把异常写进报告，避免静默失败。
                    try
                    {
                        File.WriteAllText(
                            reportPath,
                            $"[{DateTime.Now:HH:mm:ss.fff}] TIMER-ERROR {ex.GetType().Name}: {ex.Message}");
                    }
                    catch
                    {
                        // 连报告都写不了，只能放弃。
                    }
                }
            };
            timer.Start();
        }

        Application.Run(form);
    }
}
