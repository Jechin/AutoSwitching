# 双向验证：英文规则 + 中文规则都要生效。
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$targetExe = Join-Path $PSScriptRoot "TestTarget\bin\Release\net8.0-windows\TestTarget.exe"
$appExe = Join-Path $root "bin\Release\net8.0-windows\AutoSwitching.exe"
$report = Join-Path $env:TEMP "target-ime-report.txt"
$configPath = Join-Path $env:APPDATA "AutoSwitching\config.json"

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class W2 {
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

function Test-Rule([string]$stateName, [string]$expectPattern) {
    Write-Host ""
    Write-Host "=== rule: $stateName ==="

    Get-Process AutoSwitching, TestTarget -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 700
    Remove-Item $report -ErrorAction SilentlyContinue

    $cfg = @"
{
  "Enabled": true,
  "DefaultState": null,
  "ReapplyDelayMs": 150,
  "Rules": [
    { "ProcessName": "TestTarget.exe", "State": "$stateName", "Enabled": true, "Note": "e2e" }
  ]
}
"@
    Set-Content -Path $configPath -Value $cfg -Encoding UTF8

    $env:AUTOSWITCHING_DEBUG = "1"
    Remove-Item (Join-Path $env:TEMP "autoswitching.log") -ErrorAction SilentlyContinue
    Start-Process -FilePath $appExe | Out-Null
    Start-Sleep -Seconds 2

    Start-Process -FilePath $targetExe -ArgumentList "--report", $report | Out-Null
    Start-Sleep -Seconds 3

    $h = [W2]::FindByTitle("AutoSwitchingTestTarget")
    if ($h -eq [IntPtr]::Zero) { throw "target not found" }

    [W2]::ForceForeground($h) | Out-Null
    Start-Sleep -Milliseconds 2000

    $result = Read-Report
    Write-Host "report: $result"

    if ($result -match $expectPattern) {
        Write-Host "PASS ($stateName)"
        return $true
    } else {
        Write-Host "FAIL ($stateName), expected pattern: $expectPattern"
        return $false
    }
}

$ok1 = Test-Rule "English" 'ctx=EN'
$ok2 = Test-Rule "Chinese" 'ctx=CN'

Get-Process AutoSwitching, TestTarget -ErrorAction SilentlyContinue | Stop-Process -Force

Write-Host ""
if ($ok1 -and $ok2) {
    Write-Host "RESULT: ALL PASS"
    exit 0
} else {
    Write-Host "RESULT: SOME FAILED"
    exit 1
}

