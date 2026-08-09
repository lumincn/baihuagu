#!/bin/bash
# baihua - Linux + k8s CLI
# Cell of the matrix: OS=linux, deployment=k8s
# Builds images and drives kubectl against the local cluster (k3s/kind).
#
# Usage: ./bh-linux-k8s.sh <command> [args]
#   build       docker build 5 images (docker/ prebuilt context)
#   load        load images into kind (k3s: images usually already local)
#   deploy      kubectl apply k8s/ manifests + wait ready
#   up          load + deploy
#   status      pods / svc / pvc overview
#   logs <svc> [n]   tail pod logs (default 50)
#   destroy     delete namespace baihua
#   dashboard   open browser to http://localhost:30080
#   help        this help
set -u

ROOT="$(cd "$(dirname "$0")" && pwd)"
K8S_DIR="$ROOT/k8s"
DOCKER_DIR="$ROOT/docker"
NAMESPACE="baihua"

# kubectl: PATH first, then common Windows/kind locations (WSL shares the Windows FS)
KUBECTL=""
for c in kubectl /mnt/c/Program\ Files/Docker/Docker/resources/bin/kubectl.exe; do
    if command -v "$c" >/dev/null 2>&1 || [ -x "$c" ]; then KUBECTL="$c"; break; fi
done
[ -z "$KUBECTL" ] && { echo "[k8s] kubectl not found"; exit 1; }

# kubeconfig: prefer explicit, then linux home, then Windows home
if [ -z "${KUBECONFIG:-}" ]; then
    if [ -f "$HOME/.kube/config" ]; then KUBECONFIG="$HOME/.kube/config"
    elif [ -f /mnt/c/Users/lumin/.kube/config ]; then KUBECONFIG=/mnt/c/Users/lumin/.kube/config
    fi
    [ -n "${KUBECONFIG:-}" ] && export KUBECONFIG
fi

IMAGES="bh-vault:latest bh-ai:latest bh-webui:latest bh-family:latest bh-openvino:latest"

help_text() {
    sed -n 's/^#   //p' "$0" | sed -n '2,12p'
}

build_all() {
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
    if command -v kind >/dev/null 2>&1; then
        for img in $IMAGES; do kind load docker-image "$img" >/dev/null 2>&1 && echo "[load] $img"; done
    else
        echo "[load] kind CLI not found; if using k3s images are usually already visible, skipping"
    fi
}

deploy_all() {
    for m in 00-namespace.yaml 01-configmap.yaml 02-secret.yaml 03-pvc.yaml 10-intel-gpu-plugin.yaml \
             20-vault.yaml 21-ai.yaml 22a-openvino.yaml 22-family.yaml 23-webui.yaml 24-nginx-configmap.yaml 25-nginx.yaml; do
        echo "[deploy] $m"
        "$KUBECTL" apply -f "$K8S_DIR/$m" >/dev/null || exit 1
    done
    echo "[deploy] waiting for pods ..."
    "$KUBECTL" -n "$NAMESPACE" wait --for=condition=ready pod -l app.kubernetes.io/part-of=baihua --timeout=300s || echo "[deploy] some pods not ready (see status)"
    status_all
}

status_all() {
    echo "=== pods ==="
    "$KUBECTL" -n "$NAMESPACE" get pods -o wide
    echo ""
    echo "=== svc ==="
    "$KUBECTL" -n "$NAMESPACE" get svc
    echo ""
    echo "=== pvc ==="
    "$KUBECTL" -n "$NAMESPACE" get pvc
    echo ""
    echo "entry: http://localhost:30080"
}

show_logs() {
    "$KUBECTL" -n "$NAMESPACE" logs -l "app=${1:-bh-family}" --tail="${2:-50}" --all-containers=true
}

case "${1:-help}" in
    build)     build_all ;;
    load)      load_all ;;
    deploy)    deploy_all ;;
    up)        load_all; deploy_all ;;
    status)    status_all ;;
    logs)      show_logs "${2:-bh-family}" "${3:-50}" ;;
    destroy)   "$KUBECTL" delete namespace "$NAMESPACE"; echo "[destroy] done" ;;
    dashboard) (xdg-open http://localhost:30080 >/dev/null 2>&1 &) || true ;;
    help)      help_text ;;
    *)         help_text ;;
esac
