#!/usr/bin/env bash
# Wrapper to run openvino_benchmark.py inside WSL2 Arch.
# Execute from Windows PowerShell via:  wsl -d Arch -- bash /mnt/c/.../wsl_bench.sh <args_forwarded_to_py>
set -e
export PATH="/root/.local/bin:$PATH"
# pip installed to user site; ensure python sees it
export PYTHONPATH="/root/.local/lib/python3.14/site-packages:$PYTHONPATH"
SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
SCRIPT="$SCRIPT_DIR/openvino_benchmark.py"
if [ ! -f "$SCRIPT" ]; then
  SCRIPT="/root/openvino_benchmark.py"
fi
exec python3 "$SCRIPT" "$@"
