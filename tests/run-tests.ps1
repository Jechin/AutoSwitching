# 自动输入法切换 —— 本机验证脚本
#
# 只包含能在无人值守环境下稳定通过的检查：
#   1. 构建产物（单文件、零依赖）
#   2. 进程内 IME 自检（中 → 英 → 中 全部生效）
#   3. 配置解析与读写（含旧版大写属性名向后兼容）
#
# 为什么没有跨进程 E2E：
#   跨进程验证需要目标窗口拿到**真实的键盘焦点**。脚本用
#   AttachThreadInput + SetForegroundWindow 强行抢前台时，Windows 10/11
#   不会把真正的输入焦点交给该窗口（GetGUIThreadInfo 返回 hwndFocus=0），
#   此时 IME 状态挂在哪个窗口上是不确定的，测试结果不可靠。
#   跨进程行为请手动验证，步骤见 README 的「手动验证跨进程切换」一节，
#   可以用 tests/TestTarget 作为目标程序。
#
# 注意：本文件必须保存为「UTF-8 with BOM」。
#   Windows PowerShell 5.1 在没有 BOM 时会按系统 ANSI 代码页解析脚本，
#   文件里的中文会变成乱码并导致语法错误。详见 CONTRIBUTING.md。
#
# 关于用户配置的安全：
#   第 3 节通过 AUTOSWITCHING_CONFIG_DIR 把程序的配置目录重定向到 TEMP 下的
#   临时目录，**从不写入**用户的真实配置（%APPDATA%\AutoSwitching\config.json）。
#   因此即使脚本被强杀、终端被关闭或机器断电，用户配置也不会丢失。
#   脚本最后还会用 SHA256 校验真实配置未被改动。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$appExe = Join-Path $root "src\AutoSwitching\bin\Release\net48\AutoSwitching.exe"
$appDir = Split-Path $appExe
$selftestReport = Join-Path $env:TEMP "autoswitching-selftest.txt"
$dumpReport = Join-Path $env:TEMP "autoswitching-dump.json"

$script:failed = 0

function Write-Pass([string]$name) {
    Write-Host "  PASS  $name" -ForegroundColor Green
}

function Write-Fail([string]$name, [string]$detail) {
    Write-Host "  FAIL  $name" -ForegroundColor Red
    if ($detail) { Write-Host "        $detail" -ForegroundColor DarkGray }
    $script:failed++
}

function Test-Check([string]$name, [bool]$ok, [string]$detail = "") {
    if ($ok) { Write-Pass $name } else { Write-Fail $name $detail }
}

# 轮询等待文件出现，避免用固定 Start-Sleep 在慢机器上误判。
function Wait-ForFile([string]$path, [int]$timeoutSeconds = 15) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $path) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return (Test-Path $path)
}

function Stop-App {
    Get-Process AutoSwitching -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 400
}

# ---------------------------------------------------------------- 1. 构建产物
Write-Host ""
Write-Host "[1/3] 构建产物" -ForegroundColor Cyan

if (-not (Test-Path $appExe)) {
    Write-Fail "AutoSwitching.exe 存在" "找不到 $appExe，请先运行 build.cmd"
    Write-Host ""
    Write-Host "RESULT: FAILED" -ForegroundColor Red
    exit 1
}

Test-Check "AutoSwitching.exe 存在" $true

$sizeKb = [math]::Round((Get-Item $appExe).Length / 1KB, 1)
Test-Check "体积小于 200 KB" ($sizeKb -lt 200) "实际 $sizeKb KB"

# 项目的卖点是「单文件零依赖」：发布目录里除了 exe 不应有任何其他文件。
# 只查 *.dll 会漏掉 .config / .pdb / 卫星资源目录等，所以这里查全部。
$extraFiles = @(Get-ChildItem $appDir -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -ne 'AutoSwitching.exe' })
$extraDirs = @(Get-ChildItem $appDir -Directory -ErrorAction SilentlyContinue)

Test-Check "目录内只有 AutoSwitching.exe" ($extraFiles.Count -eq 0) `
    ("多余文件: " + (($extraFiles | ForEach-Object { $_.Name }) -join ', '))
Test-Check "无额外子目录" ($extraDirs.Count -eq 0) `
    ("多余目录: " + (($extraDirs | ForEach-Object { $_.Name }) -join ', '))

