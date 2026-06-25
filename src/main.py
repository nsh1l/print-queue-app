"""
PrintQueueApp - 印刷キュー管理アプリ
WinUI MCPサーバ接続: HTTP (mock/dev) または stdio (C# WinUI)
"""
import asyncio
import json
import os
import subprocess
import sys
import threading
import time
import uuid
from datetime import datetime
from pathlib import Path
from typing import Optional, TextIO

import click

from .queue_item import QueueItem, FileStatus
from .file_processor import detect_file_type
from .print_engine import PrintEngine


class StdioMCPClient:
    """MCP client over stdio (for C# WinUI server)."""

    def __init__(self, process: subprocess.Popen):
        self._proc = process
        self._stdout: TextIO = process.stdout  # type: ignore
        self._id = 0
        self._lock = threading.Lock()
        self._pending: dict[int, asyncio.Future] = {}
        self._read_thread = threading.Thread(target=self._read_loop, daemon=True)
        self._read_thread.start()

    def _read_loop(self):
        for line in self._stdout:
            if not line:
                break
            try:
                resp = json.loads(line)
                rid = resp.get("id", 0)
                with self._lock:
                    if rid in self._pending:
                        fut = self._pending.pop(rid)
                        if "error" in resp:
                            fut.set_exception(RuntimeError(resp["error"]))
                        else:
                            fut.set_result(resp.get("result", {}))
            except Exception:
                pass

    def _call(self, method: str, params: dict = {}) -> dict:
        self._id += 1
        rid = self._id
        fut: asyncio.Future = asyncio.get_event_loop().create_future()
        with self._lock:
            self._pending[rid] = fut
        payload = {"jsonrpc": "2.0", "id": rid, "method": method, "params": params}
        self._proc.stdin.write(json.dumps(payload) + "\n")  # type: ignore
        self._proc.stdin.flush()  # type: ignore
        return fut.result(timeout=30)

    def create_window(self, title: str, width: int = 800, height: int = 600) -> str:
        result = self._call("create_window", {"title": title, "width": width, "height": height})
        return result.get("window_id", "main")

    def update_file_list(self, files: list[dict]) -> None:
        self._call("update_file_list", {"window_id": "main", "files": files})

    def set_status_text(self, text: str) -> None:
        self._call("set_status_text", {"window_id": "main", "text": text})

    def set_progress(self, percent: int, label: str = "") -> None:
        self._call("set_progress", {"window_id": "main", "percent": percent, "label": label})

    def add_drop_zone(self, zone_id: str, label: str) -> None:
        self._call("add_drop_zone", {"window_id": "main", "zone_id": zone_id, "label": label})

    def add_button(self, button_id: str, label: str, icon: str = "") -> None:
        self._call("add_button", {"window_id": "main", "button_id": button_id, "label": label, "icon": icon})

    def add_label(self, label_id: str, text: str) -> None:
        self._call("add_label", {"window_id": "main", "label_id": label_id, "text": text})

    def register_callback(self, event_type: str, callback_id: str) -> None:
        self._call("register_callback", {"window_id": "main", "event_type": event_type, "callback_id": callback_id})

    def poll_events(self) -> list[dict]:
        result = self._call("poll_events", {"window_id": "main"})
        return result.get("events", [])

    def clear_window(self) -> None:
        self._call("clear_window", {"window_id": "main"})

    def close_window(self) -> None:
        self._call("close_window", {"window_id": "main"})

    def close(self) -> None:
        self._proc.terminate()


