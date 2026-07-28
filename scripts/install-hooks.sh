#!/bin/bash
set -e

# 安装 Git hooks，使 commit / pull 后自动触发本地部署
# 用法: ./scripts/install-hooks.sh

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
HOOKS_SRC="$SCRIPT_DIR/hooks"
HOOKS_DST="$PROJECT_ROOT/.git/hooks"

if [[ ! -d "$HOOKS_DST" ]]; then
    echo "❌ 未找到 .git/hooks 目录，请在 git 仓库根目录运行"
    exit 1
fi

for hook in post-commit post-merge; do
    if [[ -f "$HOOKS_SRC/$hook" ]]; then
        cp "$HOOKS_SRC/$hook" "$HOOKS_DST/$hook"
        chmod +x "$HOOKS_DST/$hook"
        echo "✅ 已安装 $hook hook"
    else
        echo "⚠️  未找到 $hook 模板"
    fi
done

echo ""
echo "🎉 本地流水线已就绪！"
echo "   每次 git commit 或 git pull 后将自动构建并部署变更的服务。"
echo "   日志: tail -f /tmp/baihua-auto-deploy.log"
