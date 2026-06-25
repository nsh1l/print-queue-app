# WinUI MCP Server for PrintQueueApp
# This MCP server exposes WinUI tools to the PrintQueueApp Python client.

# Requirements:
# pip install mcp httpx

import json
import sys
import asyncio
from mcp.server import Server
from mcp.server.stdio import stdio_server
from mcp.types import Tool, TextContent

# WinUI app bridge - this module wraps WinUI automation
try:
    from winui_bridge import WinUIBridge
except ImportError:
    # Stub when not on Windows
    class WinUIBridge:
        def __init__(self, **kw): pass
        def create_window(self, **kw): return "stub-window"
        def set_layout(self, **kw): pass
        def add_drop_zone(self, **kw): pass
        def update_file_list(self, **kw): pass
        def add_button(self, **kw): pass
        def add_label(self, **kw): pass
        def set_status_text(self, **kw): pass
        def set_progress(self, **kw): pass
        def register_callback(self, **kw): pass
        def poll_events(self, **kw): return []
        def clear_window(self, **kw): pass
        def close_window(self, **kw): pass


# MCP Server setup
server = Server("winui-mcp")

winui = WinUIBridge()


@server.list_tools()
async def list_tools() -> list[Tool]:
    return [
        Tool(
            name="create_window",
            description="Create a new WinUI window",
            inputSchema={
                "type": "object",
                "properties": {
                    "title": {"type": "string", "description": "Window title"},
                    "width": {"type": "integer", "default": 800},
                    "height": {"type": "integer", "default": 600},
                },
            },
        ),
        Tool(
            name="set_window_layout",
            description="Set the window layout configuration",
            inputSchema={
                "type": "object",
                "properties": {
                    "window_id": {"type": "string"},
                    "layout": {"type": "object"},
                },
            },
        ),
        Tool(
            name="add_drop_zone",
            description="Add a file drop zone to the window",
            inputSchema={
                "type": "object",
                "required": ["window_id", "zone_id", "label"],
                "properties": {
                    "window_id": {"type": "string"},
                    "zone_id": {"type": "string"},
                    "label": {"type": "string"},
                },
            },
        ),
        Tool(
            name="update_file_list",
            description="Update the file list display in the queue view",
            inputSchema={
                "type": "object",
                "required": ["window_id", "files"],
                "properties": {
                    "window_id": {"type": "string"},
                    "files": {
                        "type": "array",
                        "items": {
                            "type": "object",
                            "properties": {
                                "id": {"type": "string"},
                                "name": {"type": "string"},
                                "size": {"type": "string"},
                                "status": {"type": "string"},
                                "selected": {"type": "boolean"},
                                "error": {"type": "string"},
                            },
                        },
                    },
                },
            },
        ),
        Tool(
            name="add_button",
            description="Add a button to the window",
            inputSchema={
                "type": "object",
                "required": ["window_id", "button_id", "label"],
                "properties": {
                    "window_id": {"type": "string"},
                    "button_id": {"type": "string"},
                    "label": {"type": "string"},
                    "icon": {"type": "string", "default": ""},
                },
            },
        ),
        Tool(
            name="add_label",
            description="Add a text label to the window",
            inputSchema={
                "type": "object",
                "required": ["window_id", "label_id", "text"],
                "properties": {
                    "window_id": {"type": "string"},
                    "label_id": {"type": "string"},
                    "text": {"type": "string"},
                },
            },
        ),
        Tool(
            name="set_status_text",
            description="Update the status bar text",
            inputSchema={
                "type": "object",
                "required": ["window_id", "text"],
                "properties": {
                    "window_id": {"type": "string"},
                    "text": {"type": "string"},
                },
            },
        ),
        Tool(
            name="set_progress",
            description="Update the progress bar",
            inputSchema={
                "type": "object",
                "required": ["window_id", "percent"],
                "properties": {
                    "window_id": {"type": "string"},
                    "percent": {"type": "integer", "minimum": 0, "maximum": 100},
                    "label": {"type": "string", "default": ""},
                },
            },
        ),
        Tool(
            name="register_callback",
            description="Register a callback for UI events",
            inputSchema={
                "type": "object",
                "required": ["window_id", "event_type", "callback_id"],
                "properties": {
                    "window_id": {"type": "string"},
                    "event_type": {"type": "string", "enum": ["file_drop", "button_click", "file_select"]},
                    "callback_id": {"type": "string"},
                },
            },
        ),
        Tool(
            name="poll_events",
            description="Poll for pending UI events",
            inputSchema={
                "type": "object",
                "required": ["window_id"],
                "properties": {
                    "window_id": {"type": "string"},
                },
            },
        ),
        Tool(
            name="clear_window",
            description="Clear all UI elements from the window",
            inputSchema={
                "type": "object",
                "required": ["window_id"],
                "properties": {
                    "window_id": {"type": "string"},
                },
            },
        ),
        Tool(
            name="close_window",
            description="Close the window",
            inputSchema={
                "type": "object",
                "required": ["window_id"],
                "properties": {
                    "window_id": {"type": "string"},
                },
            },
        ),
    ]


@server.call_tool()
async def call_tool(name: str, arguments: dict) -> list[TextContent]:
    try:
        if name == "create_window":
            result = winui.create_window(
                title=arguments.get("title", "Window"),
                width=arguments.get("width", 800),
                height=arguments.get("height", 600),
            )
        elif name == "set_window_layout":
            result = winui.set_layout(
                window_id=arguments.get("window_id"),
                layout=arguments.get("layout", {}),
            )
        elif name == "add_drop_zone":
            result = winui.add_drop_zone(
                window_id=arguments.get("window_id"),
                zone_id=arguments.get("zone_id"),
                label=arguments.get("label"),
            )
        elif name == "update_file_list":
            result = winui.update_file_list(
                window_id=arguments.get("window_id"),
                files=arguments.get("files", []),
            )
        elif name == "add_button":
            result = winui.add_button(
                window_id=arguments.get("window_id"),
                button_id=arguments.get("button_id"),
                label=arguments.get("label"),
                icon=arguments.get("icon", ""),
            )
        elif name == "add_label":
            result = winui.add_label(
                window_id=arguments.get("window_id"),
                label_id=arguments.get("label_id"),
                text=arguments.get("text"),
            )
        elif name == "set_status_text":
            result = winui.set_status_text(
                window_id=arguments.get("window_id"),
                text=arguments.get("text"),
            )
        elif name == "set_progress":
            result = winui.set_progress(
                window_id=arguments.get("window_id"),
                percent=arguments.get("percent", 0),
                label=arguments.get("label", ""),
            )
        elif name == "register_callback":
            result = winui.register_callback(
                window_id=arguments.get("window_id"),
                event_type=arguments.get("event_type"),
                callback_id=arguments.get("callback_id"),
            )
        elif name == "poll_events":
            result = winui.poll_events(window_id=arguments.get("window_id"))
        elif name == "clear_window":
            result = winui.clear_window(window_id=arguments.get("window_id"))
        elif name == "close_window":
            result = winui.close_window(window_id=arguments.get("window_id"))
        else:
            return [TextContent(type="text", text=f"Unknown tool: {name}")]

        return [TextContent(type="text", text=json.dumps(result, ensure_ascii=False))]
    except Exception as e:
        return [TextContent(type="text", text=f"Error: {e}")]


async def main():
    async with stdio_server() as (read_stream, write_stream):
        await server.run(read_stream, write_stream, server.create_initialization_options())


if __name__ == "__main__":
    asyncio.run(main())