class HTTPWinUIMCPClient:
    """MCP client over HTTP (for mock server)."""

    def __init__(self, base_url: str = "http://localhost:8765", token: str = ""):
        import httpx
        self._client = httpx.Client(base_url=base_url, timeout=30)
        self._token = token
        self._id = 0

    def _call(self, method: str, params: dict = {}) -> dict:
        import hashlib, hmac
        self._id += 1
        headers = {"Content-Type": "application/json"}
        if self._token:
            sig = hmac.new(
                self._token.encode(),
                f"/mcp".encode(),
                hashlib.sha256
            ).hexdigest()
            headers["X-Token"] = sig
        resp = self._client.post(
            "/mcp",
            json={"jsonrpc": "2.0", "id": self._id, "method": method, "params": params},
            headers=headers,
        )
        resp.raise_for_status()
        data = resp.json()
        if "error" in data:
            raise RuntimeError(f"MCP error: {data['error']}")
        return data.get("result", {})

    def create_window(self, title: str, width: int = 800, height: int = 600) -> str:
        result = self._call("create_window", {"title": title, "width": width, "height": height})
        return result.get("window_id", "main")

    def update_file_list(self, files: list[dict]) -> None:
        self._call("update_file_list", {"window_id": "main", "files": files})

    def set_status_text(self, text: str) -> None:
        self._call("set_status_text", {"window_id": "main", "text": text})

    def set_progress(self, percent: int, label: str = "") -> None:
        self._call("set_progress", {"window_id": "main", "percent": percent, "label": label})

    def add_drop_zone(self, zone_id: str, label: str) -> None:
        self._call("add_drop_zone", {"window_id": "main", "zone_id": zone_id, "label": label})

    def add_button(self, button_id: str, label: str, icon: str = "") -> None:
        self._call("add_button", {"window_id": "main", "button_id": button_id, "label": label, "icon": icon})

    def add_label(self, label_id: str, text: str) -> None:
        self._call("add_label", {"window_id": "main", "label_id": label_id, "text": text})

    def register_callback(self, event_type: str, callback_id: str) -> None:
        self._call("register_callback", {"window_id": "main", "event_type": event_type, "callback_id": callback_id})

    def poll_events(self) -> list[dict]:
        result = self._call("poll_events", {"window_id": "main"})
        return result.get("events", [])

    def clear_window(self) -> None:
        self._call("clear_window", {"window_id": "main"})

    def close_window(self) -> None:
        self._call("close_window", {"window_id": "main"})

    def close(self) -> None:
        self._client.close()


class PrintQueueApp:
    """Main application controller."""

    def __init__(self, mcp_client):
        self.queue: list[QueueItem] = []
        self.mcp = mcp_client
        self.engine = PrintEngine()
        self._running = False
        self._selected_ids: set[str] = set()

    # ── Queue Operations ────────────────────────────────────────────

    def add_files(self, paths: list[Path]) -> list[QueueItem]:
        added = []
        for path in paths:
            ft = detect_file_type(path)
            if ft is None:
                click.echo(f"Unsupported: {path}", err=True)
                continue
            item = QueueItem(
                id=str(uuid.uuid4())[:8],
                name=path.name,
                path=path,
                size=path.stat().st_size,
                added_at=datetime.now(),
            )
            self.queue.append(item)
            added.append(item)
        return added

    def remove_selected(self) -> int:
        before = len(self.queue)
        self.queue = [q for q in self.queue if q.id not in self._selected_ids]
        self._selected_ids.clear()
        return before - len(self.queue)

    def clear_queue(self) -> int:
        count = len(self.queue)
        self.queue.clear()
        self._selected_ids.clear()
        return count

    def toggle_select(self, item_id: str) -> None:
        if item_id in self._selected_ids:
            self._selected_ids.discard(item_id)
        else:
            self._selected_ids.add(item_id)

    def select_all(self) -> None:
        self._selected_ids = {q.id for q in self.queue}

    def deselect_all(self) -> None:
        self._selected_ids.clear()

    def get_pending_items(self) -> list[QueueItem]:
        return [q for q in self.queue if q.status == FileStatus.PENDING]

    def get_selected_items(self) -> list[QueueItem]:
        return [q for q in self.queue if q.id in self._selected_ids]

    # ── Status helpers ──────────────────────────────────────────────

    def status_summary(self) -> str:
        counts = {}
        for item in self.queue:
            counts[item.status.value] = counts.get(item.status.value, 0) + 1
        parts = [f"{v}件{FileStatus(k).name}" for k, v in counts.items()]
        return ", ".join(parts) if parts else "キュー空虚"

    # ── GUI Sync ───────────────────────────────────────────────────

    def gui_file_list(self) -> list[dict]:
        return [
            {
                "id": q.id,
                "name": q.name,
                "size": q.size_str,
                "status": q.status.name,
                "selected": q.id in self._selected_ids,
                "error": q.error,
            }
            for q in self.queue
        ]

    async def sync_gui(self) -> None:
        try:
            self.mcp.update_file_list(self.gui_file_list())
            self.mcp.set_status_text(self.status_summary())
        except Exception as e:
            click.echo(f"GUI sync error: {e}", err=True)

    # ── Print Pipeline ──────────────────────────────────────────────

    async def print_all(self) -> None:
        pending = self.get_pending_items()
        if not pending:
            await self.sync_gui()
            return

        total = len(pending)
        for i, item in enumerate(pending, 1):
            item.status = FileStatus.PROCESSING
            await self.sync_gui()
            self.mcp.set_progress(
                int((i / total) * 100),
                f"処理中: {item.name}",
            )
            success, msg = await self._print_item(item)
            if success:
                item.status = FileStatus.DONE
                item.print_result = msg
            else:
                item.status = FileStatus.ERROR
                item.error = msg
            await self.sync_gui()

        self.mcp.set_progress(100, "完了")
        await asyncio.sleep(1)
        self.mcp.set_progress(0, "")

    async def _print_item(self, item: QueueItem) -> tuple[bool, str]:
        loop = asyncio.get_event_loop()
        return await loop.run_in_executor(None, self._print_item_sync, item)

    def _print_item_sync(self, item: QueueItem) -> tuple[bool, str]:
        item.status = FileStatus.PRINTING
        ft = detect_file_type(item.path)
        if ft is None:
            return False, "Unknown file type"
        return self.engine.print_file(item.path, ft)


