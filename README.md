# AYP Print-Queue

Windows用のスタンドアローン印刷キューアプリです。XLSX / XLS / XLSM / PDF をローカルで選択またはドロップし、一覧から選んだプリンターへ送信します。

- ネットワーク公開・Cloudflare Tunnel・MCPサーバーは使用しません
- キュー状態は `%LOCALAPPDATA%\\AYP\\PrintQueue\\queue.json` に保存します
- 「送信済み」はWindowsへ印刷要求を渡せた状態です。物理印刷完了はプリンター側で確認してください

## Windowsでのテスト

### 前提

- Windows 10 version 2004 (build 19041) 以降
- .NET 8 SDK
- 対象ファイルを開いて印刷できる既定アプリ（PDFは通常のPDFビューア、Excel系はExcel等）

### 起動

PowerShellで実行します。

```powershell
cd WinUIMCPServer
dotnet restore
dotnet build -c Debug
dotnet run -c Debug
```

### 最小確認

```powershell
cd WinUIMCPServer
dotnet run -c Debug -- --self-test
```

期待値:

```text
QueueItem self-test passed.
```

## 配布用ビルド

.NET 8とWindows App Runtimeを同梱したwin-x64向けフォルダーを生成します。

```powershell
cd WinUIMCPServer
dotnet publish -c Release -r win-x64 --self-contained true `
  -p:WindowsAppSDKSelfContained=true `
  -p:PublishSingleFile=false `
  -o ..\dist\AYP-Print-Queue-win-x64
```

`AYP-Print-Queue-win-x64`フォルダーをZIPにして配布してください。WinUIのnative DLL群が必要なため、`PrintQueueApp.WinUI.exe`だけを取り出して配布することはできません。PDFとExcelの印刷には、対象形式のWindows Shell `printto`動詞を提供する関連付け済みアプリが必要です。

### 手動テスト手順

1. アプリを起動する。
2. PDFまたはExcelファイルを「ファイルを選択」またはドロップゾーンから追加する。
3. 選択削除・キュークリアができることを確認する。
4. プリンター一覧から **Microsoft Print to PDF** を選択する。
5. 「選択したプリンターへ送信」を押し、キューが「送信済み」になることと、Windowsの印刷ダイアログ／出力保存が起きることを確認する。
6. 同じファイルをもう一度追加し、重複として除外されることを確認する。
7. 存在しないファイルを移動または削除してから送信し、「エラーを再送」が表示されることを確認する。キャンセル時にエラー状態が維持されることも確認する。
8. 「上へ」「下へ」、プリンター「更新」、送信前確認、「順番どおりに送信」を確認する。
9. アプリを終了・再起動し、キューが復元されること、自動再送されないことを確認する。
10. プリンター状態・スプーラー件数と「操作履歴を開く」を確認する。

## 現在の機能と制約

- 同じ絶対パスの重複追加を防ぎます。
- キューは `%LOCALAPPDATA%\\AYP\\PrintQueue\\queue.json` に保存し、次回起動時に復元します。復元後に自動再送はしません。
- 選択削除、すべてクリア、エラー項目の再送、送信済み項目の明示的な再送に対応します。
- キュー項目は「上へ」「下へ」で並べ替えできます。
- 通常は最大4件の並列送信です。「順番どおりに送信」を選ぶと1件ずつ送信します。
- 送信前に件数・ファイル名・送信先を確認します。
- 選択中プリンターの状態とスプーラー上のジョブ件数を定期的に表示します。取得できるのはWindowsスプーラーの状態であり、個々のファイルの物理印刷完了との対応付けではありません。
- 操作履歴は `%LOCALAPPDATA%\\AYP\\PrintQueue\\history.log` に保存し、「操作履歴を開く」から確認できます。ファイル内容は保存しません。
- 「送信済み」はWindowsへ印刷要求を渡せた状態です。物理印刷完了、紙切れ、プリンターエラーはプリンター側でも確認してください。
- Excelの印刷可否は、Windowsに関連付けられたアプリの `printto` 動詞に依存します。

印刷ジョブと個々のファイルを確実に対応付けるには、`printto` からWindows印刷APIへ移行する追加設計が必要です。現版では、ローカル限定と既存の関連付け互換性を優先しています。
