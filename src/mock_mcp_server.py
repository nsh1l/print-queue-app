"""
Mock WinUI MCP server for development/testing on non-Windows systems.
Simulates the MCP tools as an HTTP server with a live web preview UI.

Usage:
    python mock_mcp_server.py                    # localhost only
    python mock_mcp_server.py --bind 0.0.0.0    # remote accessible
    python mock_mcp_server.py --port 8765 --token SECRET_KEY
"""
import argparse
import json
import http.server
import socketserver
import hashlib
import hmac
import os


class MockMCPServer:
    """Simulates WinUI MCP tools as an HTTP server with a live HTML preview."""

    def __init__(self, port: int = 8765, bind: str = "localhost", token: str = ""):
        self.port = port
        self.bind = bind
        self.token = token
        self.state = {
            "title": "印刷キュー管理",
            "files": [],
            "status": "待機中 (0件)",
            "progress": 0,
            "progress_label": "",
        }
        self.events: list[dict] = []
        self._running = False

    # ── MCP Tool Handlers ──────────────────────────────────────────────

    def _handle_mcp(self, method: str, params: dict) -> dict:
        if method == "create_window":
            self.state["title"] = params.get("title", "印刷キュー管理")
            return {"window_id": "main"}

        elif method == "update_file_list":
            self.state["files"] = params.get("files", [])
            return {"ok": True}

        elif method == "set_status_text":
            self.state["status"] = params.get("text", "")
            return {"ok": True}

        elif method == "set_progress":
            self.state["progress"] = params.get("percent", 0)
            self.state["progress_label"] = params.get("label", "")
            return {"ok": True}

        elif method == "add_drop_zone":
            self.state["drop_zone_label"] = params.get("label", "")
            return {"ok": True}

        elif method == "add_label":
            return {"ok": True}

        elif method == "add_button":
            return {"ok": True}

        elif method == "register_callback":
            return {"ok": True}

        elif method == "poll_events":
            events = self.events[:]
            self.events.clear()
            return {"events": events}

        elif method == "clear_window":
            self.state["files"] = []
            return {"ok": True}

        elif method == "close_window":
            self._running = False
            return {"ok": True}

        elif method == "initialize":
            return {
                "protocolVersion": "2024-11-05",
                "capabilities": {"tools": {}},
                "serverInfo": {"name": "winui-mcp", "version": "1.0.0"},
            }

        elif method == "tools/list":
            return {"tools": []}

        else:
            return {"error": f"Unknown method: {method}"}

    def _build_html(self) -> bytes:
        files_html = ""
        for f in self.state.get("files", []):
            icon = {"PENDING": "☐", "PROCESSING": "⚙", "PRINTING": "🖨", "DONE": "✅", "ERROR": "❌"}.get(
                f.get("status", "PENDING"), "☐"
            )
            sel = "✅" if f.get("selected") else "☐"
            err = f" <span style='color:#ef5350'>{f.get('error','')}</span>" if f.get("error") else ""
            files_html += f"""
            <tr>
                <td>{sel}</td>
                <td>{f.get('name','')}{err}</td>
                <td>{f.get('size','')}</td>
                <td>{icon}</td>
            </tr>"""

        if not files_html:
            files_html = '<tr><td colspan="4" style="text-align:center;color:#666">ファイルがありません</td></tr>'

        progress_pct = self.state.get("progress", 0)
        progress_label = self.state.get("progress_label", "")

        return f"""<!DOCTYPE html>
<html lang="ja">
<head>
<meta charset="UTF-8">
<title>{self.state.get('title','印刷キュー')}</title>
<style>
  * {{ box-sizing: border-box; }}
  body {{ font-family: 'Segoe UI', 'Meiryo', sans-serif; margin: 0; background: #1e1e1e; color: #ddd; }}
  h1 {{ color: #4fc3f7; margin: 0; font-size: 18px; padding: 12px 16px; background: #2a2a2a; }}
  .drop-zone {{
    margin: 16px; border: 3px dashed #4fc3f7; border-radius: 12px;
    padding: 24px; text-align: center; background: #252525;
  }}
  .drop-zone p {{ margin: 0 0 8px; color: #4fc3f7; }}
  .drop-zone small {{ color: #888; }}
  table {{ width: 100%; border-collapse: collapse; margin: 0 16px; }}
  th, td {{ padding: 8px 12px; border-bottom: 1px solid #333; text-align: left; font-size: 13px; }}
  th {{ background: #2a2a2a; color: #aaa; }}
  .progress-wrap {{ margin: 12px 16px; }}
  .progress-bar {{ background: #333; border-radius: 8px; height: 20px; overflow: hidden; }}
  .progress-fill {{ background: #4fc3f7; height: 100%; transition: width 0.3s; border-radius: 8px; }}
  .progress-label {{ font-size: 11px; color: #aaa; margin-top: 4px; }}
  .status {{ color: #81c784; margin: 8px 16px; font-size: 13px; }}
  .btn-bar {{ padding: 8px 16px 12px; display: flex; gap: 8px; }}
  button {{
    padding: 7px 14px; border: none; border-radius: 6px; cursor: pointer;
    font-size: 13px;
  }}
  .btn-print {{ background: #4fc3f7; color: #1e1e1e; }}
  .btn-clear {{ background: #ef5350; color: white; }}
  .btn-select {{ background: #ffca28; color: #1e1e1e; }}
  .btn-refresh {{ background: #555; color: #ccc; margin-left: auto; }}
</style>
</head>
<body>
<h1>📋 {self.state.get('title','印刷キュー管理')}</h1>

<div class="drop-zone">
  <p>📂 XLSX / XLS / PDF をここにドロップ</p>
  <small>または、下のボタンからファイルを選択</small>
</div>

<div class="progress-wrap">
  <div class="progress-bar"><div class="progress-fill" style="width:{progress_pct}%"></div></div>
  <div class="progress-label">{progress_label}</div>
</div>

<div class="status">{self.state.get('status','')}</div>

<div class="btn-bar">
  <button class="btn-select" onclick="fetch('/event/select_all',{{method:'POST'}}).then(()=>location.reload())">☑ 全選択</button>
  <button class="btn-clear" onclick="fetch('/event/clear',{{method:'POST'}}).then(()=>location.reload())">🗑 クリア</button>
  <button class="btn-print" onclick="fetch('/event/print_all',{{method:'POST'}}).then(()=>location.reload())">🖨 全印刷</button>
  <button class="btn-refresh" onclick="location.reload()">🔄 更新</button>
</div>

<table>
  <thead><tr><th></th><th>ファイル名</th><th>サイズ</th><th>状態</th></tr></thead>
  <tbody>{files_html}</tbody>
</table>
</body>
</html>""".encode("utf-8")

    # ── HTTP Server ───────────────────────────────────────────────────

    class Handler(http.server.BaseHTTPRequestHandler):
        server: "MockMCPServer"

        def log_message(self, fmt, *args):
            pass  # Silent

        def _check_auth(self) -> bool:
            """Validate HMAC token from X-Token header."""
            token = self.server._mock.token
            if not token:
                return True
            provided = self.headers.get("X-Token", "")
            if not provided:
                return False
            expected = hmac.new(
                token.encode(),
                self.path.encode(),
                hashlib.sha256
            ).hexdigest()
            return hmac.compare_digest(provided, expected)

        def _auth_error(self):
            self.send_response(401)
            self.send_header("Content-Type", "application/json")
            self.end_headers()
            self.wfile.write(b'{"error": "Unauthorized"}')

        def do_GET(self):
            if not self._check_auth():
                self._auth_error()
                return
            if self.path == "/" or self.path == "/index.html":
                self.send_response(200)
                self.send_header("Content-Type", "text/html; charset=utf-8")
                self.send_header("Content-Length", len(self.server._mock._build_html()))
                self.end_headers()
                self.wfile.write(self.server._mock._build_html())

            elif self.path == "/state":
                body = json.dumps(self.server._mock.state, ensure_ascii=False).encode()
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.send_header("Content-Length", len(body))
                self.end_headers()
                self.wfile.write(body)

            elif self.path.startswith("/event/"):
                event = self.path[len("/event/"):]
                if event == "select_all":
                    self.server._mock.events.append({"type": "button_click", "data": {"button_id": "btn_select_all"}})
                elif event == "clear":
                    self.server._mock.events.append({"type": "button_click", "data": {"button_id": "btn_clear_queue"}})
                elif event == "print_all":
                    self.server._mock.events.append({"type": "button_click", "data": {"button_id": "btn_print_all"}})
                self.send_response(200)
                self.send_header("Content-Type", "application/json")
                self.end_headers()
                self.wfile.write(b'{"ok":true}')

            else:
                self.send_response(404)
                self.end_headers()

        def do_POST(self):
            if not self._check_auth():
                self._auth_error()
                return
            if self.path != "/mcp":
                self.send_response(404)
                self.end_headers()
                return

            length = int(self.headers.get("Content-Length", 0))
            body = self.rfile.read(length)

            try:
                req = json.loads(body)
            except Exception:
                self.send_error(400, "Invalid JSON")
                return

            method = req.get("method", "")
            params = req.get("params", {})
            id_ = req.get("id", 1)

            result = self.server._handle_mcp(method, params)

            resp = {"jsonrpc": "2.0", "id": id_, "result": result}
            body_out = json.dumps(resp, ensure_ascii=False).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", len(body_out))
            self.end_headers()
            self.wfile.write(body_out)

        def do_OPTIONS(self):
            self.send_response(200)
            self.send_header("Access-Control-Allow-Origin", "*")
            self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
            self.send_header("Access-Control-Allow-Headers", "Content-Type")
            self.end_headers()

    class ThreadedHTTPServer(socketserver.ThreadingMixIn, http.server.HTTPServer):
        daemon_threads = True
        allow_reuse_address = True

    def start(self):
        self._running = True
        self._server = self.ThreadedHTTPServer((self.bind, self.port), self.Handler)
        self._server._mock = self  # type: ignore
        self._server._build_html = self._build_html  # type: ignore
        self._server._handle_mcp = self._handle_mcp    # type: ignore
        print(f"Mock WinUI MCP server running at http://{self.bind}:{self.port}", flush=True)
        if self.bind == "0.0.0.0":
            import socket
            try:
                hostname = socket.gethostname()
                local_ip = socket.gethostbyname(hostname)
                print(f"  →  LAN:       http://{local_ip}:{self.port}", flush=True)
            except Exception:
                pass
        if self.token:
            print(f"  →  Token: {self.token[:8]}... (HMAC-SHA256, pass via X-Token header)", flush=True)
        while self._running:
            self._server.handle_request()

    def stop(self):
        self._running = False
        if hasattr(self, "_server"):
            self._server.shutdown()


if __name__ == "__main__":
    ap = argparse.ArgumentParser(description="Mock WinUI MCP Server")
    ap.add_argument("--bind", default="localhost", help="Bind address (default: localhost)")
    ap.add_argument("--port", type=int, default=8765, help="Port (default: 8765)")
    ap.add_argument("--token", default="", help="HMAC token for auth (optional)")
    args = ap.parse_args()

    server = MockMCPServer(port=args.port, bind=args.bind, token=args.token)
    try:
        server.start()
    except KeyboardInterrupt:
        server.stop()