async def run_gui(mcp_client) -> int:
    """Run with WinUI/Mock MCP GUI."""
    app = PrintQueueApp(mcp_client)
    click.echo("🎨 印刷キュー管理 GUI 起動中...")

    try:
        window_id = app.mcp.create_window(title="印刷キュー管理", width=700, height=650)
        click.echo(f"Window created: {window_id}")
    except Exception as e:
        click.echo(f"GUI setup error: {e}", err=True)
        return 1

    # Register callbacks
    app.mcp.register_callback("file_drop", "on_file_drop")
    app.mcp.register_callback("button_click", "on_button_click")

    # Build initial layout
    app.mcp.add_label("title", "📋 印刷キュー管理")
    app.mcp.add_drop_zone("dropzone", "XLSX / XLS / PDF をここにドロップ")
    app.mcp.add_label("status", "待機中 (0件)")
    app.mcp.add_button("btn_select_all", "☑ 全選択")
    app.mcp.add_button("btn_clear_queue", "🗑 キューをクリア")
    app.mcp.add_button("btn_print_all", "🖨 全印刷")

    await app.sync_gui()

    # Main event loop
    app._running = True
    last_sync = time.time()

    while app._running:
        try:
            events = app.mcp.poll_events()
        except Exception:
            events = []

        for event in events:
            et = event.get("type")
            data = event.get("data", {})

            if et == "file_drop":
                paths = [Path(p) for p in data.get("paths", [])]
                added = app.add_files(paths)
                click.echo(f"Added {len(added)} file(s)")
                await app.sync_gui()

            elif et == "button_click":
                btn_id = data.get("button_id")
                if btn_id == "btn_select_all":
                    app.select_all()
                    await app.sync_gui()
                elif btn_id == "btn_clear_queue":
                    count = app.clear_queue()
                    click.echo(f"Cleared {count} item(s)")
                    await app.sync_gui()
                elif btn_id == "btn_print_all":
                    await app.print_all()

            elif et == "file_select":
                item_id = data.get("id")
                if item_id:
                    app.toggle_select(item_id)
                    await app.sync_gui()

        if time.time() - last_sync > 2:
            await app.sync_gui()
            last_sync = time.time()

        await asyncio.sleep(0.5)

    app.mcp.close_window()
    mcp_client.close()
    return 0


