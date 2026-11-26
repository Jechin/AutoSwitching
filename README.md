# AutoSwitching · 自动输入法切换

按**前台窗口所属程序**自动切换输入法的中/英状态 —— 给不同程序设置不同规则，切换窗口时自动生效。

例如：

| 程序 | 期望状态 |
| --- | --- |
| `Code.exe`（VS Code） | 英文 |
| `WindowsTerminal.exe` | 英文 |
| `WeChat.exe` | 中文 |

## 功能

- 托盘常驻，双击图标打开设置
- 按进程名（exe）匹配规则，不区分大小写
- 每条规则可设为「中文」或「英文」
- 未匹配到规则时，可选择「不干预」或统一切到某个状态
- **延迟补切**：部分程序激活后会重置输入法，延迟若干毫秒再补一次
- 设置窗口里点「取当前窗口」，3 秒内切到目标程序即可自动填入进程名
- 单实例运行，避免多个实例互相抢输入法

## 运行要求

- Windows 10 / 11
- [.NET 8 桌面运行时](https://dotnet.microsoft.com/download/dotnet/8.0)（`Microsoft.WindowsDesktop.App 8.x`）

## 快速开始

```powershell
# 构建
dotnet build -c Release

# 运行
.\src\AutoSwitching\bin\Release\net8.0-windows\AutoSwitching.exe
```

产物路径：`src/AutoSwitching/bin/Release/net8.0-windows/AutoSwitching.exe`

`dotnet` 不在 PATH 时，可以用安装到了用户目录的完整路径：

```powershell
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build -c Release
```

也可以直接双击仓库根目录的 `build.cmd`（会自动处理上面两种路径情况）。

## 发布为单文件（可选）

```powershell
dotnet publish src\AutoSwitching\AutoSwitching.csproj -c Release -r win-x64 `
  --self-contained false -p:PublishSingleFile=true
```

## 使用

1. 运行 `AutoSwitching.exe`，托盘出现蓝色「中」图标
2. 双击图标打开设置
3. 在「程序规则」里添加程序名和对应输入法，点「保存」
4. 之后切换窗口就会自动切换输入法

## 配置

配置文件位于 `%APPDATA%\AutoSwitching\config.json`，首次运行会自动生成带示例规则的默认配置。

```json
{
  "Enabled": true,
  "DefaultState": null,
  "ReapplyDelayMs": 120,
  "Rules": [
    {
      "ProcessName": "Code.exe",
      "State": "English",
      "Enabled": true,
      "Note": "VS Code 默认英文"
    }
  ]
}
```

| 字段 | 说明 |
| --- | --- |
| `Enabled` | 总开关 |
| `DefaultState` | 未匹配到规则时的状态。`null` = 不干预，也可写 `"Chinese"` / `"English"` |
| `ReapplyDelayMs` | 延迟补切的毫秒数，`0` 表示不补切 |
| `Rules[].ProcessName` | 进程名，不区分大小写，可省略 `.exe` |
| `Rules[].Enabled` | 是否启用该条规则 |

## 实现说明

- 用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 监听前台窗口变化
- 用 `GetGUIThreadInfo` 找到目标窗口线程真正持有焦点的子窗口（`hwndFocus`），IME 状态挂在焦点窗口上
- 跨进程切换用 `SendMessageTimeout(ImmGetDefaultIMEWnd(hwnd), WM_IME_CONTROL, IMC_SETOPENSTATUS, ...)`，
  带 `SMTO_ABORTIFHUNG | SMTO_BLOCK` 和 500ms 超时，避免目标程序无响应时卡死
- 读取状态用同样的消息方式 `IMC_GETOPENSTATUS`；同进程内回退到 `ImmGetContext` / `ImmGetOpenStatus`
- 以 **open 状态**（`ImmGetOpenStatus`）作为中/英判据：打开 = 中文，关闭 = 英文。
  转换模式位（`IME_CMODE_NATIVE` 等）在不同输入法下语义不一致，不作为判据
- 切换后立即回读校验，不一致则重试（默认 4 次，间隔 120ms），应对部分程序激活后重置输入法
- 所有 IME 调用都在独立后台线程执行（`BlockingCollection` 队列），不阻塞 UI

> **为什么不用 `ImmGetContext` / `AttachThreadInput`**
>
> `ImmGetContext` 是**线程本地**的，跨进程调用只会拿到调用方自己的 IME 上下文，
> 因此不能用来操作其他程序的输入法。`AttachThreadInput` 跨进程也不可靠（常返回
> `ERROR_INVALID_PARAMETER`，且有死锁风险），本项目不使用。

## 项目结构

```
.
├── AutoSwitching.sln
├── Directory.Build.props        # 共享编译设置
├── build.cmd                    # 一键构建
├── src/
│   └── AutoSwitching/
│       ├── AutoSwitching.csproj
│       ├── Program.cs           # 入口、单实例、命令行参数
│       ├── TrayApplicationContext.cs
│       ├── AppIcon.cs           # 运行时绘制托盘图标
│       ├── Core/
│       │   ├── AppConfig.cs     # 配置模型与读写
│       │   ├── DebugLog.cs      # 可选诊断日志
│       │   ├── ForegroundWatcher.cs
│       │   ├── ImeController.cs
│       │   └── NativeMethods.cs
│       └── UI/
│           ├── SettingsForm.cs
│           └── SelfTestForm.cs
└── tests/
    ├── TestTarget/              # 独立的测试目标程序
    ├── run-e2e.ps1              # 单规则端到端测试
    └── run-both.ps1             # 中/英双向端到端测试
```

## 测试

```powershell
# 1) 构建
dotnet build AutoSwitching.sln -c Release

# 2) 进程内自检（中 → 英 → 中）
.\src\AutoSwitching\bin\Release\net8.0-windows\AutoSwitching.exe --selftest --auto

# 3) 跨进程端到端测试
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-e2e.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-both.ps1
```

> 端到端测试会启动真实的 GUI 窗口并抢占前台焦点，请在可交互的桌面会话中运行，
> 不要在没有桌面环境的 CI 里跑。

## 诊断

设置环境变量 `AUTOSWITCHING_DEBUG=1` 后启动，会把详细日志写到 `%TEMP%\autoswitching.log`：

```powershell
$env:AUTOSWITCHING_DEBUG = "1"
.\src\AutoSwitching\bin\Release\net8.0-windows\AutoSwitching.exe
```

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| （无） | 启动托盘程序 |
| `--settings` / `-s` | 启动并直接打开设置窗口 |
| `--selftest` | 打开自检窗口 |
| `--selftest --auto` | 自检一次后把结果写到 `%TEMP%\autoswitching-selftest.txt` 并退出 |

## 已知限制

- 只切换**同一个输入法内部**的中/英状态（如微软拼音的中/英），不切换输入法本身
- 以管理员权限运行的程序，本程序需要同样以管理员权限运行才能生效
- 部分使用自绘输入框的程序（如某些游戏、Electron 应用）可能不响应标准 IME 接口

## 许可

MIT
