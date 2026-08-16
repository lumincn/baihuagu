#!/bin/bash
# baihua - Linux + k3s CLI
# Cell of the matrix: OS=linux, deployment=k8s (k3s + containerd, 无 docker 依赖)
#
# 镜像构建用 nerdctl 直连 k3s 的 containerd socket（/run/k3s/containerd/containerd.sock），
# 构建完镜像直接落在 k3s 的 containerd 存储里，无需 docker build / docker save / ctr import。
# 前置：k3s 已安装运行（k3s 无法自动安装，见 k8s/README.md 前提条件）。
# 权限：k3s 的 containerd socket 与 k3s.yaml 仅 root 可访问，build/deploy/status 建议整体用 sudo 执行
#       （sudo bh build）；脚本内部对 /usr/local/bin 与 /etc 的写入会自动用 sudo，非 root 直接跑也会尽量完成。
# nerdctl / buildkit（buildkitd+buildctl）缺失时 build 会自动下载安装（GitHub release → /usr/local/bin）。
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
IMAGE_DIR="$ROOT/k8s/images"   # Dockerfile 配方 + entrypoint 全在这里
NAMESPACE="baihua"

IMAGES="bh-vault:latest bh-ai:latest bh-webui:latest bh-family:latest bh-openvino:latest"

# k3s containerd socket（k3s 默认）
K3S_CONTAINERD_SOCK="/run/k3s/containerd/containerd.sock"

# nerdctl 封装：直连 k3s containerd（在 build 时才检查，help/status 等不依赖）
# -n k8s.io：k3s 的镜像 namespace（否则 nerdctl 默认 default，看不到 k3s 的镜像）
n() { nerdctl -a "$K3S_CONTAINERD_SOCK" -n k8s.io "$@"; }

# kubectl 封装：优先 k3s 自带 kubectl（k3s kubectl），再 PATH 里的 kubectl
# 惰性解析——help 等不实际用 kubectl 的命令在 k3s 缺失时也能跑
k() {
    if command -v k3s >/dev/null 2>&1; then
        if [ -z "${KUBECONFIG:-}" ] && [ -f /etc/rancher/k3s/k3s.yaml ]; then
            export KUBECONFIG=/etc/rancher/k3s/k3s.yaml
        fi
        k3s kubectl "$@"
    elif command -v kubectl >/dev/null 2>&1; then
        kubectl "$@"
    else
        echo "[k8s] 未找到 k3s / kubectl（k3s 安装见 k8s/README.md 前提条件）" >&2
        return 1
    fi
}

help_text() {
    sed -n 's/^#   //p' "$0" | sed -n '2,14p'
}

# 自动安装缺失的构建依赖（nerdctl / buildkitd）
# 策略：GitHub release 官方 tarball → /usr/local/bin（需 root；有 sudo 自动用，否则提示）
# 不能自动安装的（k3s 系统服务）在 k8s/README.md 前提条件章节有指引
NERDCTL_VERSION="2.3.5"
BUILDKIT_VERSION="0.32.2"
ARCH="$(uname -m | sed 's/x86_64/amd64/; s/aarch64/arm64/')"

install_tool() {
    # $1=工具名 $2=下载URL $3=tarball 内二进制名
    local name="$1" url="$2" bin="$3"
    echo "[deps] $name 缺失，自动下载安装（$url）..."
    local tmp
    tmp="$(mktemp -d)"
    # GitHub 直连失败时自动换镜像加速（国内网络友好）
    local ok=0
    if curl -fsSL -o "$tmp/tool.tar.gz" "$url"; then
        ok=1
    else
        for mirror in "https://mirror.ghproxy.com/" "https://ghfast.top/" "https://ghproxy.net/"; do
            echo "[deps] GitHub 直连失败，尝试镜像: $mirror"
            if curl -fsSL -o "$tmp/tool.tar.gz" "$mirror$url"; then
                ok=1
                break
            fi
        done
    fi
    if [ "$ok" != "1" ]; then
        echo "[deps] 下载失败（直连+镜像均不可达），请手动安装 $name 后重试（见 k8s/README.md）"
        rm -rf "$tmp"
        exit 1
    fi
    # 完整性校验（tarball 可能被截断）
    if ! tar -tzf "$tmp/tool.tar.gz" >/dev/null 2>&1; then
        echo "[deps] 下载的 tarball 损坏（网络截断），请手动安装 $name 后重试"
        rm -rf "$tmp"
        exit 1
    fi
    if ! tar -xzf "$tmp/tool.tar.gz" -C "$tmp" "$bin" 2>/dev/null; then
        tar -xzf "$tmp/tool.tar.gz" -C "$tmp" || true
    fi
    local found
    found="$(find "$tmp" -name "$bin" -type f | head -1)"
    local target="/usr/local/bin/$name"
    if command -v sudo >/dev/null 2>&1; then
        sudo install -m 0755 "$found" "$target"             || { echo "[deps] 安装失败（权限？），请手动安装 $name"; rm -rf "$tmp"; exit 1; }
    elif [ "$(id -u)" = "0" ]; then
        install -m 0755 "$found" "$target"             || { echo "[deps] 安装失败，请手动安装 $name"; rm -rf "$tmp"; exit 1; }
    else
        echo "[deps] 需要 root 权限安装到 /usr/local/bin，请手动执行:"
        echo "        sudo install -m 0755 $found $target"
        rm -rf "$tmp"
        exit 1
    fi
    rm -rf "$tmp"
    echo "[deps] $name 安装完成"
}

