# Print Queue App — Minimal Local Specification

## Goal

Windowsでローカルの文書を印刷待ちにまとめ、既定のプリンターへ送信するスタンドアローンアプリ。

## Supported files

- `.xlsx`
- `.xls`
- `.xlsm`
- `.pdf`

## Behavior

1. ユーザーはファイル選択またはドラッグ&ドロップで対応ファイルをキューに追加できる。
2. 未対応形式はキューに追加しない。
3. ユーザーは選択項目を削除、またはキューを全消去できる。
4. 「既定のプリンターへ送信」は、待機中の各ファイルにWindows Shellの `print` 動詞を使う。
5. 成功時は `送信済み`、開始できない場合は `エラー` を表示する。

## Non-goals

- 公開URL、HTTP API、Cloudflare Tunnel、MCP、リモート操作
- キューの永続化
- プリンター選択、部数、印刷設定
- 物理印刷完了や紙切れの監視

## Acceptance criteria

- [ ] Windows上で `dotnet build -c Debug` が成功する。
- [ ] `dotnet run -c Debug -- --self-test` が成功する。
- [ ] ファイル選択・ドロップで対応形式をキューに追加できる。
- [ ] 選択削除とキュー全消去ができる。
- [ ] Microsoft Print to PDFで1ファイルを送信し、送信済み表示を確認できる。
