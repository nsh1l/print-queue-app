#!/usr/bin/env python3
"""Minimal test to diagnose mock server connection issues."""
import sys
sys.path.insert(0, 'src')
from mock_mcp_server import MockMCPServer
import threading
import time
import urllib.request

server = MockMCPServer(port=9882, bind='127.0.0.1', token=None)
t = threading.Thread(target=server.start, daemon=True)
t.start()
time.sleep(2)

print("Server started, testing connection...", flush=True)
try:
    r = urllib.request.urlopen('http://127.0.0.1:9882/', timeout=3)
    print(f"SUCCESS: {r.status}, {len(r.read())} bytes", flush=True)
except Exception as e:
    print(f"FAILED: {type(e).__name__}: {e}", flush=True)

time.sleep(1)
