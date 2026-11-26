# 端到端验证：AutoSwitching 是否能在切换窗口时自动改输入法。
# 目标程序是独立进程的 WinForms 文本框（tests/TestTarget）。

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$targetExe = Join-Path $PSScriptRoot "TestTarget\bin\Release\net8.0-windows\TestTarget.exe"
$appExe = Join-Path $root "bin\Release\net8.0-windows\AutoSwitching.exe"
$report = Join-Path $env:TEMP "target-ime-report.txt"
$configPath = Join-Path $env:APPDATA "AutoSwitching\config.json"

if (-not (Test-Path $targetExe)) { throw "TestTarget.exe not found: $targetExe" }
if (-not (Test-Path $appExe)) { throw "AutoSwitching.exe not found: $appExe" }

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool f);
  [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
  [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr p);
  public delegate bool EnumProc(IntPtr h, IntPtr p);

  public static IntPtr FindByTitle(string part) {
    IntPtr found = IntPtr.Zero;
    EnumWindows((h,p) => {
      if (!IsWindowVisible(h)) return true;
      var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
      if (sb.ToString().Contains(part)) { found = h; return false; }
      return true;
    }, IntPtr.Zero);
    return found;
  }

  public static bool ForceForeground(IntPtr h) {
    uint dummy;
    uint fgThread = GetWindowThreadProcessId(GetForegroundWindow(), out dummy);
    uint myThread = GetCurrentThreadId();
    bool attached = AttachThreadInput(myThread, fgThread, true);
    ShowWindow(h, 9);
    BringWindowToTop(h);
    bool ok = SetForegroundWindow(h);
    if (attached) AttachThreadInput(myThread, fgThread, false);
    return ok;
  }
}
"@

function Read-Report {
    if (Test-Path $report) { return (Get-Content $report -Raw).Trim() }
    return "(no report)"
}

Write-Output "=== E2E: AutoSwitching ==="

# 清理
Get-Process AutoSwitching, TestTarget -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 600
Remove-Item $report -ErrorAction SilentlyContinue

# 配置：TestTarget.exe -> 英文
$cfg = @'
{
  "Enabled": true,
  "DefaultState": null,
  "ReapplyDelayMs": 150,
  "Rules": [
    { "ProcessName": "TestTarget.exe", "State": "English", "Enabled": true, "Note": "e2e" }
  ]
}
'@
New-Item -ItemType Directory -Force -Path (Split-Path $configPath) | Out-Null
Set-Content -Path $configPath -Value $cfg -Encoding UTF8

# 启动 AutoSwitching
$env:AUTOSWITCHING_DEBUG = "1"
Remove-Item (Join-Path $env:TEMP "autoswitching.log") -ErrorAction SilentlyContinue
Start-Process -FilePath $appExe | Out-Null
Start-Sleep -Seconds 2

# 启动目标程序
$target = Start-Process -FilePath $targetExe -ArgumentList "--report", $report -PassThru
Start-Sleep -Seconds 3

$h = [W]::FindByTitle("AutoSwitchingTestTarget")
if ($h -eq [IntPtr]::Zero) { throw "target window not found" }
Write-Output "target hwnd=0x$($h.ToInt64().ToString('X'))"

# 激活目标窗口
[W]::ForceForeground($h) | Out-Null
Start-Sleep -Milliseconds 1500
Write-Output "state after activate (rule=English): $(Read-Report)"

# 切到别处
$shell = New-Object -ComObject WScript.Shell
$shell.AppActivate($PID) | Out-Null
Start-Sleep -Milliseconds 1500

# 切回
[W]::ForceForeground($h) | Out-Null
Start-Sleep -Milliseconds 2000
$final = Read-Report
Write-Output "state after switch back: $final"

Write-Output "--- debug log ---"
$logPath = Join-Path $env:TEMP "autoswitching.log"
if (Test-Path $logPath) { Get-Content $logPath | Select-Object -Last 25 } else { Write-Output "(no log)" }

# 清理
Get-Process TestTarget -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process AutoSwitching -ErrorAction SilentlyContinue | Stop-Process -Force

if ($final -match 'ctx=EN') {
    Write-Output "RESULT: PASS"
    exit 0
} else {
    Write-Output "RESULT: FAIL"
    exit 1
}
