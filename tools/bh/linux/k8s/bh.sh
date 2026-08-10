#!/bin/bash
# baihua - Linux + k3s CLI
# Cell of the matrix: OS=linux, deployment=k8s (k3s + containerd, 无 docker 依赖)
#
# 镜像构建用 nerdctl 直连 k3s 的 containerd socket（/run/k3s/containerd/containerd.sock），
# 构建完镜像直接落在 k3s 的 containerd 存储里，无需 docker build / docker save / ctr import。
# 前置：k3s 已安装运行；nerdctl 已安装（k3s 不附带，需单独装）。
#
# Usage: ./tools/bh/linux/k8s/bh.sh <command> [args]
#   build       nerdctl 构建 5 个镜像（直接进 k3s containerd）
#   deploy      kubectl apply k8s/ manifests + wait ready
#   up          build + deploy
#   status      pods / svc / pvc overview
#   logs <svc> [n]   tail pod logs (default 50)
#   destroy     delete namespace baihua
#   dashboard   open browser with cli-token auto-login
#   help        this help
set -u

ROOT="$(cd "$(dirname "$0")/../../../.." && pwd)"  # tools/bh/linux/k8s → 仓库根
K8S_DIR="$ROOT/k8s"
DOCKER_DIR="$ROOT/docker"
NAMESPACE="baihua"

IMAGES="bh-vault:latest bh-ai:latest bh-webui:latest bh-family:latest bh-openvino:latest"

# k3s containerd socket（k3s 默认）
K3S_CONTAINERD_SOCK="/run/k3s/containerd/containerd.sock"

# nerdctl 封装：直连 k3s containerd（在 build 时才检查，help/status 等不依赖）
n() { nerdctl -a "$K3S_CONTAINERD_SOCK" "$@"; }

# kubectl 封装：优先 k3s 自带 kubectl（k3s kubectl），再 PATH 里的 kubectl
if command -v k3s >/dev/null 2>&1; then
    if [ -z "${KUBECONFIG:-}" ] && [ -f /etc/rancher/k3s/k3s.yaml ]; then
        export KUBECONFIG=/etc/rancher/k3s/k3s.yaml
    fi
    k() { k3s kubectl "$@"; }
elif command -v kubectl >/dev/null 2>&1; then
    k() { kubectl "$@"; }
else
    echo "[k8s] 未找到 k3s / kubectl"; exit 1
fi

help_text() {
    sed -n 's/^#   //p' "$0" | sed -n '2,14p'
}

# nerdctl 直接构建进 k3s containerd（构建即入库，无 docker）
# -o type=image：产物直接写入 containerd（默认 tarball 导出在 containerd worker 下会报 content not found）
build_all() {
    if ! command -v nerdctl >/dev/null 2>&1; then
        echo "[build] 未找到 nerdctl（需单独安装：https://github.com/containerd/nerdctl）"
        echo "        k3s 不附带 nerdctl，仅自带 containerd 与 k3s ctr（ctr 不能构建镜像）"
        exit 1
    fi
    if ! n info >/dev/null 2>&1; then
        echo "[build] 无法连接 k3s containerd（$K3S_CONTAINERD_SOCK）"
        echo "        请确认 k3s 已运行，且 nerdctl 已安装"
        exit 1
    fi
    n build -o type=image -f "$DOCKER_DIR/Dockerfile.vault.prebuilt"          -t bh-vault:latest    "$DOCKER_DIR" >/dev/null || exit 1
    echo "[build] bh-vault"
    n build -o type=image -f "$DOCKER_DIR/Dockerfile.ai.prebuilt"             -t bh-ai:latest       "$DOCKER_DIR" >/dev/null || exit 1
    echo "[build] bh-ai"
    n build -o type=image -f "$DOCKER_DIR/Dockerfile.webui.prebuilt"          -t bh-webui:latest    "$DOCKER_DIR" >/dev/null || exit 1
    echo "[build] bh-webui"
    n build -o type=image -f "$DOCKER_DIR/Dockerfile.family.prebuilt"         -t bh-family:latest   "$DOCKER_DIR" >/dev/null || exit 1
    echo "[build] bh-family"
    n build -o type=image -f "$DOCKER_DIR/Dockerfile.openvino-server.prebuilt" -t bh-openvino:latest "$ROOT" >/dev/null || exit 1
    echo "[build] bh-openvino"
    echo "[build] 5 images done (已直接进入 k3s containerd，无需 load)"
}

deploy_all() {
    for m in 00-namespace.yaml 01-configmap.yaml 02-secret.yaml 03-pvc.yaml 10-intel-gpu-plugin.yaml \
             20-vault.yaml 21-ai.yaml 22a-openvino.yaml 22-family.yaml 23-webui.yaml 24-nginx-configmap.yaml 25-nginx.yaml; do
        echo "[deploy] $m"
        k apply -f "$K8S_DIR/$m" >/dev/null || exit 1
    done
    echo "[deploy] waiting for pods ..."
    k -n "$NAMESPACE" wait --for=condition=ready pod -l app.kubernetes.io/part-of=baihua --timeout=300s || echo "[deploy] some pods not ready (see status)"
    status_all
}

status_all() {
    echo "=== pods ==="
    k -n "$NAMESPACE" get pods -o wide
    echo ""
    echo "=== svc ==="
    k -n "$NAMESPACE" get svc
    echo ""
    echo "=== pvc ==="
    k -n "$NAMESPACE" get pvc
    echo ""
    echo "entry: http://localhost:30080"
}

show_logs() {
    local svc="${1:-bh-family}"
    # 自动补 bh- 前缀：logs vault → app=bh-vault
    case "$svc" in
        bh-*) ;;            # 已带前缀
        *) svc="bh-$svc" ;;
    esac
    k -n "$NAMESPACE" logs -l "app=$svc" --tail="${2:-50}" --all-containers=true
}

open_dashboard() {
    local token
    token=$(curl -s -X POST http://localhost:30080/api/auth/cli-token | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
    if [ -n "$token" ]; then
        (xdg-open "http://localhost:30080/?cli-token=$token" >/dev/null 2>&1 &) || true
        echo "[dashboard] opened with cli-token"
    else
        echo "[dashboard] cli-token failed, opening plain URL"
        (xdg-open http://localhost:30080 >/dev/null 2>&1 &) || true
    fi
}

case "${1:-help}" in
    build)     build_all ;;
    deploy)    deploy_all ;;
    up)        build_all; deploy_all ;;
    status)    status_all ;;
    logs)      show_logs "${2:-bh-family}" "${3:-50}" ;;
    destroy)   k delete namespace "$NAMESPACE"; echo "[destroy] done" ;;
    dashboard) open_dashboard ;;
    help)      help_text ;;
    *)         help_text ;;
esac
