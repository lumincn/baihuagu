#!/bin/bash
set -e

# 百花 - 本地自动构建与部署脚本
# 用法: ./scripts/auto-deploy.sh [--force] [--all]
#   --force: 跳过防抖，立即构建
#   --all:   强制构建所有服务（family + webui），忽略增量判断

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
DOCKER_DIR="$PROJECT_ROOT/docker"
LOG_FILE="/tmp/baihua-auto-deploy.log"
LOCK_FILE="/tmp/baihua-auto-deploy.lock"
NGINX_CONFIG_SRC="$DOCKER_DIR/nginx/nginx.conf"
NGINX_CONFIG_DST="/opt/baihua/config/nginx/nginx.conf"
DEBOUNCE_SECONDS=5

log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1" | tee -a "$LOG_FILE"
}

detect_services_to_build() {
    # 获取最近一次 commit 的变更文件列表
    local changed_files
    changed_files=$(cd "$PROJECT_ROOT" && git diff --name-only HEAD~1 HEAD 2>/dev/null || true)

    if [[ -z "$changed_files" ]]; then
        echo "⚠️  未检测到变更文件，默认构建 family + webui" >&2
        echo "family webui"
        return
    fi

    local build_family=false
    local build_webui=false
    local build_ai=false
    local build_vault=false
    build_nginx=false  # 全局变量，供调用方读取（不能 local，否则 $(...) subshell 捕获后丢失）

    while IFS= read -r file; do
        [[ -z "$file" ]] && continue
        case "$file" in
            services/Baihua.Family/*|services/Baihua.Core/*|services/Baihua.Contracts/*|services/Baihua.Data/*|libs/*|libs/MobileContract/*|docker/Dockerfile.family|docker/Dockerfile.base-build|docker/Dockerfile.base-runtime)
                build_family=true
                ;;
            services/Baihua.AI/*|services/Baihua.Core/*|services/Baihua.Contracts/*|services/Baihua.Data/*|libs/*|libs/MobileContract/*|docker/Dockerfile.ai|docker/Dockerfile.base-build|docker/Dockerfile.base-runtime)
                build_ai=true
                ;;
            services/Baihua.Vault/*|services/Baihua.Core/*|services/Baihua.Contracts/*|services/Baihua.Data/*|libs/*|libs/MobileContract/*|docker/Dockerfile.vault|docker/Dockerfile.base-build|docker/Dockerfile.base-runtime)
                build_vault=true
                ;;
            services/Baihua.Web/*|services/Baihua.Contracts/*|services/Baihua.Data/*|libs/*|libs/MobileContract/*|docker/Dockerfile.webui|docker/Dockerfile.base-build|docker/Dockerfile.base-runtime)
                build_webui=true
                ;;
            docker/nginx/*|docker/docker-compose.yml)
                # nginx 配置变更需要重启 nginx 容器
                build_nginx=true
                ;;
        esac
    done <<< "$changed_files"

    # 如果没有匹配到任何服务，但变更在仓库内，安全起见全部构建
    if [[ "$build_family" == false && "$build_ai" == false && "$build_vault" == false && "$build_webui" == false ]]; then
        # 检查变更是否在仓库内（排除 docs/ 等）
        local has_code_change=false
        while IFS= read -r file; do
            if [[ "$file" == services/* || "$file" == libs/* || "$file" == docker/* ]]; then
                has_code_change=true
                break
            fi
        done <<< "$changed_files"

        if [[ "$has_code_change" == true ]]; then
            echo "📋 检测到代码变更，但无法精确匹配服务，安全起见构建全部服务" >&2
            build_family=true
            build_ai=true
            build_vault=true
            build_webui=true
        else
            echo "📋 变更不涉及核心服务（docs/scripts 等），跳过构建" >&2
            echo ""
            return
        fi
    fi

    local services=""
    [[ "$build_family" == true ]] && services="family"
    [[ "$build_ai" == true ]] && services="${services:+$services }family-ai"
    [[ "$build_vault" == true ]] && services="${services:+$services }family-vault"
    [[ "$build_webui" == true ]] && services="${services:+$services }webui"
    echo "$services"
}

# 文件锁：确保只有一个实例执行构建（防止 post-merge/post-commit 并发触发多个实例）
exec 200>"$LOCK_FILE"
if ! flock -n 200; then
    log "⏭️  另一个构建实例正在运行，跳过"
    exit 0
fi

# 防抖：如果最近 N 秒内有构建成功过，跳过（hook 重复触发防护）
if [[ "$1" != "--force" && "$1" != "--all" ]]; then
    LAST_BUILD=$(stat -c %Y "$LOCK_FILE" 2>/dev/null || echo 0)
    NOW=$(date +%s)
    ELAPSED=$((NOW - LAST_BUILD))
    if [[ $ELAPSED -lt $DEBOUNCE_SECONDS ]]; then
        log "⏭️  跳过构建（${ELAPSED}s 前刚构建过，防抖 ${DEBOUNCE_SECONDS}s）"
        exit 0
    fi
fi

touch "$LOCK_FILE"
log "🚀 开始自动构建与部署..."

# 同步 nginx 配置（如存在）
if [[ -f "$NGINX_CONFIG_SRC" && -d "$(dirname "$NGINX_CONFIG_DST")" ]]; then
    if ! cmp -s "$NGINX_CONFIG_SRC" "$NGINX_CONFIG_DST" 2>/dev/null; then
        log "📋 同步 nginx 配置..."
        if [[ -w "$(dirname "$NGINX_CONFIG_DST")" ]]; then
            cp "$NGINX_CONFIG_SRC" "$NGINX_CONFIG_DST"
        elif command -v sudo >/dev/null 2>&1; then
            sudo cp "$NGINX_CONFIG_SRC" "$NGINX_CONFIG_DST"
        else
            log "⚠️  无权限同步 nginx 配置到 $NGINX_CONFIG_DST，跳过"
        fi
    fi
fi

cd "$DOCKER_DIR"

# 导出版本信息，确保 Dockerfile 能注入 git.commit label
export GIT_COMMIT=$(cd "$PROJECT_ROOT" && git rev-parse HEAD 2>/dev/null || echo "unknown")
export GIT_BRANCH=$(cd "$PROJECT_ROOT" && git rev-parse --abbrev-ref HEAD 2>/dev/null || echo "unknown")

# 判断需要构建哪些服务
build_nginx=false  # 全局标记：nginx 是否需要重启
if [[ "$1" == "--all" || "$2" == "--all" ]]; then
    SERVICES="family family-ai family-vault webui"
    log "🔨 强制构建所有服务: $SERVICES"
else
    # 不使用 $(...) 捕获以避免 subshell 丢失 build_nginx 变量
    detect_services_to_build > /tmp/baihua-detect-result.txt
    SERVICES=$(cat /tmp/baihua-detect-result.txt)
    if [[ -z "$SERVICES" ]]; then
        if [[ "$build_nginx" == true ]]; then
            log "🔄 仅重启 nginx（配置变更）..."
            docker compose -p baihua up -d --force-recreate nginx 2>&1 | tee -a "$LOG_FILE"
            log "✅ nginx 已重启"
        fi
        rm -f /tmp/baihua-detect-result.txt
        exit 0
    fi
    log "🔨 增量构建: $SERVICES"
fi
rm -f /tmp/baihua-detect-result.txt

# 构建核心服务
if docker compose -p baihua build $SERVICES 2>&1 | tee -a "$LOG_FILE"; then
    log "✅ 镜像构建成功"
else
    log "❌ 构建失败，查看日志: $LOG_FILE"
    exit 1
fi

# 重启服务（保留数据卷）
log "🔄 重启服务..."
if docker compose -p baihua up -d --force-recreate $SERVICES nginx 2>&1 | tee -a "$LOG_FILE"; then
    log "✅ 容器已启动"
else
    log "❌ 部署失败，查看日志: $LOG_FILE"
    exit 1
fi

# 等待健康检查
log "🏥 等待健康检查..."
for i in {1..30}; do
    if curl -sf http://127.0.0.1:8788/health >/dev/null 2>&1 && \
       curl -sf http://127.0.0.1:8791/health >/dev/null 2>&1 && \
       curl -sf http://127.0.0.1:8790/health >/dev/null 2>&1 && \
       curl -sf http://127.0.0.1:5177/ >/dev/null 2>&1; then
        log "✅ 服务已就绪 | http://localhost:5177"
        exit 0
    fi
    sleep 1
done

log "⚠️  健康检查超时，请手动查看: docker compose -p baihua ps"
