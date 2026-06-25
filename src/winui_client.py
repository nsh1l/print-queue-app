"""
MCP client for WinUI MCP server.
Wraps the MCP HTTP client and exposes typed tool methods.
"""
import json
import httpx
from typing import Any, Optional


class WinUIMCPClient:
    """Client for the WinUI MCP server tools."""

    def __init__(self, base_url: str = "http://localhost:8765"):
        self.base_url = base_url.rstrip("/")
        self.session_id: Optional[str] = None

    def _call(self, method: str, params: dict = {}) -> dict:
        """Send a JSON-RPC request to the MCP server."""
        payload = {
            "jsonrpc": "2.0",
            "id": 1,
            "method": method,
            "params": params,
        }
        with httpx.Client(timeout=30) as client:
            response = client.post(
                f"{self.base_url}/mcp",
                json=payload,
                headers={"Content-Type": "application/json"},
            )
            response.raise_for_status()
            data = response.json()
            if "error" in data:
                raise RuntimeError(f"MCP error: {data['error']}")
            return data.get("result", {})

    # ── Window ────────────────────────────────────────────────────────

    def create_window(
        self, title: str, width: int = 800, height: int = 600
    ) -> str:
        result = self._call("create_window", {"title": title, "width": width, "height": height})
        self.session_id = result.get("window_id", result.get("id", "main"))
        return self.session_id

    def set_window_layout(self, layout: dict) -> None:
        self._call("set_window_layout", {"window_id": self.session_id, "layout": layout})

    # ── UI Elements ───────────────────────────────────────────────────

    def add_drop_zone(self, zone_id: str, label: str) -> None:
        self._call("add_drop_zone", {
            "window_id": self.session_id,
            "zone_id": zone_id,
            "label": label,
        })

    def update_file_list(self, files: list[dict]) -> None:
        self._call("update_file_list", {
            "window_id": self.session_id,
            "files": files,
        })

    def add_button(
        self, button_id: str, label: str, icon: str = ""
    ) -> None:
        self._call("add_button", {
            "window_id": self.session_id,
            "button_id": button_id,
            "label": label,
            "icon": icon,
        })

    def add_label(self, label_id: str, text: str) -> None:
        self._call("add_label", {
            "window_id": self.session_id,
            "label_id": label_id,
            "text": text,
        })

    def set_status_text(self, text: str) -> None:
        self._call("set_status_text", {
            "window_id": self.session_id,
            "text": text,
        })

    # ── Progress ─────────────────────────────────────────────────────

    def set_progress(self, percent: int, label: str = "") -> None:
        self._call("set_progress", {
            "window_id": self.session_id,
            "percent": percent,
            "label": label,
        })

    # ── Events ───────────────────────────────────────────────────────

    def register_callback(self, event_type: str, callback_id: str) -> None:
        self._call("register_callback", {
            "window_id": self.session_id,
            "event_type": event_type,
            "callback_id": callback_id,
        })

    def poll_events(self) -> list[dict]:
        """Poll for UI events (button clicks, file drops)."""
        result = self._call("poll_events", {"window_id": self.session_id})
        return result.get("events", [])

    # ── Utility ──────────────────────────────────────────────────────

    def clear_window(self) -> None:
        self._call("clear_window", {"window_id": self.session_id})

    def close_window(self) -> None:
        self._call("close_window", {"window_id": self.session_id})
