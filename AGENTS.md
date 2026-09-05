# AGENTS.md - AYP Print-Queue

## Project overview

Windows用のスタンドアローン印刷キューアプリ。ローカルでXLSX / XLS / XLSM / PDFを追加し、一覧から選択したWindowsプリンターへ送信する。

公開API、Cloudflare Tunnel、HTTPサーバー、MCP連携はこのプロジェクトの範囲外。

## Stack

- .NET 8 SDK / WinUI 3 (Windows App SDK); target `net8.0-windows10.0.19041.0`, runtime `win-x64`
- `Microsoft.WindowsAppSDK` 2.3.1, Windows Shell `printto` verb
- Windows 10 version 2004 (build 19041)以降を対象にする

## Build and test (Windows PowerShell)

Run from the repository root on Windows. The current runnable verification is the `Program.cs` self-test; there is no separate test project.

```powershell
cd WinUIMCPServer
dotnet restore
dotnet build -c Debug
dotnet run -c Debug -- --self-test
dotnet run -c Debug
dotnet run -c Debug -- --print-test <file-path>
```

`--self-test` must print `QueueItem self-test passed.`. `--print-test` uses the default printer and exits non-zero when submission fails; it still depends on the file association's `printto` verb.

## Distribution build

Use the complete self-contained folder; do not distribute the executable alone because WinUI native DLLs and resources are required.

```powershell
cd WinUIMCPServer
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:WindowsAppSDKSelfContained=true `
  -p:PublishSingleFile=false `
  -o ..\dist\AYP-Print-Queue-win-x64
```

ZIP `dist\AYP-Print-Queue-win-x64` as the release artifact. `bin/`, `obj/`, and `dist/` are generated and ignored.

## Source layout

- `WinUIMCPServer/Program.cs` — application entry point, `--self-test`, and `--print-test`
- `WinUIMCPServer/MainWindow.xaml(.cs)` — local WinUI queue UI, drag/drop, dialogs, accessibility status, and printer refresh
- `WinUIMCPServer/QueueItem.cs` — supported-format validation, queue persistence, bounded submission, Win32 printer catalog/health, and self-test

## Design constraints

- Keep the app local-only. Do not add a server, token, remote control, or public endpoint.
- `Submitted` means the request was accepted by Windows Shell, not physical print completion; physical status remains a printer/spooler concern.
- PDF/Excel submission depends on an associated Windows application exposing the Shell `printto` verb.
- Use native Windows capabilities before dependencies or abstractions. Keep P/Invoke and Windows printer handling in the existing catalog instead of adding a new library.
- Duplicate identity is the normalized absolute path, compared case-insensitively. Supported extensions are `.xlsx`, `.xls`, `.xlsm`, and `.pdf`.
- Normal submission is bounded to four concurrent Shell launches; the preserve-order option submits sequentially.

## Persistence, privacy, and UI behavior

- Queue state is JSON at `%LOCALAPPDATA%\\AYP\\PrintQueue\\queue.json`; save through the existing temporary-file-then-move path.
- Operation history is `%LOCALAPPDATA%\\AYP\\PrintQueue\\history.log`; it records metadata and must never store file contents.
- Restored items do not auto-submit. Preserve stored status, printer, timestamp, file size, and last-write metadata when changing snapshots.
- Submission checks for missing or changed files before the confirmation dialog; cancellation must leave the queue state unchanged.
- Preserve the existing `x:Bind`/`INotifyPropertyChanged`-free refresh pattern, multi-select actions, and `AutomationProperties.LiveSetting="Polite"` status notification unless the UI architecture is deliberately changed.
- Main UI zoom uses the native `ScrollViewer` from 80% to 160%; keep its slider, ± controls, 100% reset, and Ctrl+mouse-wheel behavior synchronized.

## Pitfalls

- Build, launch, printer enumeration, and real printing are Windows-specific; a Linux compile is not a substitute for the Windows build/self-test.
- A valid `SymbolIcon` name is required by the XAML compiler; use a valid symbol or `FontIcon` glyph rather than guessing a symbol name.
- Do not hand-edit `bin/`, `obj/`, `dist/`, `.vs/`, logs, or credential files; `.gitignore` excludes them.
- Keep `printto` limitations explicit: Shell acceptance does not prove paper output or associate a spooler job with a particular file.
