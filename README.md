# 自动输入法切换 (AutoSwitching)

按**前台窗口所属程序**自动切换输入法的中/英状态。给不同程序设置不同规则，切换窗口时自动生效。

例如：

- `Code.exe`（VS Code）→ 英文
- `WindowsTerminal.exe` → 英文
- `WeChat.exe` → 中文

## 功能

- 托盘常驻，双击图标打开设置
- 按进程名（exe）匹配规则，不区分大小写
- 每条规则可设为「中文」或「英文」
- 未匹配到规则时，可选择「不干预」或统一切到某个状态
- 「延迟补切」：部分程序激活后会重置输入法，延迟若干毫秒再补一次
- 设置窗口里点「取当前窗口」，3 秒内切到目标程序即可自动填入进程名
- 单实例运行，避免多个实例互相抢输入法

## 运行要求

- Windows 10 1903 及以上 / Windows 11
- **无需安装任何运行时**：.NET Framework 4.8 已内置于上述系统

## 体积与资源占用

| 项目 | 数值 |
|---|---|
| 发布产物 | **单个 `AutoSwitching.exe`，约 45 KB** |
| 额外依赖 | **无**（不需要任何 DLL 或运行时安装包） |
| 常驻内存（工作集） | 约 33 MB |
| 线程数 | 8 |

> 早期版本基于 .NET 8，exe 虽只有 135 KB，但要求用户先安装约 55 MB 的
> .NET 8 桌面运行时。改用 .NET Framework 4.8 后，产物更小、零依赖、内存更低。

## 构建

```powershell
# 如果 dotnet 不在 PATH 里，用完整路径
& "$env:LOCALAPPDATA\Microsoft\dotnet\dotnet.exe" build "src\AutoSwitching\AutoSwitching.csproj" -c Release
```

产物：`src\AutoSwitching\bin\Release\net48\AutoSwitching.exe`

这个 exe 可以直接拷到任何 Windows 10 1903+ / Windows 11 机器上双击运行。

> 编译期需要 `Microsoft.NETFramework.ReferenceAssemblies` NuGet 包提供引用程序集
> （本机未安装 .NET Framework 4.8 目标包时必需），它**不会**进入发布产物。

## 使用

1. 运行 `AutoSwitching.exe`，托盘出现蓝色「中」图标
2. 双击图标打开设置
3. 在「程序规则」里添加程序名和对应输入法，点「保存」
4. 之后切换窗口就会自动切换输入法

## 配置文件

`%APPDATA%\AutoSwitching\config.json`

```json
{
  "enabled": true,
  "defaultState": null,
  "reapplyDelayMs": 120,
  "rules": [
    { "processName": "Code.exe", "state": "English", "enabled": true, "note": "VS Code 默认英文" }
  ]
}
```

`defaultState` 为 `null` 表示未匹配时不干预，也可写 `"Chinese"` / `"English"`。
属性名不区分大小写，旧版大写写法（`Enabled` / `Rules` 等）仍可正常读取。

## 实现说明

- 用 `SetWinEventHook(EVENT_SYSTEM_FOREGROUND)` 监听前台窗口变化
- 用 `GetGUIThreadInfo` 找到目标窗口线程真正持有焦点的子窗口（`hwndFocus`），IME 状态挂在焦点窗口上
- 跨进程切换用 `SendMessageTimeout(ImmGetDefaultIMEWnd(焦点窗口), WM_IME_CONTROL, IMC_SETOPENSTATUS, ...)`，
  带 `SMTO_ABORTIFHUNG | SMTO_BLOCK` 和 500ms 超时，避免目标程序无响应时卡死
- 读取状态用同样的消息方式 `IMC_GETOPENSTATUS`；同进程内回退到 `ImmGetContext` / `ImmGetOpenStatus`
- 以 **open 状态**（`ImmGetOpenStatus`）作为中/英判据：打开 = 中文，关闭 = 英文。
  转换模式位（`IME_CMODE_NATIVE` 等）在不同输入法下语义不一致，不作为判据
- 切换后立即回读校验，不一致则重试（默认 4 次，间隔 120ms），应对部分程序激活后重置输入法
- 所有 IME 调用都在独立后台线程执行（`BlockingCollection` 队列），不阻塞 UI
- 配置文件读写用自带的极简 JSON 实现（`Core/Json.cs`），避免引入 `System.Text.Json` 等额外程序集

> 注 1：`ImmGetContext` 是**线程本地**的，跨进程调用只会拿到调用方自己的 IME 上下文，
> 因此不能用来操作其他程序的输入法；`AttachThreadInput` 跨进程也不可靠（常返回
> `ERROR_INVALID_PARAMETER`，且有死锁风险），本项目不使用。
>
> 注 2：IME 状态属于**拥有键盘焦点的子窗口**，不是顶层窗口。
> 例如 WinForms 的 `TextBox` 有自己的 IME 上下文，只改顶层窗口的 IME 窗口不会影响实际输入。
> 因此必须先 `GetGUIThreadInfo` 拿到 `hwndFocus`，再对它取 `ImmGetDefaultIMEWnd`。

## 测试

```powershell
# 1) 构建（主程序 + 测试目标程序）
dotnet build AutoSwitching.sln -c Release

# 2) 跑全部自动化检查（构建产物 + 进程内自检 + 配置读写）
powershell -NoProfile -ExecutionPolicy Bypass -File tests\run-tests.ps1
```

`run-tests.ps1` 覆盖三类检查，全部可在无人值守环境稳定通过：

| 检查 | 内容 |
| --- | --- |
| 构建产物 | exe 存在、体积 < 200 KB、发布目录内除 exe 外无任何文件或子目录 |
| 进程内自检 | 中 → 英 → 中 三步全部生效 |
| 配置文件 | 首次启动生成默认配置、中文备注不损坏、旧版大写属性名向后兼容、损坏配置回退默认值 |

