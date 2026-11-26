# 贡献前请先看一眼

## 环境

- Windows 10 / 11
- .NET 8 SDK

## 构建与验证

提交前请确保干净构建通过、且不引入新的警告：

```powershell
dotnet build AutoSwitching.sln -c Release
```

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
- 改完请至少跑一遍 `tests/run-both.ps1`（中/英两个方向）

## 提交信息

用一句话说明改了什么，例如：

```
fix: 修复 Electron 应用激活后不补切的问题
docs: 补充发布单文件的命令
test: 增加中文规则的端到端用例
```
