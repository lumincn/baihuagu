#!/usr/bin/env bash
# macOS/Linux 开发启动脚本
# 在独立终端中启动 Baihua.AI、Baihua.Vault、Baihua.Family 和 WebUI

set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"

echo "========================================"
echo "百花 - 开发环境启动 (macOS/Linux)"
echo "========================================"
echo ""

# 检查 dotnet
if ! command -v dotnet &> /dev/null; then
    echo "错误：未安装 .NET SDK"
    echo "请访问 https://dotnet.microsoft.com/download 安装"
    exit 1
fi

echo "正在启动服务..."
echo ""

# 清理编译服务器缓存，避免 VBCSCompiler 缓存导致 stale binary
echo "清理编译服务器缓存..."
dotnet build-server shutdown 2>/dev/null || true
echo ""

# 辅助函数：在终端中启动进程
launch_in_terminal() {
    local label="$1"
    local dir="$2"
    local port="$3"
    local cmd="cd '$dir' && dotnet watch run --non-interactive --no-hot-reload --urls 'http://0.0.0.0:$port'"

    echo "[$label] 启动 $label (端口 $port)..."
    osascript -e "tell application \"Terminal\" to do script \"$cmd\"" 2>/dev/null || \
        echo "  请手动在终端运行: cd \"$dir\" && dotnet watch run --non-interactive --no-hot-reload --urls 'http://0.0.0.0:$port'"
}

# 启动 Baihua.AI
launch_in_terminal "Baihua.AI" "$ROOT/services/Baihua.AI" "8791"

# 启动 Baihua.Vault
launch_in_terminal "Baihua.Vault" "$ROOT/services/Baihua.Vault" "8790"

# 启动 Baihua.Family
launch_in_terminal "Baihua.Family" "$ROOT/services/Baihua.Family" "8788"

# 启动 WebUI
echo "[WebUI] 启动 WebUI.Family (端口 5177)..."
osascript -e "tell application \"Terminal\" to do script \"cd '$ROOT/services/WebUI.Family' && dotnet watch run --non-interactive\"" 2>/dev/null || \
    echo "  请手动在终端运行: cd \"$ROOT/services/WebUI.Family\" && dotnet watch run --non-interactive"

echo ""
echo "========================================"
echo "服务启动中..."
echo "========================================"
echo ""
echo "后端服务:"
echo "  - Baihua.AI    http://localhost:8791"
echo "  - Baihua.Vault http://localhost:8790"
echo "  - Baihua.Family http://localhost:8788"
echo ""
echo "前端界面:"
echo "  - WebUI            http://localhost:5177"
echo ""
echo "提示: 使用 Ctrl+C 停止服务"
