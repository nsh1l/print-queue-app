#!/usr/bin/env python3
"""
PrintQueueApp launcher.
Starts the WinUI MCP server (C#) and Python client, then connects them.

Usage:
    python run_app.py                   # GUI mode (needs Windows + .NET)
    python run_app.py --mock            # Mock GUI mode (any platform)
    python run_app.py --cli file.xlsx   # CLI mode (any platform)
"""
import subprocess
import sys
import os
import time
import signal
import argparse
from pathlib import Path


def find_dotnet():
    """Find dotnet CLI."""
    for path in [r"C:\Program Files\dotnet\dotnet.exe", "dotnet"]:
        try:
            result = subprocess.run([path, "--version"], capture_output=True, text=True, timeout=5)
            if result.returncode == 0:
                return path
        except Exception:
            pass
    return None


def start_mock_server(port: int = 8765):
    """Start the mock MCP server (works on any platform)."""
    mock_path = Path(__file__).parent / "src" / "mock_mcp_server.py"
    proc = subprocess.Popen(
        [sys.executable, str(mock_path)],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    time.sleep(1)
    print(f"Mock MCP server running at http://localhost:{port}")
    return proc


def start_winui_server():
    """Start the real WinUI MCP server."""
    dotnet = find_dotnet()
    if not dotnet:
        print("dotnet not found. Install .NET 8 SDK.", file=sys.stderr)
        return None

    csproj = Path(__file__).parent / "WinUIMCPServer" / "PrintQueueApp.WinUI.csproj"
    proc = subprocess.Popen(
        [dotnet, "run", "--project", str(csproj), "--", "--mcp"],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )
    time.sleep(2)
    print("WinUI MCP server started")
    return proc


def start_python_client(mock: bool = False):
    """Start the Python client."""
    src_main = Path(__file__).parent / "src" / "__main__.py"
    mcp_url = "http://localhost:8765" if mock else "stdio"

    args = [sys.executable, str(src_main)]
    if mock:
        args.append("--gui")
    else:
        args.append("--gui")
    # Pass MCP URL via env
    env = os.environ.copy()
    env["PRINT_QUEUE_MCP_URL"] = mcp_url

    proc = subprocess.Popen(args, env=env)
    return proc


def main():
    parser = argparse.ArgumentParser(description="PrintQueueApp")
    parser.add_argument("--mock", action="store_true", help="Use mock MCP server (no Windows required)")
    parser.add_argument("--cli", action="store_true", help="CLI mode")
    parser.add_argument("files", nargs="*", help="Files to add to queue")
    args = parser.parse_args()

    server_proc = None
    client_proc = None

    def cleanup():
        for proc in [client_proc, server_proc]:
            if proc and proc.poll() is None:
                proc.terminate()
                proc.wait(timeout=5)

    signal.signal(signal.SIGINT, lambda *a: (cleanup(), sys.exit(1)))
    signal.signal(signal.SIGTERM, lambda *a: (cleanup(), sys.exit(1)))

    if args.mock or args.cli:
        if not args.cli:
            server_proc = start_mock_server()
        client_proc = start_python_client(mock=args.mock)
        client_proc.wait()
    else:
        # Try Windows path
        if sys.platform == "win32":
            server_proc = start_winui_server()
            if server_proc:
                client_proc = start_python_client(mock=False)
                client_proc.wait()
            else:
                print("Falling back to mock mode...")
                server_proc = start_mock_server()
                client_proc = start_python_client(mock=True)
                client_proc.wait()
        else:
            print("Non-Windows platform detected. Use --mock or --cli.")
            print("  python run_app.py --mock   # with mock GUI")
            print("  python run_app.py --cli    # CLI only")
            return 1

    cleanup()
    return 0


if __name__ == "__main__":
    sys.exit(main())
