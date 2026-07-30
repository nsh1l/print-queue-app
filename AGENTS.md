# AGENTS.md - Print Queue App

## Project overview

Windows用のスタンドアローン印刷キューアプリ。ローカルでXLSX / XLS / XLSM / PDFを追加し、一覧から選択したWindowsプリンターへ送信する。

公開API、Cloudflare Tunnel、HTTPサーバー、MCP連携はこのプロジェクトの範囲外。

## Stack

- .NET 8
- WinUI 3 (Windows App SDK)
- Windows Shell `printto` verb

## Build and test (Windows PowerShell)

```powershell
cd WinUIMCPServer
dotnet restore
dotnet build -c Debug
dotnet run -c Debug -- --self-test
dotnet run -c Debug
```

## Source layout

- `WinUIMCPServer/Program.cs` — application entry point
- `WinUIMCPServer/MainWindow.xaml.cs` — local WinUI queue UI
- `WinUIMCPServer/QueueItem.cs` — supported-format validation and local print submission

## Design constraints

- Keep the app local-only. Do not add a server, token, remote control, or public endpoint.
- `Submitted` means the request was accepted by Windows Shell, not physical print completion.
- Use native Windows capabilities before dependencies or abstractions.

IMPORTANT: Do not write overly defensive code. Always prefer simplicity over pathological complexity.
