#!/bin/bash
# baihua - Linux + k8s CLI
# Cell of the matrix: OS=linux, deployment=k8s
# Builds images and drives kubectl against the local cluster (k3s/kind).
#
# Usage: ./bh-linux-k8s.sh <command> [args]
#   build       docker build 5 images (docker/ prebuilt context)
#   load        load images into k3s/kind (k3s: docker save | ctr import)
#   deploy      kubectl apply k8s/ manifests + wait ready
#   up          load + deploy
#   status      pods / svc / pvc overview
#   logs <svc> [n]   tail pod logs (default 50)
#   destroy     delete namespace baihua
#   dashboard   open browser with cli-token auto-login
#   help        this help
set -u

ROOT="$(cd "$(dirname "$0")" && pwd)"
K8S_DIR="$ROOT/k8s"
DOCKER_DIR="$ROOT/docker"
NAMESPACE="baihua"

IMAGES="bh-vault:latest bh-ai:latest bh-webui:latest bh-family:latest bh-openvino:latest"

# kubectl 封装：优先 k3s 自带 kubectl（k3s kubectl），再 PATH 里的 kubectl，最后 Windows 侧 kubectl.exe
K3S_MODE=0
if command -v k3s >/dev/null 2>&1; then
    K3S_MODE=1
    if [ -z "${KUBECONFIG:-}" ] && [ -f /etc/rancher/k3s/k3s.yaml ]; then
        export KUBECONFIG=/etc/rancher/k3s/k3s.yaml
    fi
    k() { k3s kubectl "$@"; }
elif command -v kubectl >/dev/null 2>&1; then
    k() { kubectl "$@"; }
elif [ -x "/mnt/c/Program Files/Docker/Docker/resources/bin/kubectl.exe" ]; then
    k() { "/mnt/c/Program Files/Docker/Docker/resources/bin/kubectl.exe" "$@"; }
else
    echo "[k8s] 未找到 k3s / kubectl"; exit 1
fi

help_text() {
    sed -n 's/^#   //p' "$0" | sed -n '2,12p'
}

build_all() {
    # 注意：需在能访问 docker 的环境执行（Windows 或启用了 WSL 集成的发行版）
    docker build -f "$DOCKER_DIR/Dockerfile.vault.prebuilt"          -t bh-vault:latest    "$DOCKER_DIR" >/dev/null || exit 1
    echo "[build] bh-vault"
    docker build -f "$DOCKER_DIR/Dockerfile.taskrunner.ai.prebuilt"  -t bh-ai:latest       "$DOCKER_DIR" >/dev/null || exit 1
    echo "[build] bh-ai"
    docker build -f "$DOCKER_DIR/Dockerfile.webui.prebuilt"          -t bh-webui:latest    "$DOCKER_DIR" >/dev/null || exit 1
    echo "[build] bh-webui"
    docker build -f "$DOCKER_DIR/Dockerfile.family.prebuilt"         -t bh-family:latest   "$DOCKER_DIR" >/dev/null || exit 1
    echo "[build] bh-family"
    docker build -f "$DOCKER_DIR/Dockerfile.openvino-server.prebuilt" -t bh-openvino:latest "$ROOT" >/dev/null || exit 1
    echo "[build] bh-openvino"
    echo "[build] 5 images done"
}

load_all() {
    if [ "$K3S_MODE" = 1 ]; then
        # k3s：docker save | k3s ctr images import（若 WSL 内 docker 不可用，见下方提示）
        if ! docker info >/dev/null 2>&1; then
            echo "[load] WSL 内 docker 不可用（Docker Desktop 未启用此发行版 WSL 集成）。"
            echo "       请在 Windows 侧执行："
            echo "       docker save $IMAGES | wsl -e bash -lc 'k3s ctr images import -'"
            exit 1
        fi
        for img in $IMAGES; do
            docker save "$img" | k3s ctr images import - >/dev/null 2>&1 && echo "[load] $img" || echo "[load] FAILED: $img"
        done
    elif command -v kind >/dev/null 2>&1; then
        for img in $IMAGES; do kind load docker-image "$img" >/dev/null 2>&1 && echo "[load] $img"; done
    else
        echo "[load] 未检测到 k3s/kind，跳过"
    fi
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
    k -n "$NAMESPACE" logs -l "app=${1:-bh-family}" --tail="${2:-50}" --all-containers=true
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
    load)      load_all ;;
    deploy)    deploy_all ;;
    up)        load_all; deploy_all ;;
    status)    status_all ;;
    logs)      show_logs "${2:-bh-family}" "${3:-50}" ;;
    destroy)   k delete namespace "$NAMESPACE"; echo "[destroy] done" ;;
    dashboard) open_dashboard ;;
    help)      help_text ;;
    *)         help_text ;;
esac
