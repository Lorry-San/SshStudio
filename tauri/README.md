# SshStudio Tauri Port

This folder is a Rust + Tauri WebView branch prototype. It keeps the current C# / Avalonia app intact and builds the future small-footprint Windows client in parallel.

## Current State

- Tauri 2 project skeleton is in place.
- The WebView UI implements the same operator layout: host list, terminal workspace, AI review panel.
- Rust commands expose typed placeholders for hosts, API config, SSH connect, command run, AI ask, and command review.
- AI command review keeps the requested local hard block only for `rm` / `rmdir`; all other automatic-execution decisions are intended to go through the independent AI review prompt.
- Frontend TypeScript/Vite build has been verified with `cmd /c npm run build`.
- Rust/Tauri build has not been verified in this workspace because Rust/Cargo is not installed.

This is not yet feature-complete. Real SSH PTY/SFTP, encrypted vault migration, OpenAI streaming, and import/export persistence still need to be wired to Rust crates.

## Required Tooling

Install:

- Rust stable: `rustup`
- Node.js 20+ or 22+
- Windows WebView2 Runtime
- Visual Studio Build Tools with MSVC C++ workload

PowerShell may block `npm.ps1`. Use either:

```powershell
cmd /c npm install
cmd /c npm run tauri dev
```

or run PowerShell with an execution policy that allows npm scripts.

## Run

```powershell
cd tauri
cmd /c npm install
cmd /c npm run tauri dev
```

## Build

```powershell
cd tauri
cmd /c npm install
cmd /c npm run tauri build
```

Expected release size after real implementation depends on dependencies:

- WebView UI + Rust backend: roughly 8-20 MB if relying on installed WebView2.
- Add SSH/SFTP/OpenAI/vault crates: likely 12-30 MB.
- Bundling WebView2 runtime separately will add much more.

## Migration Plan

1. Replace placeholder host storage with encrypted JSON vault using Rust crypto and Windows DPAPI.
2. Add real SSH PTY/SFTP using a Rust SSH crate or a native libssh2 binding.
3. Port OpenAI Responses and Chat Completions streaming.
4. Implement AI command review with the same independent `ReviewPrompt`.
5. Add config import/export compatible with the Avalonia `vault.json`, `hosts.json`, and `api-config.json` formats where possible.
6. Add release builds and compare size/startup against the Avalonia single-file release.
