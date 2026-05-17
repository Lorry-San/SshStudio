# SshStudio

SshStudio 是一个 Windows 桌面 SSH 运维客户端，使用 C#、.NET 8 和 Avalonia 开发。它把主机管理、交互式终端、SFTP、资源监控和 AI 助手放在同一个界面里。

## 功能

- SSH 主机管理：新增、编辑、删除、保存常用主机。
- 交互式 PTY 终端：支持 nano/vim、Ctrl 组合键、窗口尺寸同步、滚轮查看历史输出。
- 终端显示：支持 ANSI 颜色、clear、中文宽字符、复制/粘贴、右侧滚动条。
- SFTP 文件管理：独立窗口，上传、下载打开、双击文件名重命名、右键菜单操作。
- 资源监控：左侧显示 CPU、内存、近 30 秒带宽趋势。
- AI 助手：支持聊天上下文、每台机器独立上下文、长期记忆、Markdown 渲染。
- AI 执行命令：AI 可以提出命令，经过审核后从中间终端执行，并读取输出继续分析。
- API 配置：支持 OpenAI Responses 风格接口，支持可选 Web Search 工具。
- 本地安全：SSH 密码、私钥密码和 API Key 使用 Windows 当前用户加密保存；查看明文时需要主密码。

## 运行环境

- Windows x64
- .NET 8 SDK，只有开发/编译时需要

普通用户可以直接运行发布后的 `SshStudio.exe`，不需要安装 .NET SDK。

## 开发运行

```powershell
dotnet run --project SshStudio.csproj -c Release
```

## 编译

```powershell
dotnet build SshStudio.csproj -c Release
```

## 发布单文件 EXE

```powershell
dotnet publish SshStudio.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\SshStudio-win-x64
```

输出文件：

```text
publish\SshStudio-win-x64\SshStudio.exe
```

## 数据保存位置

运行时数据保存在：

```text
%APPDATA%\SshStudio
```

常见文件：

- `hosts.json`
- `api-config.json`
- `chat-histories.json`
- `memories.json`
- `vault.json`

这些文件是用户本机数据，不应该提交到 GitHub。

## 安全说明

SshStudio 保存 SSH 密码、私钥密码和 API Key 时会使用 Windows DPAPI 的当前用户范围加密。也就是说，配置文件复制到其他 Windows 用户或其他机器后通常无法直接解密。

主密码用于“查看明文”时的二次确认，不会阻止正常 SSH 登录和 API 调用。忘记主密码不会影响已经在本机运行时自动解密使用，但会影响通过界面查看具体密码。

## AI 命令权限

AI 助手不会直接绕过终端执行命令。它生成命令后，会进入审核流程；通过审核后，命令会显示并发送到中间终端执行。执行结果会回传给 AI，用于继续分析。

API 配置里可以选择执行模式：

- 仅建议
- 执行前确认
- 自动执行安全命令

## 项目结构

- `Controls/`：终端控件、Markdown 渲染等自定义控件。
- `Models/`：主机、AI、SFTP、配置等数据模型。
- `Services/`：SSH/SFTP、AI API、本地状态保存、加密存储。
- `ViewModels/`：主窗口状态和操作逻辑。
- `Views/`：Avalonia 窗口和界面。

## 备注

这是一个桌面工具项目，不包含服务端组件。请不要把真实主机配置、API Key、聊天记录或 `%APPDATA%\SshStudio` 内容上传到公开仓库。
