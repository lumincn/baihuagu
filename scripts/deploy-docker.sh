#!/usr/bin/env bash
set -euo pipefail

# ============================================
# Family Docker 化部署脚本
# 功能：上传源码到服务器，在服务器端构建镜像并启动
# 设计：程序在 Docker 内运行，数据和配置通过宿主机卷分离
# 架构：family + webui + nginx + openobserve 全栈容器化
# ============================================

# ---------- 配置（按需修改）----------
SERVER="${1:-}"
if [[ -z "${SERVER}" ]]; then
    echo "用法: $0 <user@host> [--skip-build]"
    echo "示例: $0 root@192.168.1.100"
    echo "      $0 root@192.168.1.100 --skip-build"
    exit 1
fi

SKIP_BUILD=false
if [[ "${2:-}" == "--skip-build" ]]; then
    SKIP_BUILD=true
fi

SSH_OPTS="-o StrictHostKeyChecking=no -o ConnectTimeout=10"
LOCAL_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

REMOTE_COMPOSE_DIR="/opt/baihua/compose"
REMOTE_CONFIG_DIR="/opt/baihua/config"
REMOTE_DATA_DIR="/opt/baihua/data"
REMOTE_LOGS_DIR="/opt/baihua/logs"
REMOTE_SRC_DIR="/opt/baihua/src"

echo "========================================"
echo "Family Docker 化部署"
echo "目标服务器: ${SERVER}"
echo "========================================"

# ---------- 1. 准备源码包 ----------
echo "[1/7] 准备源码包（排除构建产物）..."
TMP_PKG="/tmp/baihua-src.tar.gz"
cd "${LOCAL_ROOT}"
tar czf "${TMP_PKG}" \
    --exclude='.git' \
    --exclude='*/bin' \
    --exclude='*/obj' \
    --exclude='docker/data' \
    --exclude='*.user' \
    --exclude='.vscode' \
    --exclude='tests' \
    services/ libs/ docker/ scripts/ nuget-local/
echo "      源码包大小: $(du -h "${TMP_PKG}" | cut -f1)"

# ---------- 2. 上传源码与编排文件 ----------
echo "[2/7] 上传到服务器..."
ssh ${SSH_OPTS} "${SERVER}" "mkdir -p ${REMOTE_COMPOSE_DIR} ${REMOTE_CONFIG_DIR}/taskrunner ${REMOTE_CONFIG_DIR}/webui ${REMOTE_CONFIG_DIR}/nginx ${REMOTE_DATA_DIR} ${REMOTE_LOGS_DIR} ${REMOTE_SRC_DIR}"

# 上传源码
echo "      上传源码..."
rsync -avz --delete --progress "${TMP_PKG}" "${SERVER}:${REMOTE_SRC_DIR}/baihua-src.tar.gz" >/dev/null 2>&1

# 上传 .env（如果本地存在）
if [[ -f "${LOCAL_ROOT}/docker/.env" ]]; then
    rsync -avz "${LOCAL_ROOT}/docker/.env" "${SERVER}:${REMOTE_SRC_DIR}/family/docker/.env" >/dev/null 2>&1
    echo "      .env 已上传"
fi

echo "      上传完成"

# ---------- 3. 服务器端安装 Docker（如未安装）----------
echo "[3/7] 检查并安装 Docker..."
ssh ${SSH_OPTS} "${SERVER}" bash -s << 'REMOTE_SCRIPT'
    if command -v docker &>/dev/null && docker compose version &>/dev/null; then
        echo "      Docker 已安装: $(docker --version)"
        exit 0
    fi

    echo "      正在安装 Docker..."
    apt-get update -qq
    apt-get install -y -qq ca-certificates curl gnupg lsb-release
    install -m 0755 -d /etc/apt/keyrings

    # 使用阿里云镜像源（国内服务器更稳定）
    curl -fsSL https://mirrors.aliyun.com/docker-ce/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc 2>/dev/null || \
        curl -fsSL https://mirrors.aliyun.com/docker-ce/linux/debian/gpg -o /etc/apt/keyrings/docker.asc 2>/dev/null
    chmod a+r /etc/apt/keyrings/docker.asc

    . /etc/os-release
    echo "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.asc] https://mirrors.aliyun.com/docker-ce/linux/${ID} ${VERSION_CODENAME} stable" > /etc/apt/sources.list.d/docker.list
    apt-get update -qq
    apt-get install -y -qq docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
    systemctl enable docker
    systemctl start docker
    echo "      Docker 安装完成: $(docker --version)"
REMOTE_SCRIPT