# ---------------------------------------------------------------- 2. 进程内自检
Write-Host ""
Write-Host "[2/3] 进程内 IME 自检" -ForegroundColor Cyan

Stop-App
Remove-Item $selftestReport -ErrorAction SilentlyContinue

Start-Process -FilePath $appExe -ArgumentList "--selftest", "--auto" | Out-Null

# 自检窗口约 1.2 秒后自动跑完并写结果文件。
$gotReport = Wait-ForFile $selftestReport 15
Stop-App

if (-not $gotReport) {
    Write-Fail "自检结果文件生成" "15 秒内没有生成 $selftestReport"
} else {
    $text = Get-Content $selftestReport -Raw -Encoding UTF8

    # 自检会输出三行带 ✓ 的检查项；出现 ✗ 说明有步骤失败。
    $passCount = ([regex]::Matches($text, [string][char]0x2713)).Count
    $failCount = ([regex]::Matches($text, [string][char]0x2717)).Count

    Test-Check "切到中文生效" ($text -match '切到中文.*\u2713') $text.Trim()
    Test-Check "切到英文生效" ($text -match '切到英文.*\u2713') $text.Trim()
    Test-Check "可反复切回中文" ($text -match '再切中文.*\u2713') $text.Trim()
    Test-Check "无失败项" ($failCount -eq 0) "发现 $failCount 个 ✗"
    Test-Check "三项检查全部通过" ($passCount -eq 3) "实际 $passCount 个 ✓"
}

# ---------------------------------------------------------------- 3. 配置解析
Write-Host ""
Write-Host "[3/3] 配置解析与读写" -ForegroundColor Cyan

# 关键设计：测试**完全不碰**用户的真实配置。
#
# 早先的做法是「备份真实配置 → 改写 → finally 还原」，但 finally 只在正常结束
# 或终止性错误时执行。进程被强杀、终端被关掉、机器断电时 finally 不会跑，
# 此时真实配置已被删除或写成测试数据，备份躺在 %TEMP% 里一个随机文件名下，
# 用户根本找不到 —— 等同于配置丢失。
#
# 现在改为：通过 AUTOSWITCHING_CONFIG_DIR 把程序的配置目录重定向到一个
# 临时目录。真实配置从头到尾只被读取（用于校验未被改动），从不被写入。
$realConfigDir = Join-Path $env:APPDATA "AutoSwitching"
$realConfigPath = Join-Path $realConfigDir "config.json"

# 记录真实配置的初始指纹，结束时校验它没有被改动。
$realConfigExisted = Test-Path $realConfigPath -PathType Leaf
$realConfigHash = if ($realConfigExisted) { (Get-FileHash $realConfigPath -Algorithm SHA256).Hash } else { $null }

