using System.Runtime.InteropServices;

namespace AutoSwitching.Core;

/// <summary>输入法状态。</summary>
public enum ImeState
{
    /// <summary>中文输入（输入法打开）。</summary>
    Chinese = 0,

    /// <summary>英文输入（输入法关闭）。</summary>
    English = 1,
}

/// <summary>
/// 负责把指定窗口的输入法切换到目标状态。
/// </summary>
/// <remarks>
/// 实现要点（经实测验证）：
///
/// 1. <c>ImmGetContext</c> 只能获取**调用线程**的输入上下文，跨进程调用返回 0。
/// 2. <c>AttachThreadInput</c> 在跨进程场景下常返回失败（ERROR_INVALID_PARAMETER），
///    且目标线程卡死时会拖住调用方，因此不采用。
/// 3. 可靠方案是向目标窗口的**默认 IME 窗口**发送
///    <c>WM_IME_CONTROL / IMC_SETOPENSTATUS</c>，这是 Windows 官方支持的跨进程控制方式。
/// 4. 中/英状态以 IME 的**打开状态**（ImmGetOpenStatus）为准：
///    打开 = 中文输入，关闭 = 英文输入。这是微软拼音、搜狗、QQ 拼音等主流输入法的通用语义。
/// </remarks>
public static class ImeController
{
    private static readonly IntPtr InvalidResult = new(-1);

    /// <summary>
    /// 尝试把 <paramref name="hwnd"/> 所属窗口的输入法切换到 <paramref name="state"/>。
    /// </summary>
    /// <returns>是否成功执行了切换。</returns>
    public static bool TryApply(IntPtr hwnd, ImeState state)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        bool open = state == ImeState.Chinese;

        // 主方案：向默认 IME 窗口发送控制消息（跨进程可靠）。
        bool ok = SetOpenStatusViaMessage(hwnd, open);

        if (!ok)
        {
            // 回退：同进程窗口可以直接操作 IME 上下文。
            ok = SetOpenStatusViaContext(hwnd, open);
        }

        return ok;
    }

    /// <summary>
    /// 切换并校验：如果状态没生效就重试若干次。
    /// 用于应对窗口刚激活时 IME 尚未初始化、或程序自身重置输入法的情况。
    /// </summary>
    /// <param name="hwnd">目标窗口。</param>
    /// <param name="state">目标状态。</param>
    /// <param name="attempts">最多尝试次数。</param>
    /// <param name="delayMs">每次尝试之间的间隔。</param>
    /// <returns>最终是否达到目标状态。</returns>
    public static bool ApplyWithVerification(IntPtr hwnd, ImeState state, int attempts = 4, int delayMs = 120)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return false;
        }

        bool anyApplied = false;

        for (int i = 0; i < Math.Max(1, attempts); i++)
        {
            if (!NativeMethods.IsWindow(hwnd))
            {
                return anyApplied;
            }

            anyApplied |= TryApply(hwnd, state);

            ImeState? current = TryGetState(hwnd);
            DebugLog.Write($"ApplyWithVerification: attempt {i + 1}/{attempts} target={state} current={current?.ToString() ?? "unknown"}");

            if (current == state)
            {
                return true;
            }

            if (i < attempts - 1 && delayMs > 0)
            {
                Thread.Sleep(delayMs);
            }
        }

        return anyApplied;
    }

    /// <summary>读取窗口当前的输入法状态，失败返回 null。</summary>
    public static ImeState? TryGetState(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero || !NativeMethods.IsWindow(hwnd))
        {
            return null;
        }

        // 主方案：向默认 IME 窗口查询打开状态。
        IntPtr result = QueryOpenStatusViaMessage(hwnd);
        if (result != InvalidResult)
        {
            return result != IntPtr.Zero ? ImeState.Chinese : ImeState.English;
        }

        // 回退：同进程窗口直接读上下文。
        IntPtr focus = GetFocusedWindow(hwnd);
        IntPtr hImc = NativeMethods.ImmGetContext(focus);
        if (hImc == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            return NativeMethods.ImmGetOpenStatus(hImc) ? ImeState.Chinese : ImeState.English;
        }
        catch
        {
            return null;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(focus, hImc);
        }
    }

    private static bool SetOpenStatusViaMessage(IntPtr hwnd, bool open)
    {
        try
        {
            IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(hwnd);
            if (imeWnd == IntPtr.Zero)
            {
                return false;
            }

            // 带超时，避免目标进程卡死拖住本程序。
            IntPtr sent = NativeMethods.SendMessageTimeout(
                imeWnd,
                NativeMethods.WM_IME_CONTROL,
                (IntPtr)NativeMethods.IMC_SETOPENSTATUS,
                (IntPtr)(open ? 1 : 0),
                NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK,
                500,
                out _);

            return sent != IntPtr.Zero;
        }
        catch
        {
            return false;
        }
    }

    private static IntPtr QueryOpenStatusViaMessage(IntPtr hwnd)
    {
        try
        {
            IntPtr imeWnd = NativeMethods.ImmGetDefaultIMEWnd(hwnd);
            if (imeWnd == IntPtr.Zero)
            {
                return InvalidResult;
            }

            IntPtr sent = NativeMethods.SendMessageTimeout(
                imeWnd,
                NativeMethods.WM_IME_CONTROL,
                (IntPtr)NativeMethods.IMC_GETOPENSTATUS,
                IntPtr.Zero,
                NativeMethods.SMTO_ABORTIFHUNG | NativeMethods.SMTO_BLOCK,
                500,
                out IntPtr result);

            return sent != IntPtr.Zero ? result : InvalidResult;
        }
        catch
        {
            return InvalidResult;
        }
    }

    private static bool SetOpenStatusViaContext(IntPtr hwnd, bool open)
    {
        IntPtr focus = GetFocusedWindow(hwnd);
        IntPtr hImc = NativeMethods.ImmGetContext(focus);
        if (hImc == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NativeMethods.ImmSetOpenStatus(hImc, open);
        }
        catch
        {
            return false;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(focus, hImc);
        }
    }

    /// <summary>找到目标窗口所在线程真正拥有键盘焦点的子窗口。</summary>
    private static IntPtr GetFocusedWindow(IntPtr fallback)
    {
        uint threadId = NativeMethods.GetWindowThreadProcessId(fallback, out _);
        if (threadId == 0)
        {
            return fallback;
        }

        var info = new NativeMethods.GUITHREADINFO
        {
            cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>(),
        };

        if (NativeMethods.GetGUIThreadInfo(threadId, ref info))
        {
            if (info.hwndFocus != IntPtr.Zero)
            {
                return info.hwndFocus;
            }

            if (info.hwndActive != IntPtr.Zero)
            {
                return info.hwndActive;
            }
        }

        return fallback;
    }
}
