# PrintQueueApp - 社用ドキュメント印刷キュー管理

## 1. Concept & Vision

 empresarin のための「あとで印刷」キューアプリ。ファイルをドロップしてキューに追加、WinUI越しにGUIでステータス管理、社用プリン夕へまとめて印刷。Mac/Linux でも Python 部分は動くが、GUI は WinUI MCPサーバ越しに Windows で描画する構成。

## 2. Architecture

```
[Python PrintQueueApp]  ← キュー管理・ファイル処理
        │ MCP (HTTP/stdio)
        ▼
[WinUI MCPサーバ]      ← ボタン・リスト・進捗WinUI描画
        │ WinRT API
        ▼
[社内プリン夕]          ← 実際の印刷
```

## 3. GUI Layout (WinUI)

```
┌──────────────────────────────────────────────┐
│ 📋 印刷キュー管理                        [−][□][×] │
├──────────────────────────────────────────────┤
│  ┌─ ドロップゾーン ─────────────────────┐    │
│  │   XLSX / XLS / PDF をここにドロップ    │    │
│  │          [📁 フォルダから追加]        │    │
│  └───────────────────────────────────────┘    │
│                                              │
│  キュー (3件)                                │
│  ┌────────────────────────────────────────┐  │
│  │ 📄 report.xlsx        [済] 準備中      │  │
│  │ 📄 invoice.xls        [□] 待機中       │  │
│  │ 📄 contract.pdf       [□] 待機中       │  │
│  └────────────────────────────────────────┘  │
│                                              │
│  [🗑 選択削除]  [🔄 全再開]  [🖨 全印刷]     │
│                                              │
│  ステータス: 待機中 (2件)                      │
└──────────────────────────────────────────────┘
```

## 4. File Support

| 形式 | ライブラリ | 備考 |
|------|-----------|------|
| XLSX | openpyxl | Excel 2007+ |
| XLS  | xlrd + xlwt | Excel 97-2003 |
| PDF  | PyMuPDF (fitz) | そのまま印刷 or 画像化 |

## 5. MCP Tools Interface (WinUI →)

```
# ウィンドウ管理
create_window(title: string, width: int, height: int) → window_id: string
set_window_layout(window_id: string, layout: dict)  # ドロップゾーン・リスト・ボタン配置

# UI要素
add_drop_zone(window_id: string, zone_id: string, label: string)
update_file_list(window_id: string, files: list[dict])  # [{name, status, size}]
add_button(window_id: string, button_id: string, label: string, icon: string)
set_status_text(window_id: string, text: string)

# イベント受信用 (MCP → App逆流)
register_callback(event_type: string, callback_id: string)

# 進捗
set_progress(window_id: string, percent: int, label: string)
```

## 6. Queue Management

```
状態: PENDING → PROCESSING → PRINTING → DONE / ERROR

各ファイル:
  - name: ファイル名
  - path: 完全パス
  - size: サイズ (bytes)
  - added_at: 追加時刻
  - status: 上述状態
  - error: エラー文字列 (あれば)
  - print_result: 印刷結果
```

## 7. Print Pipeline

```
1. PENDING: ファイル待機
2. PROCESSING: 印刷用データに変換 (XLS→XLSX, PDF→中間形式)
3. PRINTING: Windows API / lp1 コマンドで印刷
4. DONE: 完了記録
5. ERROR: エラー詳細表示
```

## 8. Local File Processing (Unix/Linux向け)

Unix/Linux では、印刷コマンドを生成して出力:
```bash
# PDF
lp -d PRINTER_NAME file.pdf

# XLSX → CSV → 印刷
python -c "import openpyxl; ..." | lp -d PRINTER_NAME
```

## 9. Acceptance Criteria

- [ ] ウィンドウがWinUI MCP経由で描画される
- [ ] ファイルドロップでキューに追加できる
- [ ] XLSX/XLS/PDF がすべて認識・キューイングされる
- [ ] 進捗表示がリアルタイムで更新される
- [ ] 印刷完了/エラーがステータス反映される
- [ ] キュー選択→削除ができる
