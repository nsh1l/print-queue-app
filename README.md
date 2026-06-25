# PrintQueueApp (プリントキューアプリ)

社用ドキュメント印刷キュー管理アプリ — WinUI MCP サーバー連携

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Python 3.11+](https://img.shields.io/badge/python-3.11+-blue.svg)](https://www.python.org/downloads/)
[![.NET 8](https://img.shields.io/badge/.NET-8-purple.svg)](https://dotnet.microsoft.com/download/dotnet/8.0)

## 📋 概要

PrintQueueApp は、XLSX/XLS/PDF ファイルをドラッグ＆ドロップでキューに追加し、WinUI 3 の GUI でステータスを管理しながら社内プリンターへ印刷するためのアプリケーションです。

- **クロスプラットフォーム**: Python 部分は Windows/Mac/Linux で動作
- **WinUI 3 GUI**: Windows ネイティブのモダンなインターフェース
- **MCP プロトコル**: Model Context Protocol による柔軟なサーバー/クライアント分離
- **多形式対応**: Excel (XLSX/XLS) と PDF ファイルをサポート

## 🏗 アーキテクチャ

```
┌─────────────────────────────────────────────────────────┐
│  Python PrintQueueApp (クロスプラットフォーム)           │
│  ├── キュー管理                                         │
│  ├── ファイル処理 (openpyxl, xlrd, PyMuPDF)             │
│  └── 印刷実行 (Windows API / lp)                        │
│         │                                               │
│         │ MCP (HTTP または stdio)                       │
│         ▼                                               │
│  WinUI MCP サーバー (C# / WinUI 3)                      │
│  ├── MCP ツール → WinUI ウィジェット実装                │
│  └── GUI ウィンドウホスティング                         │
└─────────────────────────────────────────────────────────┘
```

## ✨ 主な機能

### キュー管理
- ファイルのドラッグ＆ドロップ追加
- キュー内のファイル一覧表示
- 個別/全体の印刷操作
- 印刷状況のリアルタイム表示

### サポートファイル形式
| 形式 | ライブラリ | 備考 |
|------|-----------|------|
| XLSX | openpyxl | Excel 2007+ |
| XLS  | xlrd + xlwt | Excel 97-2003 (レガシー) |
| PDF  | PyMuPDF | そのまま印刷または画像化 |

### 印刷パイプライン
```
PENDING → PROCESSING → PRINTING → DONE / ERROR
```

## 🚀 クイックスタート

### 前提条件

- **Python 3.11+**
- **.NET 8 SDK** (WinUI GUI を使用する場合)
- **Windows 10/11** (WinUI GUI の場合、または Windows 印刷 API の場合)

### インストール

```bash
# リポジトリのクローン
git clone https://github.com/nsh1l/print-queue-app.git
cd print-queue-app

# 仮想環境の作成とアクティベート
python -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate

# 依存関係のインストール
pip install -r requirements.txt
```

### 実行方法

#### GUI モード (推奨 - Windows)

```bash
# ランチャーを使用 (WinUI サーバー + Python クライアントを自動起動)
python run_app.py

# または直接
python -m src --gui
```

#### モック GUI モード (Mac/Linux/開発用)

```bash
# Windows 不要のモックサーバーを使用
python run_app.py --mock
```

#### CLI モード

```bash
# コマンドラインからファイルを指定
python run_app.py --cli report.xlsx invoice.pdf
```

### 環境変数

| 変数 | 説明 | デフォルト |
|------|------|-----------|
| `PRINT_QUEUE_MCP_URL` | MCP サーバー URL | `http://localhost:8765` |
| `DOTNET_PATH` | dotnet CLI のパス | `dotnet` |

## 🔧 MCP ツールインターフェース

WinUI サーバーは以下の MCP ツールを公開します:

| ツール | 説明 |
|--------|------|
| `winui_create_window` | メインウィンドウ作成 |
| `winui_add_drop_zone` | ファイルドロップゾーン追加 |
| `winui_update_file_list` | キュー一覧更新 |
| `winui_add_button` | ツールバーボタン追加 |
| `winui_add_label` | テキストラベル追加 |
| `winui_set_status_text` | ステータスバー更新 |
| `winui_set_progress` | 進捗バー更新 |
| `winui_poll_events` | UI イベントポーリング |

### 転送プロトコル

- **開発/テスト**: stdio (JSON-RPC over stdin/stdout)
- **本番**: HTTP POST `http://localhost:8765/mcp`

## 📁 プロジェクト構成

```
print-queue-app/
├── run_app.py              # アプリランチャー
├── winui_mcp_server.py     # WinUI MCP サーバー (Python)
├── winui_bridge.py         # WinUI ブリッジ (Windows ネイティブ)
├── requirements.txt        # Python 依存関係
├── src/
│   ├── __main__.py        # エントリーポイント
│   ├── main.py            # メインアプリケーション
│   ├── queue_item.py      # キューアイテム定義
│   ├── file_processor.py  # ファイル処理
│   ├── print_engine.py    # 印刷エンジン
│   ├── winui_client.py    # WinUI クライアント
│   └── mock_mcp_server.py # モック MCP サーバー
└── WinUIMCPServer/
    ├── Program.cs         # C# エントリーポイント
    ├── MCPServer.cs       # MCP サーバー実装
    ├── MainWindow.xaml.cs # メインウィンドウ
    └── PrintQueueApp.WinUI.csproj
```

## 🧪 開発

### テスト

```bash
# サーバー単体テスト
python test_server.py
```

### リモート接続

リモートサーバーへの接続設定は `CONNECTION.md` を参照してください。

```bash
# リモートサーバーに接続
python -m src --mock --url http://print.soichi.ro/ --token <token>
```

## ⚠️ 注意事項

- **WinUI GUI** を使用する場合は Windows 環境が必要です
- **Mac/Linux** では `--mock` または `--cli` モードを使用してください
- 印刷機能は Windows プリンタードライバーに依存します
- Unix/Linux では `lp` コマンドを使用した印刷が可能です

## 📄 ライセンス

MIT License

## 🤝 貢献

Issue や Pull Request を歓迎します！

## 📞 サポート

ご質問や問題がありましたら、GitHub Issues までお知らせください。