# ---------- 4. 服务器端解压并构建 ----------
echo "[4/7] 服务器端解压源码并构建镜像..."
ssh ${SSH_OPTS} "${SERVER}" bash -s "${REMOTE_SRC_DIR}" "${REMOTE_COMPOSE_DIR}" "${SKIP_BUILD}" << 'REMOTE_SCRIPT'
    SRC_DIR="$1"
    COMPOSE_DIR="$2"
    SKIP_BUILD="$3"

    # 解压源码
    echo "      解压源码..."
    rm -rf "${SRC_DIR}/family"
    mkdir -p "${SRC_DIR}/family"
    tar xzf "${SRC_DIR}/baihua-src.tar.gz" -C "${SRC_DIR}/family"

    # 建立 compose 目录软链接（方便管理）
    rm -rf "${COMPOSE_DIR}"
    ln -s "${SRC_DIR}/family/docker" "${COMPOSE_DIR}"

    if [[ "${SKIP_BUILD}" == "true" ]]; then
        echo "      跳过镜像构建（--skip-build）"
    else
        # 构建镜像（nginx 使用官方镜像，无需构建）
        echo "      构建 Docker 镜像（首次构建可能需要 5-10 分钟）..."
        cd "${SRC_DIR}/family/docker"
        docker compose build family ai vault webui --no-cache 2>&1 | tail -20
    fi
REMOTE_SCRIPT

echo "      镜像准备完成"

# ---------- 5. 部署 Nginx 配置 ----------
echo "[5/7] 部署 Nginx 配置..."
ssh ${SSH_OPTS} "${SERVER}" bash -s "${REMOTE_SRC_DIR}" "${REMOTE_CONFIG_DIR}" << 'REMOTE_SCRIPT'
    SRC_DIR="$1"
    CONFIG_DIR="$2"

    # 部署 Nginx 配置（如果宿主机配置目录中不存在自定义配置）
    if [[ ! -f "${CONFIG_DIR}/nginx/nginx.conf" ]]; then
        cp "${SRC_DIR}/family/docker/nginx/nginx.conf" "${CONFIG_DIR}/nginx/nginx.conf"
        echo "      Nginx 配置已部署到 ${CONFIG_DIR}/nginx/nginx.conf"
    else
        echo "      Nginx 配置已存在，跳过（如需更新请手动修改 ${CONFIG_DIR}/nginx/nginx.conf）"
    fi
REMOTE_SCRIPT

# ---------- 6. 停止旧服务并启动容器 ----------
echo "[6/7] 停止旧版 systemd 服务并启动容器..."
ssh ${SSH_OPTS} "${SERVER}" bash -s "${REMOTE_SRC_DIR}" << 'REMOTE_SCRIPT'
    SRC_DIR="$1"

    # 停止旧版 systemd 服务（如果存在）
    systemctl stop family ai vault webui 2>/dev/null || true
    systemctl disable family ai vault webui 2>/dev/null || true

    # 确保数据目录权限正确
    mkdir -p /opt/baihua/data /opt/baihua/logs \
        /opt/baihua/config/taskrunner /opt/baihua/config/taskrunner-ai /opt/baihua/config/taskrunner-vault \
        /opt/baihua/config/webui /opt/baihua/config/nginx \
        /opt/baihua/data/openobserve

    # 启动容器
    cd "${SRC_DIR}/family/docker"
    docker compose down 2>/dev/null || true
    docker compose up -d --remove-orphans
REMOTE_SCRIPT

echo "      容器已启动"

# ---------- 7. 健康检查 ----------
echo "[7/7] 等待服务启动并健康检查..."
HEALTH_OK=0
for i in {1..45}; do
    sleep 3
    FAMILY_HEALTH=$(ssh ${SSH_OPTS} "${SERVER}" "docker inspect --format='{{.State.Health.Status}}' bh-family 2>/dev/null || echo 'unknown'")
    AI_HEALTH=$(ssh ${SSH_OPTS} "${SERVER}" "docker inspect --format='{{.State.Health.Status}}' bh-ai 2>/dev/null || echo 'unknown'")
    VAULT_HEALTH=$(ssh ${SSH_OPTS} "${SERVER}" "docker inspect --format='{{.State.Health.Status}}' bh-vault 2>/dev/null || echo 'unknown'")
    WEBUI_HEALTH=$(ssh ${SSH_OPTS} "${SERVER}" "docker inspect --format='{{.State.Health.Status}}' baihua-webui 2>/dev/null || echo 'unknown'")
    NGINX_HEALTH=$(ssh ${SSH_OPTS} "${SERVER}" "docker inspect --format='{{.State.Health.Status}}' baihua-nginx 2>/dev/null || echo 'unknown'")

    if [[ "$FAMILY_HEALTH" == "healthy" && "$AI_HEALTH" == "healthy" && "$VAULT_HEALTH" == "healthy" && "$WEBUI_HEALTH" == "healthy" && "$NGINX_HEALTH" == "healthy" ]]; then
        HEALTH_OK=1
        break
    fi
    echo "      等待健康检查... TR=$FAMILY_HEALTH AI=$AI_HEALTH Vault=$VAULT_HEALTH WUF=$WEBUI_HEALTH Nginx=$NGINX_HEALTH ($i/45)"
