using System.Collections.Concurrent;
using System.Diagnostics;

namespace AutoSwitching.Core;

/// <summary>
/// 监听前台窗口变化，并按规则自动切换输入法。
/// </summary>
/// <remarks>
/// WinEvent 回调在安装钩子的线程（UI 线程）上执行，而 IME 操作可能涉及跨进程调用。
/// 因此这里把实际的切换动作丢到一条专用后台线程串行执行，保证 UI 永不卡顿。
/// </remarks>
public sealed class ForegroundWatcher : IDisposable
{
    private readonly NativeMethods.WinEventDelegate _callback;
    private readonly BlockingCollection<WorkItem> _queue = new();
    private readonly Thread _worker;
    private readonly CancellationTokenSource _cts = new();

    private IntPtr _hook;
    private System.Windows.Forms.Timer? _reapplyTimer;
    private IntPtr _pendingHwnd;
    private ImeState _pendingState;
    private volatile bool _disposed;

    private readonly record struct WorkItem(IntPtr Hwnd, string ProcessName, ImeState State, bool Notify);

    /// <summary>每次成功应用规则后触发，参数为进程名与目标状态。</summary>
    public event Action<string, ImeState>? RuleApplied;

    /// <summary>发生错误时触发。</summary>
    public event Action<string>? Error;

    public ForegroundWatcher()
    {
        // 必须保存委托引用，否则会被 GC 回收导致回调崩溃。
        _callback = OnWinEvent;

        _worker = new Thread(WorkerLoop)
        {
            IsBackground = true,
            Name = "AutoSwitching.ImeWorker",
        };
        _worker.Start();
    }

    /// <summary>当前配置，由外部更新。</summary>
    public AppConfig Config { get; set; } = new();

    /// <summary>是否暂停（例如用户正在编辑规则）。</summary>
    public bool Paused { get; set; }

    /// <summary>最近一次激活的非本程序进程名，供界面"从当前窗口添加"使用。</summary>
    public string? LastExternalProcessName { get; private set; }

    public void Start()
    {
        if (_hook != IntPtr.Zero)
        {
            return;
        }

        _hook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _callback,
            0,
            0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);

        if (_hook == IntPtr.Zero)
        {
            Error?.Invoke("无法安装前台窗口监听钩子。");
            return;
        }

        _reapplyTimer = new System.Windows.Forms.Timer { Interval = 120 };
        _reapplyTimer.Tick += OnReapplyTick;

        // 启动时先处理一次当前窗口。
        HandleForeground(NativeMethods.GetForegroundWindow());
    }

    public void Stop()
    {
        if (_hook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
        }

        if (_reapplyTimer is not null)
        {
            _reapplyTimer.Stop();
            _reapplyTimer.Dispose();
            _reapplyTimer = null;
        }
    }

    private void OnWinEvent(
        IntPtr hWinEventHook,
        uint eventType,
        IntPtr hwnd,
        int idObject,
        int idChild,
        uint dwEventThread,
        uint dwmsEventTime)
    {
        if (eventType != NativeMethods.EVENT_SYSTEM_FOREGROUND)
        {
            return;
        }

        HandleForeground(hwnd);
    }

    private void HandleForeground(IntPtr hwnd)
    {
        if (_disposed)
        {
            return;
        }

        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            DebugLog.Write($"HandleForeground: invalid hwnd 0x{hwnd.ToInt64():X}");
            return;
        }

        string? processName = GetProcessName(hwnd);
        if (string.IsNullOrEmpty(processName))
        {
            DebugLog.Write($"HandleForeground: no process name for 0x{hwnd.ToInt64():X}");
            return;
        }

        // 忽略本程序自身。
        if (string.Equals(processName, GetOwnProcessName(), StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // 记录最近的外部窗口，即使暂停时也记录，方便界面添加规则。
        LastExternalProcessName = processName;

        if (Paused || !Config.Enabled)
        {
            DebugLog.Write($"HandleForeground: paused={Paused} enabled={Config.Enabled}");
            return;
        }

        AppRule? rule = Config.FindRule(processName);
        ImeState? target = rule?.State ?? Config.DefaultState;

        DebugLog.Write(
            $"HandleForeground: {processName} rule={(rule is null ? "none" : rule.State.ToString())} target={target?.ToString() ?? "none"}");

        if (target is null)
        {
            return;
        }

        Enqueue(hwnd, processName, target.Value, notify: true);

        // 有些程序在激活后会重置输入法，延迟再补一次。
        int delay = Math.Clamp(Config.ReapplyDelayMs, 0, 2000);
        if (delay > 0 && _reapplyTimer is not null)
        {
            _pendingHwnd = hwnd;
            _pendingState = target.Value;
            _reapplyTimer.Interval = delay;
            _reapplyTimer.Stop();
            _reapplyTimer.Start();
        }
    }

    private void OnReapplyTick(object? sender, EventArgs e)
    {
        _reapplyTimer?.Stop();

        if (_disposed || Paused || !Config.Enabled)
        {
            return;
        }

        IntPtr hwnd = _pendingHwnd;
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return;
        }

        // 只有当它仍然是前台窗口时才补一次，避免误改别的窗口。
        if (NativeMethods.GetForegroundWindow() != hwnd)
        {
            return;
        }

        string? processName = GetProcessName(hwnd);
        if (string.IsNullOrEmpty(processName))
        {
            return;
        }

        Enqueue(hwnd, processName, _pendingState, notify: false);
    }

    private void Enqueue(IntPtr hwnd, string processName, ImeState state, bool notify)
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            _queue.Add(new WorkItem(hwnd, processName, state, notify));
        }
        catch (InvalidOperationException)
        {
            // 队列已关闭，忽略。
        }
    }

    private void WorkerLoop()
    {
        try
        {
            foreach (WorkItem item in _queue.GetConsumingEnumerable(_cts.Token))
            {
                if (_disposed)
                {
                    continue;
                }

                bool ok = ImeController.ApplyWithVerification(item.Hwnd, item.State);
                DebugLog.Write($"Worker: apply {item.ProcessName} -> {item.State} ok={ok}");
                if (ok && item.Notify)
                {
                    RuleApplied?.Invoke(item.ProcessName, item.State);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常退出。
        }
        catch (Exception ex)
        {
            Error?.Invoke("切换输入法时出错：" + ex.Message);
        }
    }

    /// <summary>获取窗口所属进程名（含 .exe）。</summary>
    public static string? GetProcessName(IntPtr hwnd)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0)
            {
                return null;
            }

            using Process process = Process.GetProcessById((int)pid);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return null;
        }
    }

    private static string GetOwnProcessName()
    {
        try
        {
            using Process process = Process.GetCurrentProcess();
            return process.ProcessName + ".exe";
        }
        catch
        {
            return "AutoSwitching.exe";
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();

        _queue.CompleteAdding();
        _cts.Cancel();

        // 给工作线程一点时间收尾，但不无限等待。
        _worker.Join(TimeSpan.FromSeconds(1));

        _cts.Dispose();
        _queue.Dispose();
    }
}
