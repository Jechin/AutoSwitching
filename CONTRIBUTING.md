# 贡献前请先看一眼

## 环境

- Windows 10 1903+ / Windows 11
- .NET SDK 6.0 或更高（仅用于编译；产物是 .NET Framework 4.8，不需要安装 4.8 目标包）

> 项目目标框架是 `net48`，目的是让产物零依赖、体积最小。
> 编译时由 `Microsoft.NETFramework.ReferenceAssemblies` NuGet 包提供引用程序集。
> 请不要随意把 `TargetFramework` 改回 `net8.0-windows`，那会让用户必须安装 .NET 8 桌面运行时。

## 构建与验证

提交前请确保干净构建通过、且不引入新的警告：

```powershell
dotnet build AutoSwitching.sln -c Release
```

产物：`src/AutoSwitching/bin/Release/net48/AutoSwitching.exe`（单文件，约 45 KB）

## 代码约定

仓库根目录的 `.editorconfig` 与 `.gitattributes` 已固定了格式：

- `.cs` / `.csproj` / `.md` / `.json` → **LF**
- `.cmd` / `.bat` / `.ps1` → **CRLF**（Windows 脚本对行尾敏感，请勿改动）
- 文件编码一律 UTF-8（无 BOM）

在 WSL 或 macOS 下编辑时，git 会按 `.gitattributes` 在暂存区统一为 LF，
不需要手动转换；但**不要**给 `.ps1` / `.cmd` 提交 LF 版本。

## 不要提交的内容

`bin/`、`obj/`、`*.exe`、`*.dll`、`*.pdb` 等构建产物已被 `.gitignore` 排除。
如果你发现它们出现在 `git status` 里，说明 `.gitignore` 被改坏了。

## 改动 IME 相关代码时

- 不要用 `ImmGetContext` 做跨进程操作（它只对调用线程有效）
- 不要用 `AttachThreadInput`（跨进程不可靠且有死锁风险）
- 跨进程一律用 `SendMessageTimeout` + `WM_IME_CONTROL`，并保留超时
- IME 状态属于**拥有键盘焦点的子窗口**，不是顶层窗口。
  必须先 `GetGUIThreadInfo` 拿到 `hwndFocus`，再对它取 `ImmGetDefaultIMEWnd`，
  否则像 WinForms `TextBox` 这种自带 IME 上下文的控件不会被切换
- 改完请至少跑一遍 `tests/run-tests.ps1`（产物 + 进程内自检 + 配置读写）
- 跨进程切换请按 README 的「手动验证跨进程切换」一节手动确认，
  可以用 `tests/TestTarget` 作为目标程序（它会把自身 IME 状态写进报告文件）

## 测试不得触碰用户真实配置

`run-tests.ps1` 通过环境变量 `AUTOSWITCHING_CONFIG_DIR` 把程序的配置目录
重定向到 `%TEMP%` 下的临时目录，**从不写入** `%APPDATA%\AutoSwitching\config.json`。

不要改回「备份真实配置 → 改写 → `finally` 还原」的写法。`finally` 只在正常结束
或终止性错误时执行；进程被强杀、终端被关闭、机器断电时它不会跑，此时真实配置
已被删除或写成测试数据，备份躺在 `%TEMP%` 里一个随机文件名下，用户根本找不到。

新增需要读写配置的测试时，同样走 `AUTOSWITCHING_CONFIG_DIR` 隔离。

## 脚本文件的编码：`run-tests.ps1` 必须保留 BOM

`tests/run-tests.ps1` 里含有中文，它**必须**保存为「UTF-8 with BOM」。

原因：Windows PowerShell 5.1 在读取没有 BOM 的 `.ps1` 时，会按系统 ANSI
代码页（简体中文系统上是 GBK）解析，文件里的 UTF-8 中文会变成乱码，
进而引发语法错误，脚本直接跑不起来。

因此：

- **不要**为了「统一编码」把 BOM 去掉，也不要让编辑器把它另存为 UTF-8 无 BOM
- 用 VS Code 保存时确认右下角显示 `UTF-8 with BOM`
- 用命令行改写该文件时显式指定 BOM：

  ```powershell
  [System.IO.File]::WriteAllText($path, $text, (New-Object System.Text.UTF8Encoding $true))
  ```

- 新增含中文的 `.ps1` 也请照此处理

## 不要引入新的运行时依赖

本项目的核心卖点是「单 exe + 零依赖」，因此：

- 不要引入需要额外分发 DLL 的 NuGet 包（例如 `System.Text.Json`）
- 配置文件读写用自带的 `Core/Json.cs`，它只实现配置需要的 JSON 子集
- 不要用 `PublishTrimmed` / NativeAOT 之类的思路，WinForms 不支持
- 新增 API 时注意 `net48` 的可用性：没有 `Math.Clamp`、`string[..n]`、
  `record struct`、`Marshal.SizeOf<T>()`、`TextBox.PlaceholderText`、
  `Application.SetDefaultFont`、`File.Move(..., overwrite)` 等

## 提交信息

用一句话说明改了什么，例如：

```
fix: 修复 Electron 应用激活后不补切的问题
docs: 补充发布单文件的命令
test: 增加中文规则的端到端用例
```