# buildkit 全家桶：buildkitd（守护进程）+ buildctl（客户端，nerdctl build 需要）同在一个 tarball
install_buildkit() {
    local url="https://github.com/moby/buildkit/releases/download/v${BUILDKIT_VERSION}/buildkit-v${BUILDKIT_VERSION}.linux-${ARCH}.tar.gz"
    echo "[deps] buildkit 缺失，自动下载安装（buildkitd + buildctl，$url）..."
    local tmp
    tmp="$(mktemp -d)"
    local ok=0
    if curl -fsSL -o "$tmp/tool.tar.gz" "$url"; then
        ok=1
    else
        for mirror in "https://mirror.ghproxy.com/" "https://ghfast.top/" "https://ghproxy.net/"; do
            echo "[deps] GitHub 直连失败，尝试镜像: $mirror"
            if curl -fsSL -o "$tmp/tool.tar.gz" "$mirror$url"; then
                ok=1
                break
            fi
        done
    fi
    if [ "$ok" != "1" ]; then
        echo "[deps] 下载失败（直连+镜像均不可达），请手动安装 buildkit（见 k8s/README.md）"
        rm -rf "$tmp"
        exit 1
    fi
    if ! tar -tzf "$tmp/tool.tar.gz" >/dev/null 2>&1; then
        echo "[deps] 下载的 tarball 损坏（网络截断），请手动安装 buildkit 后重试"
        rm -rf "$tmp"
        exit 1
    fi
    tar -xzf "$tmp/tool.tar.gz" -C "$tmp" 2>/dev/null || true
    local install_one
    install_one() {
        local bin="$1"
        local found
        found="$(find "$tmp" -name "$bin" -type f | head -1)"
        [ -z "$found" ] && { echo "[deps] tarball 里找不到 $bin"; return 1; }
        local target="/usr/local/bin/$bin"
        if command -v sudo >/dev/null 2>&1; then
            sudo install -m 0755 "$found" "$target"
        elif [ "$(id -u)" = "0" ]; then
            install -m 0755 "$found" "$target"
        else
            echo "[deps] 需要 root 权限安装到 /usr/local/bin，请手动执行:"
            echo "        sudo install -m 0755 $found $target"
            return 1
        fi
    }
    if ! install_one buildkitd || ! install_one buildctl; then
        rm -rf "$tmp"
        exit 1
    fi
    rm -rf "$tmp"
    echo "[deps] buildkitd + buildctl 安装完成"
}

ensure_deps() {
    # nerdctl：k3s 不附带，自动装
    if ! command -v nerdctl >/dev/null 2>&1; then
        install_tool nerdctl \
            "https://github.com/containerd/nerdctl/releases/download/v${NERDCTL_VERSION}/nerdctl-${NERDCTL_VERSION}-linux-${ARCH}.tar.gz" \
            "nerdctl"
    fi
    # buildkit：buildkitd（守护进程）+ buildctl（客户端，nerdctl build 需要），一次下载装俩
    if ! command -v buildkitd >/dev/null 2>&1 || ! command -v buildctl >/dev/null 2>&1; then
        install_buildkit
    fi
    # buildkitd 必须运行（socket 可达），否则给出启动指引
    if ! command -v buildkitd >/dev/null 2>&1; then return 0; fi
    if [ ! -S /run/buildkit/buildkitd.sock ]; then
        ensure_registry
        # systemd 环境：GitHub release 的 buildkit 不带 systemd 单元文件，脚本先写入 buildkit.service 再提示 systemctl 启动
        if [ -d /run/systemd/system ]; then
            if [ ! -f /etc/systemd/system/buildkit.service ]; then
                write_root /etc/systemd/system/buildkit.service << 'EOF'
[Unit]
Description=BuildKit
Documentation=https://github.com/moby/buildkit

[Service]
ExecStart=/usr/local/bin/buildkitd -config /etc/buildkit/buildkitd.toml
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
            fi
            if [ -f /etc/systemd/system/buildkit.service ]; then
                { systemctl daemon-reload || sudo systemctl daemon-reload; } 2>/dev/null || true
                echo "[deps] buildkitd 未运行。systemd 单元已就绪，请启动："
                echo "        sudo systemctl enable --now buildkit"
                echo "        （状态: systemctl status buildkit / 日志: journalctl -u buildkit -f）"
                exit 1
            fi
            echo "[deps] buildkitd 未运行且无法写入 systemd 单元（需要 root 权限）" >&2
            exit 1
        fi
        echo "[deps] buildkitd 未运行。无 systemd 环境请手动启动："
        echo "        nohup buildkitd -config /etc/buildkit/buildkitd.toml > /tmp/buildkitd.log 2>&1 &"
        exit 1
    fi
}

