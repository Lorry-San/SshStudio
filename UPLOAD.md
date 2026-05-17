# 上传和发布说明

这份说明用于把 SshStudio 源码上传到 GitHub，并打包一个可分发的 Windows EXE。

## 上传前检查

确认仓库里不要包含这些内容：

- `bin/`
- `obj/`
- `publish/`
- `.vs/`
- `*.user`
- `*.pdb`
- `*.exe`
- `%APPDATA%\SshStudio` 下的任何文件

当前 `.gitignore` 已经排除了常见编译产物和可执行文件。

## 初始化 Git 仓库

```powershell
cd "G:\Documents\New project 2\github\SshStudio"
git init
git add .
git commit -m "Initial SshStudio source"
```

## 关联 GitHub 仓库

先在 GitHub 网页创建一个空仓库，然后执行：

```powershell
git branch -M main
git remote add origin https://github.com/<your-name>/<repo-name>.git
git push -u origin main
```

把 `<your-name>` 和 `<repo-name>` 换成你的 GitHub 用户名和仓库名。

## 本地编译检查

```powershell
dotnet build SshStudio.csproj -c Release
```

期望结果：

```text
0 个错误
0 个警告
```

## 打包 EXE

```powershell
dotnet publish SshStudio.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o publish\SshStudio-win-x64
```

发布结果：

```text
publish\SshStudio-win-x64\SshStudio.exe
```

## 发 GitHub Release

1. 打开 GitHub 仓库页面。
2. 进入 `Releases`。
3. 点击 `Draft a new release`。
4. Tag 建议使用 `v0.1.0`。
5. 上传 `publish\SshStudio-win-x64\SshStudio.exe`。
6. Release notes 可以写主要功能和已知限制。

## 推荐 Release Notes

```text
SshStudio v0.1.0

- SSH 主机管理
- 交互式 PTY 终端
- SFTP 上传、下载、重命名
- CPU、内存、带宽监控
- AI 助手和命令审核执行
- Markdown 渲染和上下文记忆
- Windows 当前用户加密保存 SSH 密码和 API Key
```

## 同步主开发目录到 GitHub 目录

如果你继续在 `csharp\SshStudio` 目录开发，可以用下面命令同步到 GitHub 目录：

```powershell
robocopy "G:\Documents\New project 2\csharp\SshStudio" "G:\Documents\New project 2\github\SshStudio" /E /XD bin obj /XF *.user *.pdb
```

`robocopy` 返回码 `0` 到 `7` 都可以视为成功。
