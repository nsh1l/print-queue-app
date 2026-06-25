# PrintQueueApp Remote Connection Info
# ================================================

## Endpoints

| Service | URL |
|---------|-----|
| Mock UI | http://print.soichi.ro/ |
| MCP JSON-RPC | http://print.soichi.ro/mcp |
| State | http://print.soichi.ro/state |

## Token (HMAC-SHA256)

Pass via header: `X-Token: <token>`

Token: `print_queue_secret_2026`

## Quick Start

```bash
# 1. Cloudflare Tunnel config (sudo needed once)
sudo tee /etc/cloudflared/config.yml > /dev/null << 'EOF'
tunnel: 890021ac-7a96-4819-9d90-cd9bd8a965b6
credentials-file: /root/.cloudflared/890021ac-7a96-4819-9d90-cd9bd8a965b6.json

ingress:
  - hostname: beszel.soichi.ro
    service: http://localhost:8090
  - hostname: ssh.soichi.ro
    service: http://localhost:9999
  - hostname: print.soichi.ro
    service: http://localhost:8765
  - service: http_status:404
EOF

# 2. Reload cloudflared
sudo pkill -HUP cloudflared && sleep 3

# 3. Start mock server (in screen/tmux)
cd ~/proj/print-queue-app
./start_remote.sh --token print_queue_secret_2026

# 4. Connect from any machine
python -m src --mock --url http://print.soichi.ro/ --token print_queue_secret_2026
```

## Local Development

```bash
# Without token (localhost only)
python -m src --mock --url http://localhost:8765/

# With token (remote)
python -m src --mock --url http://print.soichi.ro/ --token print_queue_secret_2026
```
