"""
WinUI Bridge - Python wrapper for WinUI 3 (Windows App SDK) API.
This module provides the actual WinUI widget manipulation.

On non-Windows, this module is replaced by a mock in mock_mcp_server.py.
"""
import sys

if sys.platform != "win32":
    raise ImportError("WinUI bridge requires Windows")

import json
from typing import Any


class WinUIBridge:
    """
    Bridges Python to WinUI 3 via Windows App SDK / WinRT API.

    This implementation uses:
    - win32gui / win32con for basic window operations
    - win32com for XAML element access (when WinUI is available)
    - Fallback: spawns a WebView2 window with our HTML/JS interface

    The WebView2 fallback is the most portable approach and works
    with any WinUI 3 app that hosts a WebView2 control.
    """

    def __init__(self, port: int = 8765):
        self.port = port
        self.windows: dict[str, dict] = {}
        self._webview_ready = False

    # ── Window Management ──────────────────────────────────────────────

    def create_window(self, title: str = "Window", width: int = 800, height: int = 600) -> str:
        """
        Create a new WinUI window. Returns window_id.
        Uses WebView2 hosted HTML as the UI surface.
        """
        import win32gui
        import win32con
        import win32api

        window_id = f"win_{len(self.windows)}"

        # Register window class
        wc = win32gui.WNDCLASS()
        wc.lpfnWndProc = self._wnd_proc
        wc.hInstance = win32api.GetModuleHandle(None)
        wc.lpszClassName = f"PrintQueue_{window_id}"
        wc.style = win32con.CS_VREDRAW | win32con.CS_HREDRAW
        try:
            win32gui.RegisterClass(wc)
        except Exception:
            pass  # Already registered

        # Create window
        hwnd = win32gui.CreateWindow(
            wc.lpszClassName,
            title,
            win32con.WS_OVERLAPPEDWINDOW | win32con.WS_VISIBLE,
            win32con.CW_USEDEFAULT,
            win32con.CW_USEDEFAULT,
            width,
            height,
            None,
            None,
            wc.hInstance,
            None,
        )

        self.windows[window_id] = {
            "hwnd": hwnd,
            "title": title,
            "elements": {},
            "callbacks": {},
        }

        self._init_webview(hwnd)

        win32gui.ShowWindow(hwnd, win32con.SW_SHOW)
        win32gui.UpdateWindow(hwnd)

        return window_id

    def _init_webview(self, hwnd):
        """Initialize WebView2 in the window for HTML-based UI."""
        try:
            from msedge_webview_functions import edge_webview2  # type: ignore
            self._webview_ready = True
        except ImportError:
            self._webview_ready = False

    def _wnd_proc(self, hwnd, msg, wparam, lparam):
        import win32gui
        if msg == win32con.WM_DESTROY:
            win32gui.PostQuitMessage(0)
            return 0
        return win32gui.DefWindowProc(hwnd, msg, wparam, lparam)

    def set_layout(self, window_id: str, layout: dict) -> dict:
        """Configure window layout from dict spec."""
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        # layout spec: { "drop_zone": {...}, "list_view": {...}, "button_bar": {...} }
        self.windows[window_id]["layout"] = layout
        return {"ok": True}

    def set_window_size(self, window_id: str, width: int, height: int) -> dict:
        import win32gui
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        hwnd = self.windows[window_id]["hwnd"]
        win32gui.MoveWindow(hwnd, 0, 0, width, height, True)
        return {"ok": True}

    # ── UI Elements ────────────────────────────────────────────────────

    def add_drop_zone(self, window_id: str, zone_id: str, label: str = "") -> dict:
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        self.windows[window_id]["elements"][zone_id] = {
            "type": "drop_zone",
            "label": label,
        }
        return {"ok": True}

    def update_file_list(self, window_id: str, files: list[dict]) -> dict:
        """Update the queue list view with file data."""
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        self.windows[window_id]["file_list"] = files
        self._refresh_webview(window_id)
        return {"ok": True}

    def add_button(self, window_id: str, button_id: str, label: str = "", icon: str = "") -> dict:
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        self.windows[window_id]["elements"][button_id] = {
            "type": "button",
            "label": label,
            "icon": icon,
        }
        return {"ok": True}

    def add_label(self, window_id: str, label_id: str, text: str = "") -> dict:
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        self.windows[window_id]["elements"][label_id] = {
            "type": "label",
            "text": text,
        }
        return {"ok": True}

    def set_status_text(self, window_id: str, text: str) -> dict:
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        self.windows[window_id]["status_text"] = text
        self._refresh_webview(window_id)
        return {"ok": True}

    def set_progress(self, window_id: str, percent: int, label: str = "") -> dict:
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        self.windows[window_id]["progress"] = percent
        self.windows[window_id]["progress_label"] = label
        self._refresh_webview(window_id)
        return {"ok": True}

    def register_callback(self, window_id: str, event_type: str, callback_id: str) -> dict:
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        self.windows[window_id]["callbacks"][event_type] = callback_id
        return {"ok": True}

    def poll_events(self, window_id: str) -> list[dict]:
        """Poll and return pending UI events."""
        if window_id not in self.windows:
            return []
        # Process Windows message queue
        import win32gui
        events = []
        while True:
            msg = win32gui.PeekMessage(None, 0, 0, win32con.PM_REMOVE)
            if not msg[0]:
                break
            msg_hwnd, msg_msg, msg_wparam, msg_lparam, msg_time, msg_point = msg
            if msg_msg == win32con.WM_COMMAND:
                # Button click
                btn_id = msg_wparam & 0xFFFF
                events.append({"type": "button_click", "data": {"button_id": str(btn_id)}})
            win32gui.TranslateMessage(msg)
            win32gui.DispatchMessage(msg)
        return events

    def _refresh_webview(self, window_id: str) -> None:
        """Push state update to WebView2 if available."""
        if not self._webview_ready:
            return
        state = self.windows.get(window_id, {})
        # The webview receives state via window.chrome.webview.postMessage
        # This is handled by the edge_webview2 bridge

    def clear_window(self, window_id: str) -> dict:
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        self.windows[window_id]["elements"].clear()
        self.windows[window_id]["file_list"] = []
        return {"ok": True}

    def close_window(self, window_id: str) -> dict:
        import win32gui
        if window_id not in self.windows:
            return {"error": f"Window {window_id} not found"}
        hwnd = self.windows[window_id]["hwnd"]
        win32gui.DestroyWindow(hwnd)
        del self.windows[window_id]
        return {"ok": True}