> `run-tests.ps1` **不会碰你的真实配置**。它通过环境变量
> `AUTOSWITCHING_CONFIG_DIR` 把程序的配置目录重定向到 `%TEMP%` 下的临时目录，
> 测试结束后整个删掉。因此即使脚本被强杀、终端被关闭或机器断电，
> `%APPDATA%\AutoSwitching\config.json` 也不会丢失。
> 脚本最后还会用 SHA256 校验真实配置未被改动。

### 手动验证跨进程切换

跨进程切换**没有**自动化测试，因为它需要目标窗口拿到真实的键盘焦点。
脚本用 `AttachThreadInput` + `SetForegroundWindow` 强行抢前台时，
Windows 10/11 不会把真正的输入焦点交给该窗口（`GetGUIThreadInfo` 返回 `hwndFocus=0`），
此时 IME 状态挂在哪个窗口上是不确定的，测试结果不可靠。

请手动验证：

1. 运行 `AutoSwitching.exe`，托盘出现蓝色「中」图标
2. 双击图标打开设置，添加规则：`notepad.exe` → 英文，保存
3. 打开记事本，点进输入框，确认输入法变为英文
4. 切回其他窗口再切回记事本，确认仍为英文

#### 用 TestTarget 观察切换结果

`tests/TestTarget` 是一个专门为此准备的目标程序：一个标题为
`AutoSwitchingTestTarget` 的窗口，里面有一个 `TextBox`。它每 300 ms 采样一次
自己的 IME 状态并写入报告文件，因此可以直接看到「切换有没有真的生效」，
不用靠肉眼判断输入法状态栏。

```powershell
# 构建（TestTarget 已包含在解决方案里）
dotnet build AutoSwitching.sln -c Release

# 启动目标程序，每 300ms 把 IME 状态写入报告文件
.\tests\TestTarget\bin\Release\net48\TestTarget.exe --report "$env:TEMP\tt.txt"
```

报告是**每次覆盖写入的单行**，形如：

```
[14:32:07.418] ctx=CN(open=True,conv=0x401) msg=CN fg=True boxFocus=True focus=0x120A3E active=0x120A3C box=0x120A3E form=0x120A3C ime=0x120A40 boxFocused=True formIme=0x120A42
```

关键字段：

| 字段 | 含义 |
| --- | --- |
| `ctx` | 目标程序**自己**读到的 IME 状态，`open=True` 为中文、`open=False` 为英文 |
| `msg` | 用 `SendMessageTimeout` 向默认 IME 窗口查询的结果（与 AutoSwitching 同一条路径） |
| `fg` | 目标窗口是否为前台窗口 |
| `boxFocus` | 输入焦点是否真的落在那个 `TextBox` 上 |

在设置里给 `TestTarget.exe` 配一条规则（例如 → 英文），然后点进 TestTarget
的输入框，报告里的 `open` 应当随之变化。因为报告是覆盖写入的，用
`Get-Content "$env:TEMP\tt.txt"` 反复读即可，不要用 `-Wait`。

> 注意：报告反映的是**目标程序自己**的 IME 状态，所以它比外部脚本抢前台
> 再查询要可靠得多——焦点是用户真实点击给的，不存在 `hwndFocus=0` 的问题。

## 诊断

设置环境变量 `AUTOSWITCHING_DEBUG=1` 后启动，会把详细日志写到 `%TEMP%\autoswitching.log`：

```powershell
$env:AUTOSWITCHING_DEBUG = "1"
.\src\AutoSwitching\bin\Release\net48\AutoSwitching.exe
```

## 命令行参数

| 参数 | 说明 |
| --- | --- |
| （无） | 启动托盘程序 |
| `--settings` / `-s` | 启动并直接打开设置窗口 |
| `--selftest` | 打开自检窗口 |
| `--selftest --auto` | 自检一次后把结果写到 `%TEMP%\autoswitching-selftest.txt` 并退出 |

## 项目结构

```
.
├── AutoSwitching.sln
├── Directory.Build.props        # 共享编译设置
├── build.cmd                    # 一键构建
├── .github/workflows/
│   ├── build.yml                # 推送即构建
│   └── release.yml              # 推送 v* tag 时自动构建并发布
├── src/
│   └── AutoSwitching/
│       ├── AutoSwitching.csproj # net48，单文件零依赖
│       ├── app.manifest         # 高 DPI / 兼容性声明
│       ├── GlobalUsings.cs      # 补上 net48 缺失的 WinForms 隐式 using
│       ├── Program.cs           # 入口、单实例、命令行参数
│       ├── TrayApplicationContext.cs
│       ├── AppIcon.cs           # 运行时绘制托盘图标
│       ├── Core/
│       │   ├── AppConfig.cs     # 配置模型与读写
│       │   ├── Json.cs          # 极简 JSON 读写（替代 System.Text.Json）
│       │   ├── DebugLog.cs      # 可选诊断日志
│       │   ├── ForegroundWatcher.cs
│       │   ├── ImeController.cs
│       │   └── NativeMethods.cs
│       └── UI/
│           ├── SettingsForm.cs
│           └── SelfTestForm.cs
└── tests/
    ├── TestTarget/              # 独立的测试目标程序（net48）
    └── run-tests.ps1            # 自动化检查（产物 + 自检 + 配置）
```

## 已知限制

- 只切换**同一个输入法内部**的中/英状态（如微软拼音的中/英），不切换输入法本身
- 以管理员权限运行的程序，本程序需要同样以管理员权限运行才能生效
- 部分使用自绘输入框的程序（如某些游戏、Electron 应用）可能不响应标准 IME 接口