done

if [[ "$HEALTH_OK" -eq 0 ]]; then
    echo "ERROR: 容器未在预期时间内变为 healthy"
    ssh ${SSH_OPTS} "${SERVER}" "cd ${REMOTE_SRC_DIR}/family/docker && docker compose logs --tail 30"
    exit 1
fi

# HTTP 检查
FAMILY_CODE="000"
for i in {1..10}; do
    FAMILY_CODE=$(ssh ${SSH_OPTS} "${SERVER}" "curl -s -o /dev/null -w '%{http_code}' --max-time 5 http://127.0.0.1:8788/health" 2>/dev/null || echo "000")
    if [[ "$FAMILY_CODE" == "200" ]]; then break; fi
    sleep 1
done

AI_CODE="000"
for i in {1..10}; do
    AI_CODE=$(ssh ${SSH_OPTS} "${SERVER}" "curl -s -o /dev/null -w '%{http_code}' --max-time 5 http://127.0.0.1:8791/health" 2>/dev/null || echo "000")
    if [[ "$AI_CODE" == "200" ]]; then break; fi
    sleep 1
done

VAULT_CODE="000"
for i in {1..10}; do
    VAULT_CODE=$(ssh ${SSH_OPTS} "${SERVER}" "curl -s -o /dev/null -w '%{http_code}' --max-time 5 http://127.0.0.1:8790/health" 2>/dev/null || echo "000")
    if [[ "$VAULT_CODE" == "200" ]]; then break; fi
    sleep 1
done

WEBUI_CODE="000"
for i in {1..10}; do
    WEBUI_CODE=$(ssh ${SSH_OPTS} "${SERVER}" "curl -s -o /dev/null -w '%{http_code}' --max-time 5 http://127.0.0.1:5177/" 2>/dev/null || echo "000")
    if [[ "$WEBUI_CODE" == "200" || "$WEBUI_CODE" == "302" ]]; then break; fi
    sleep 1
done

if [[ "$FAMILY_CODE" != "200" ]]; then
    echo "ERROR: Baihua.Family HTTP 检查失败 (status=$FAMILY_CODE)"
    exit 1
fi

if [[ "$AI_CODE" != "200" ]]; then
    echo "ERROR: Baihua.AI HTTP 检查失败 (status=$AI_CODE)"
    exit 1
fi

if [[ "$VAULT_CODE" != "200" ]]; then
    echo "ERROR: Baihua.Vault HTTP 检查失败 (status=$VAULT_CODE)"
    exit 1
fi

if [[ "$WEBUI_CODE" != "200" && "$WEBUI_CODE" != "302" ]]; then
    echo "ERROR: WebUI HTTP 检查失败 (status=$WEBUI_CODE)"
    exit 1
fi

# 清理临时文件
rm -f "${TMP_PKG}"
ssh ${SSH_OPTS} "${SERVER}" "rm -f ${REMOTE_SRC_DIR}/baihua-src.tar.gz"

echo "========================================"
echo "Family Docker 化部署成功！"
echo "  Baihua.Family: http://127.0.0.1:8788 正常 (HTTP $FAMILY_CODE)"
echo "  Baihua.AI:     http://127.0.0.1:8791 正常 (HTTP $AI_CODE)"
echo "  Baihua.Vault:  http://127.0.0.1:8790 正常 (HTTP $VAULT_CODE)"
echo "  WebUI:             http://127.0.0.1:5177 正常 (HTTP $WEBUI_CODE)"
echo "  Nginx:             80 端口 (HTTP 反向代理)"
echo "  OpenObserve:       http://127.0.0.1:5082"
echo ""
echo "数据目录: ${REMOTE_DATA_DIR}"
echo "日志目录: ${REMOTE_LOGS_DIR}"
echo "配置目录: ${REMOTE_CONFIG_DIR}"
echo ""
echo "常用命令:"
echo "  ssh ${SERVER} 'cd ${REMOTE_SRC_DIR}/family/docker && docker compose logs -f'"
echo "  ssh ${SERVER} 'cd ${REMOTE_SRC_DIR}/family/docker && docker compose ps'"
echo "========================================"