# 写 root 属主的配置文件：root 直接写；有 sudo 自动用（与 install_tool 策略一致）；否则提示手动执行
write_root() {
    local target="$1"
    local dir; dir="$(dirname "$target")"
    if [ "$(id -u)" = "0" ]; then
        mkdir -p "$dir" || return 1
        cat > "$target" || return 1
    elif command -v sudo >/dev/null 2>&1; then
        sudo mkdir -p "$dir" || return 1
        sudo tee "$target" > /dev/null || return 1
    else
        echo "[deps] 需要 root 权限写入 $target（无 sudo），请手动执行:"
        echo "        sudo mkdir -p $dir && sudo tee $target"
        return 1
    fi
}

# 自动配置国内镜像加速（幂等：已存在则不动）：
#   1. /etc/rancher/k3s/registries.yaml —— k3s containerd 拉镜像走 daocloud（实测 pause/nginx 直连 docker.io 超时）
#   2. /etc/buildkit/buildkitd.toml —— buildkitd 构建时拉基础镜像走 daocloud
ensure_registry() {
    local wrote=0
    # k3s registries.yaml
    if [ ! -f /etc/rancher/k3s/registries.yaml ]; then
        write_root /etc/rancher/k3s/registries.yaml << 'EOF'
mirrors:
  docker.io:
    endpoint:
      - "https://docker.m.daocloud.io"
      - "https://docker.1ms.run"
      - "https://registry-1.docker.io"
EOF
        if [ $? -eq 0 ]; then
            echo "[deps] 已写入 /etc/rancher/k3s/registries.yaml（daocloud 镜像加速）"
            echo "        k3s 重启后生效（systemctl restart k3s 或重新拉起 k3s server）"
            wrote=1
        else
            echo "[deps] 写入 /etc/rancher/k3s/registries.yaml 失败（需要 root）" >&2
        fi
    fi
    # buildkitd.toml（仅当 buildkitd 存在时写）
    if command -v buildkitd >/dev/null 2>&1 && [ ! -f /etc/buildkit/buildkitd.toml ]; then
        write_root /etc/buildkit/buildkitd.toml << 'EOF'
# 必须禁用 OCI(runc) worker：否则 nerdctl build -o type=image 导出到 buildkit 的 OCI 存储，
# 镜像进不了 k3s containerd 的 k8s.io namespace，后续 FROM bh/* 本地镜像解析失败（会去 docker.io 拉→403）
[worker.oci]
  enabled = false

[worker.containerd]
address = "/run/k3s/containerd/containerd.sock"
namespace = "k8s.io"
[registry."docker.io"]
mirrors = ["docker.m.daocloud.io", "docker.1ms.run"]
EOF
        if [ $? -eq 0 ]; then
            echo "[deps] 已写入 /etc/buildkit/buildkitd.toml（daocloud 镜像加速 + k8s.io namespace，禁用 OCI worker）"
            wrote=1
        else
            echo "[deps] 写入 /etc/buildkit/buildkitd.toml 失败（需要 root）" >&2
        fi
    fi
    return "$wrote"
}

