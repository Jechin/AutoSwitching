namespace AutoSwitching.Core;

/// <summary>
/// 简单的诊断日志，写入 %TEMP%\autoswitching.log。
/// 仅在存在环境变量 AUTOSWITCHING_DEBUG=1 时启用。
/// </summary>
public static class DebugLog
{
    private static readonly bool Enabled =
        Environment.GetEnvironmentVariable("AUTOSWITCHING_DEBUG") == "1";

    private static readonly string LogPath =
        Path.Combine(Path.GetTempPath(), "autoswitching.log");

    private static readonly object Gate = new();

    public static void Write(string message)
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    LogPath,
                    $"{DateTime.Now:HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 日志失败不影响主流程。
        }
    }

    public static void Reset()
    {
        if (!Enabled)
        {
            return;
        }

        try
        {
            lock (Gate)
            {
                File.WriteAllText(LogPath, string.Empty);
            }
        }
        catch
        {
            // 忽略
        }
    }
}
