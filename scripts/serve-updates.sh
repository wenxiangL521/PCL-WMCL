#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PORT="${1:-8765}"
echo "更新源：http://127.0.0.1:$PORT/latest.json"
exec python3 -m http.server "$PORT" --bind 127.0.0.1 --directory "$ROOT/artifacts"
