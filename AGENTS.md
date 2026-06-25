# AGENTS.md - Print Queue App

## プロジェクト概要
社用ドキュメント印刷キュー管理アプリ。WinUI MCP サーバー連携でリモート印刷制御。

## 技術スタック
- Python 3.13
- WinUI MCP Server
- FastAPI / HTTP

## ビルド・実行コマンド
```bash
# 依存関係インストール
pip install -r requirements.txt

# アプリ起動
python3 run_app.py

# テスト
python3 test_server.py
```

## ファイル構造
- `run_app.py` - アプリエントリーポイント
- `src/` - アプリソースコード
- `winui_mcp_server.py` - WinUI MCP サーバー
- `winui_bridge.py` - WinUI ブリッジ
- `WinUIMCPServer/` - MCP サーバーモジュール

## コードスタイル
- ruff 使用（ruff.toml 参照）
- 行長：120 文字
- インデント：スペース 4 個