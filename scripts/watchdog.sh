#!/bin/bash
# 百花 - 文件变化监控守护进程
# 监控源码目录变化，自动触发构建部署

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
LOG_FILE="/tmp/baihua-watchdog.log"

echo "[$(date '+%Y-%m-%d %H:%M:%S')] 👁️  watchdog 启动，监控目录: $PROJECT_ROOT/services/ $PROJECT_ROOT/docker/" | tee -a "$LOG_FILE"

# 使用 inotifywait 监控文件变化（创建、修改、移动）
# 排除 bin/ obj/ .git/ 等目录；baihua 的 libs/ 通过 NuGet 引用，不监控
inotifywait -mr \
    -e modify,create,moved_to,delete \
    --exclude '.*\.(dll|pdb|cache|tmp|swp|swo|swn|log|pyc)|/(bin|obj|\.git|node_modules|\.playwright-cli|test-results)/' \
    "$PROJECT_ROOT/services" \
    "$PROJECT_ROOT/docker" \
    2>/dev/null | while read -r dir event file; do

    # 只关注代码和配置文件的变更
    if [[ "$file" =~ \.(cs|razor|cshtml|css|js|json|xml|yml|yaml|Dockerfile|sh|env|conf|html|md)$ ]]; then
        echo "[$(date '+%Y-%m-%d %H:%M:%S')] 📝 检测到变更: $dir$file ($event)" | tee -a "$LOG_FILE"
        "$SCRIPT_DIR/auto-deploy.sh"
    fi
done
