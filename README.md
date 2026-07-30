# AYP Print-Queue

Windows用のスタンドアローン印刷キューアプリです。XLSX / XLS / XLSM / PDF をローカルで選択またはドロップし、一覧から選んだプリンターへ送信します。

- ネットワーク公開・Cloudflare Tunnel・MCPサーバーは使用しません
- ファイルとキュー状態はアプリ実行中だけ保持します
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

## 現在の制約

- キューは永続化しません。アプリ終了で消えます。
- 印刷ジョブの物理完了・紙切れ・プリンターエラーは追跡しません。Windowsの印刷キューで確認します。
- Excelの印刷可否は、Windowsに関連付けられたアプリの `printto` 動詞に依存します。

この範囲でローカル利用の最小版は成立します。印刷ジョブ監視や部数設定が必要になった時点で追加します。
