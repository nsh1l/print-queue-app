# PrintQueueApp - AI Assistant Guide

## プロジェクト概要

社用ドキュメント印刷キュー管理アプリ — WinUI MCP サーバー連携

**言語**: Python + C# (.NET 8)  
**主要技術**: WinUI 3, MCP Protocol, openpyxl, PyMuPDF

## ビルドコマンド

```bash
# 仮想環境の作成とアクティベート
python -m venv .venv
source .venv/bin/activate  # Windows: .venv\Scripts\activate

# 依存関係のインストール
pip install -r requirements.txt

# GUI モード (推奨 - Windows)
python run_app.py

# モック GUI モード (Mac/Linux/開発用)
python run_app.py --mock

# CLI モード
python run_app.py --cli report.xlsx invoice.pdf
```

## ファイル構造

```
print-queue-app/
├── run_app.py              # アプリランチャー
├── winui_mcp_server.py     # WinUI MCP サーバー (Python)
├── winui_bridge.py         # WinUI ブリッジ (Windows ネイティブ)
├── src/
│   ├── main.py            # メインアプリケーション
│   ├── queue_item.py      # キューアイテム定義
│   ├── file_processor.py  # ファイル処理
│   ├── print_engine.py    # 印刷エンジン
│   └── winui_client.py    # WinUI クライアント
└── WinUIMCPServer/
    ├── Program.cs         # C# エントリーポイント
    ├── MCPServer.cs       # MCP サーバー実装
    └── MainWindow.xaml.cs # メインウィンドウ
```

## 主要機能

- クロスプラットフォーム: Python 部分は Windows/Mac/Linux で動作
- WinUI 3 GUI: Windows ネイティブのモダンなインターフェース
- MCP プロトコル: Model Context Protocol による柔軟なサーバー/クライアント分離
- 多形式対応: Excel (XLSX/XLS) と PDF ファイルをサポート

## 環境変数

| 変数 | 説明 | デフォルト |
|------|------|----------|
| `PRINT_QUEUE_MCP_URL` | MCP サーバー URL | `http://localhost:8765` |
| `DOTNET_PATH` | dotnet CLI のパス | `dotnet` |

## 注意事項

- **WinUI GUI** を使用する場合は Windows 環境が必要
- **Mac/Linux** では `--mock` または `--cli` モードを使用
- 印刷機能は Windows プリンタードライバーに依存