# 隔离的配置目录：TEMP 下的唯一目录，测试结束后整个删掉。
$sandboxDir = Join-Path $env:TEMP ("autoswitching-test-{0}" -f [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $sandboxDir -Force | Out-Null
$configPath = Join-Path $sandboxDir "config.json"

# 让本脚本启动的所有子进程都使用隔离目录。
# Start-Process 会继承当前进程的环境变量，所以设置一次即可。
$env:AUTOSWITCHING_CONFIG_DIR = $sandboxDir

try {
    # ---- 3a. 首次启动生成默认配置 ----
    Start-Process -FilePath $appExe | Out-Null
    $created = Wait-ForFile $configPath 15
    Stop-App

    Test-Check "首次启动生成 config.json" $created

    if ($created) {
        $json = Get-Content $configPath -Raw -Encoding UTF8

        Test-Check "配置含 enabled 字段" ($json -match '"enabled"')
        Test-Check "配置含 rules 数组" ($json -match '"rules"')
        Test-Check "默认规则含 Code.exe" ($json -match 'Code\.exe')
        Test-Check "默认规则含 WeChat.exe" ($json -match 'WeChat\.exe')
        Test-Check "中文备注未损坏" ($json -match '微信默认中文')
    }

    # ---- 3b. 旧版大写属性名必须仍能读取（向后兼容）----
    #
    # 用 --dump-config 直接输出解析结果。
    # 不能靠「启动一次再看文件」——程序只在文件不存在时才写盘，
    # 已存在的文件不会被重写，那样断言恒真、测不出任何问题。
    $legacy = '{ "Enabled": false, "DefaultState": "Chinese", "ReapplyDelayMs": 300, "Rules": [ { "ProcessName": "Legacy.exe", "State": "Chinese", "Enabled": true, "Note": "old" } ] }'
    [System.IO.File]::WriteAllText($configPath, $legacy, (New-Object System.Text.UTF8Encoding $false))

    Remove-Item $dumpReport -ErrorAction SilentlyContinue
    Start-Process -FilePath $appExe -ArgumentList "--dump-config", $dumpReport -Wait | Out-Null
    $dumped = Wait-ForFile $dumpReport 10

    Test-Check "旧版配置可被解析（--dump-config 有输出）" $dumped

    if ($dumped) {
        $parsed = Get-Content $dumpReport -Raw -Encoding UTF8

        Test-Check "旧版 Enabled=false 被读取" ($parsed -match '"enabled":\s*false')
        Test-Check "旧版 DefaultState=Chinese 被读取" ($parsed -match '"defaultState":\s*"Chinese"')
        Test-Check "旧版 ReapplyDelayMs=300 被读取" ($parsed -match '"reapplyDelayMs":\s*300')
        Test-Check "旧版规则 Legacy.exe 被读取" ($parsed -match 'Legacy\.exe')
        Test-Check "旧版规则 State=Chinese 被读取" ($parsed -match '"state":\s*"Chinese"')
    }

    # ---- 3c. 损坏的配置必须回退到默认值，而不是崩溃 ----
    [System.IO.File]::WriteAllText(
        $configPath,
        '{ "Enabled": BROKEN!!! "Legacy.exe"',
        (New-Object System.Text.UTF8Encoding $false))

    Remove-Item $dumpReport -ErrorAction SilentlyContinue
    Start-Process -FilePath $appExe -ArgumentList "--dump-config", $dumpReport -Wait | Out-Null
    $dumpedBroken = Wait-ForFile $dumpReport 10

    Test-Check "损坏配置不导致崩溃" $dumpedBroken

    if ($dumpedBroken) {
        $fallback = Get-Content $dumpReport -Raw -Encoding UTF8

        # 回退到默认配置：应出现默认规则，且不应出现损坏文件里的 Legacy.exe。
        Test-Check "损坏配置回退到默认规则" ($fallback -match 'Code\.exe')
        Test-Check "损坏配置未残留脏数据" (-not ($fallback -match 'Legacy\.exe'))
    }

    # ---- 3d. 真实配置必须原封不动 ----
    # 这是本节的兜底断言：即使前面所有步骤都出错，也不允许动到用户数据。
    $realStillExisted = Test-Path $realConfigPath -PathType Leaf
    if ($realConfigExisted) {
        $realHashNow = if ($realStillExisted) { (Get-FileHash $realConfigPath -Algorithm SHA256).Hash } else { $null }
        Test-Check "用户真实配置未被改动" ($realStillExisted -and $realHashNow -eq $realConfigHash) `
            "真实配置 $realConfigPath 在测试期间被修改或删除"
    }
    else {
        Test-Check "测试未凭空创建用户配置" (-not $realStillExisted) `
            "测试在 $realConfigPath 创建了文件"
    }
}
finally {
    # 清理隔离目录。即使这里失败，受影响的也只是 TEMP 下的临时目录，
    # 用户的真实配置从未被写入，不存在数据丢失风险。
    Remove-Item $sandboxDir -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $dumpReport -Force -ErrorAction SilentlyContinue
    Remove-Item Env:\AUTOSWITCHING_CONFIG_DIR -ErrorAction SilentlyContinue
    Stop-App
}

# ---------------------------------------------------------------- 汇总
Write-Host ""
if ($script:failed -eq 0) {
    Write-Host "RESULT: ALL PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host "RESULT: $($script:failed) FAILED" -ForegroundColor Red
    exit 1
}