# nerdctl 直接构建进 k3s containerd（构建即入库，无 docker）
# -o type=image：产物直接写入 containerd（默认 tarball 导出在 containerd worker 下会报 content not found）
build_all() {
    ensure_deps
    ensure_registry
    if ! n info >/dev/null 2>&1; then
        echo "[build] 无法连接 k3s containerd（$K3S_CONTAINERD_SOCK）"
        echo "        请确认 k3s 已运行（k3s 安装见 k8s/README.md 前提条件）"
        exit 1
    fi
    # base-runtime：vault/ai/webui/family 的 FROM（运行时基础镜像）
    if ! n images | grep -qE 'bh/base-runtime\s+latest'; then
        n build -o type=image -f "$IMAGE_DIR/Dockerfile.base-runtime" -t bh/base-runtime:latest "$IMAGE_DIR" >/dev/null || exit 1
        echo "[build] bh/base-runtime"
    fi
    # sdk-offline：离线 SDK 基础镜像（nuget-local 包源经 build-context 沉底，一次性构建），.NET 镜像 build 阶段 FROM 它
    if ! n images | grep -qE 'bh/sdk-offline\s+latest'; then
        n build --build-context "nuget=$ROOT/nuget-local" -o type=image -f "$IMAGE_DIR/Dockerfile.sdk-offline" -t bh/sdk-offline:latest "$ROOT" >/dev/null || exit 1
        echo "[build] bh/sdk-offline"
    fi
    # .NET 镜像：多阶段源码构建（容器内 dotnet publish，restore 走 sdk-offline 里的离线包源），context 需仓库根（services/ 源码）
    n build -o type=image -f "$IMAGE_DIR/Dockerfile.vault"     -t bh-vault:latest    "$ROOT" >/dev/null || exit 1
    echo "[build] bh-vault"
    n build -o type=image -f "$IMAGE_DIR/Dockerfile.ai"        -t bh-ai:latest       "$ROOT" >/dev/null || exit 1
    echo "[build] bh-ai"
    n build -o type=image -f "$IMAGE_DIR/Dockerfile.webui"     -t bh-webui:latest    "$ROOT" >/dev/null || exit 1
    echo "[build] bh-webui"
    n build -o type=image -f "$IMAGE_DIR/Dockerfile.family"    -t bh-family:latest   "$ROOT" >/dev/null || exit 1
    echo "[build] bh-family"
    n build -o type=image -f "$IMAGE_DIR/Dockerfile.openvino-server" -t bh-openvino:latest "$ROOT" >/dev/null || exit 1  # COPY services/... 需仓库根上下文
    echo "[build] bh-openvino"
    echo "[build] 5 images done (已直接进入 k3s containerd，无需 load)"
}

deploy_all() {
    for m in 00-namespace.yaml 01-configmap.yaml 02-secret.yaml 03-pvc.yaml 10-intel-gpu-plugin.yaml \
             20-vault.yaml 21-ai.yaml 22a-openvino.yaml 22-family.yaml 23-webui.yaml 24-traefik.yaml; do
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
    echo "entry: http://localhost/  (Traefik :80)"
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
    # 用局域网 IP 而非 localhost：打印的 URL 在本机或局域网其它设备的浏览器都能打开
    local host="localhost"
    local lanip
    lanip="$(hostname -I 2>/dev/null | awk '{print $1}')"
    if [ -n "$lanip" ] && [[ "$lanip" =~ ^[0-9.]+$ ]]; then
        host="$lanip"
    fi
    local url="http://$host"
    local token
    token=$(curl -s -m 5 -X POST "http://$host/api/auth/cli-token" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
    if [ -n "$token" ]; then
        url="http://$host/?cli-token=$token"
        echo "[dashboard] cli-token 获取成功（5 分钟内可重复打开）"
    else
        echo "[dashboard] cli-token 获取失败（traefik :80 未就绪？先打开无 token URL）"
    fi
    echo "[dashboard] URL: $url"

    # root/sudo 下 xdg-open 无法直接访问用户桌面（X/Wayland 授权），
    # 尝试以原用户身份 + 其桌面环境打开；失败则只打印 URL
    if [ "$(id -u)" = "0" ] && [ -n "${SUDO_USER:-}" ]; then
        local uid xdgrt
        uid="$(id -u "$SUDO_USER" 2>/dev/null || echo 0)"
        xdgrt="/run/user/$uid"
        if [ -d "$xdgrt" ]; then
            sudo -u "$SUDO_USER" env DISPLAY="${DISPLAY:-:0}" WAYLAND_DISPLAY="${WAYLAND_DISPLAY:-wayland-0}" \
                XDG_RUNTIME_DIR="$xdgrt" xdg-open "$url" >/dev/null 2>&1 && {
                echo "[dashboard] 已以 $SUDO_USER 身份在桌面打开浏览器"
                return 0
            }
        fi
        echo "[dashboard] 无法代开浏览器（root 无桌面授权），请复制上面 URL 手动打开，或退出 sudo 重跑: bh dashboard"
        return 0
    fi

    if command -v xdg-open >/dev/null 2>&1 && xdg-open "$url" >/dev/null 2>&1; then
        echo "[dashboard] 已在默认浏览器打开"
    else
        echo "[dashboard] 无法自动打开浏览器（无 GUI 或未设 DISPLAY），请复制上面 URL 手动打开"
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