async def run_cli(files: list[str]) -> int:
    """Run in CLI mode (no GUI)."""
    app = PrintQueueApp(None)
    click.echo("📋 印刷キュー管理 (CLIモード)")

    if files:
        paths = [Path(f) for f in files]
        added = app.add_files(paths)
        click.echo(f"追加: {len(added)} 件")
        for item in app.queue:
            click.echo(f"  {item.name} ({item.size_str})")

    printers = app.engine.list_printers()
    if printers:
        click.echo(f"\n利用可能プリンダ: {', '.join(printers)}")
    else:
        click.echo("\nプリンダが見つかりません")
    return 0


@click.command()
@click.option("--gui", "mode", flag_value="gui", default=True, help="GUIモード (default)")
@click.option("--cli", "mode", flag_value="cli", help="CLIモード")
@click.option("--mock", is_flag=True, help="Mock MCPサーバを使用 (Windows不要)")
@click.option("--stdio", is_flag=True, help="stdioでC# WinUI MCPサーバに接続")
@click.option("--token", default="", help="MCPサーバのHMACトークン")
@click.option("--url", "mcp_url", default="http://localhost:8765", help="MCPサーバURL (--mock時)")
@click.argument("files", nargs=-1, type=click.Path(exists=False))
def main(mode, mock, stdio, token, mcp_url, files):
    """
    Print queue manager for XLSX / XLS / PDF files.

    Examples:
      python -m src --gui                    # GUI (mock server on non-Windows)
      python -m src --cli report.xlsx        # CLI
      python run_app.py --mock               # Launcher with mock GUI
    """
    files = list(files) if files else []

    loop = asyncio.new_event_loop()
    asyncio.set_event_loop(loop)

    try:
        if mode == "cli" or stdio:
            exit_code = loop.run_until_complete(run_cli(files))
        else:
            # GUI mode
            mcp_url = os.environ.get("PRINT_QUEUE_MCP_URL", "http://localhost:8765")

            if stdio:
                # Connect to C# WinUI MCP server via stdio
                dotnet = os.environ.get("DOTNET_PATH", "dotnet")
                csproj = Path(__file__).parent.parent / "WinUIMCPServer" / "PrintQueueApp.WinUI.csproj"
                click.echo(f"Starting WinUI MCP server (dotnet)...")
                proc = subprocess.Popen(
                    [dotnet, "run", "--project", str(csproj), "--", "--mcp"],
                    stdin=subprocess.PIPE,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.PIPE,
                    text=True,
                )
                mcp_client = StdioMCPClient(proc)
                time.sleep(2)
            elif mock:
                mcp_client = HTTPWinUIMCPClient(mcp_url, token=token)
            else:
                # Try stdio first (C# server), fall back to HTTP (mock)
                dotnet = os.environ.get("DOTNET_PATH", "dotnet")
                csproj = Path(__file__).parent.parent / "WinUIMCPServer" / "PrintQueueApp.WinUI.csproj"
                try:
                    proc = subprocess.Popen(
                        [dotnet, "run", "--project", str(csproj), "--", "--mcp"],
                        stdin=subprocess.PIPE,
                        stdout=subprocess.PIPE,
                        stderr=subprocess.PIPE,
                        text=True,
                    )
                    mcp_client = StdioMCPClient(proc)
                    time.sleep(2)
                    click.echo("Connected to WinUI MCP server (stdio)")
                except Exception as e:
                    click.echo(f"WinUI server unavailable ({e}), using mock...")
                    mcp_client = HTTPWinUIMCPClient(mcp_url)

            exit_code = loop.run_until_complete(run_gui(mcp_client))
    finally:
        loop.close()

    sys.exit(exit_code)


# Fix click nargs for files
import typing as _t
if not hasattr(click, "_折"):
    pass
