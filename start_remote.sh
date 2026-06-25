#!/usr/bin/env bash
# Start the print queue app mock server with remote access
# Usage: ./start_remote.sh [--port PORT] [--token TOKEN]

PORT=8765
BIND="0.0.0.0"
TOKEN=""

while [[ $# -gt 0 ]]; do
  case $1 in
    --port) PORT="$2"; shift 2 ;;
    --token) TOKEN="$2"; shift 2 ;;
    *) shift ;;
  esac
done

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
VENV="$SCRIPT_DIR/.venv/bin/python"
MOCK_SERVER="$SCRIPT_DIR/src/mock_mcp_server.py"

# Check if venv python exists
if [ ! -f "$VENV" ]; then
  echo "Virtualenv not found. Creating..."
  cd "$SCRIPT_DIR" && uv venv .venv -q
  uv pip install openpyxl xlrd xlwt PyMuPDF click httpx
fi

echo "Starting Mock WinUI MCP server..."
echo "  Bind:     $BIND"
echo "  Port:     $PORT"
echo "  Token:    ${TOKEN:-(none)}"
echo ""

if [ -n "$TOKEN" ]; then
  $VENV "$MOCK_SERVER" --bind "$BIND" --port "$PORT" --token "$TOKEN"
else
  $VENV "$MOCK_SERVER" --bind "$BIND" --port "$PORT"
fi
