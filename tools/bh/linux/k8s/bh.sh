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
#   update      git pull + build + deploy（pull 以真实用户执行，build/deploy 自动提权，sudo 与否均可）
#   status      pods / svc / pvc overview
#   logs <svc> [n]   tail pod logs (default 50)
#   destroy     delete namespace baihua
#   dashboard   open browser with cli-token auto-login
#   openvino <on|off|status>   按需启停 Intel GPU 相关服务（10-intel-gpu-plugin + bh-openvino）
#                              deploy 时自动探测：无 Intel GPU 则跳过；可用 BAIHUA_ENABLE_OPENVINO=1/0 强制
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
    sed -n 's/^#   //p' "$0" | grep -E '^[a-z]'
}

# WSL2 判定：内核版本/ /proc/version 含 microsoft，或 systemd-detect-virt=wsl
# （不能拿 /dev/dxg 当依据——k8s hostPath DirectoryOrCreate 会在原生 Linux 上建出空目录）
is_wsl() {
    if [ -f /proc/sys/kernel/osrelease ] && grep -qi microsoft /proc/sys/kernel/osrelease 2>/dev/null; then
        return 0
    fi
    if [ -f /proc/version ] && grep -qi microsoft /proc/version 2>/dev/null; then
        return 0
    fi
    if command -v systemd-detect-virt >/dev/null 2>&1; then
        [ "$(systemd-detect-virt)" = "wsl" ] && return 0
    fi
    return 1
}

# Intel GPU 探测：决定是否部署/启动 openvino 相关服务（10-intel-gpu-plugin + 22a-openvino）
# 顺序：显式开关 BAIHUA_ENABLE_OPENVINO（1/0）> WSL2 GPU-PV（内核判定 + /dev/dxg 字符设备）> 真机 /dev/dri + lspci 厂商为 Intel
has_intel_gpu() {
    case "${BAIHUA_ENABLE_OPENVINO:-auto}" in
        1|true|yes|on)  echo "[gpu] BAIHUA_ENABLE_OPENVINO=on 强制启用 openvino 服务"; return 0 ;;
        0|false|no|off) echo "[gpu] BAIHUA_ENABLE_OPENVINO=off 强制停用 openvino 服务"; return 1 ;;
    esac
    # WSL2：GPU-PV 走 /dev/dxg（必须是字符设备；空目录是 k8s hostPath 建的，不作数）
    if is_wsl; then
        if [ -c /dev/dxg ]; then
            echo "[gpu] WSL2 GPU-PV：/dev/dxg 字符设备"
            return 0
        fi
        echo "[gpu] WSL2 未检测到 /dev/dxg 字符设备，继续查 /dev/dri"
    fi
    # 真机 / 直通：无 /dev/dri 渲染节点 → 无 GPU
    if [ ! -d /dev/dri ] || ! ls /dev/dri/renderD* >/dev/null 2>&1; then
        echo "[gpu] 未检测到 /dev/dri 渲染节点"
        return 1
    fi
    # Intel 厂商判断（lspci 可用时）；lspci 缺失时以 /dev/dri 存在为准
    if command -v lspci >/dev/null 2>&1; then
        if lspci | grep -qiE '(vga|3d|display).*intel'; then
            echo "[gpu] 检测到 Intel GPU（lspci）"
            return 0
        fi
        echo "[gpu] 有 /dev/dri 但非 Intel 显卡（openvino 需要 Intel GPU，跳过）"
        return 1
    fi
    echo "[gpu] 检测到 /dev/dri（无 lspci，按有 GPU 处理）"
    return 0
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
# 标准构建：镜像 unpack 进 k3s containerd（-o type=image 导出不可靠——tag 不更新会部署到旧镜像）
build_all() {
    if [ "$(id -u)" != "0" ]; then
        echo "[build] 需要 root 权限（k3s containerd socket $K3S_CONTAINERD_SOCK 仅 root 可读）" >&2
        echo "        请用: sudo $0 build   或直接 bh update（自动提权）" >&2
        exit 1
    fi
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
    n build -f "$IMAGE_DIR/Dockerfile.vault"     -t bh-vault:latest    "$ROOT" >/dev/null || exit 1
    echo "[build] bh-vault"
    n build -f "$IMAGE_DIR/Dockerfile.ai"        -t bh-ai:latest       "$ROOT" >/dev/null || exit 1
    echo "[build] bh-ai"
    n build -f "$IMAGE_DIR/Dockerfile.webui"     -t bh-webui:latest    "$ROOT" >/dev/null || exit 1
    echo "[build] bh-webui"
    n build -f "$IMAGE_DIR/Dockerfile.family"    -t bh-family:latest   "$ROOT" >/dev/null || exit 1
    echo "[build] bh-family"
    n build -f "$IMAGE_DIR/Dockerfile.openvino-server" -t bh-openvino:latest "$ROOT" >/dev/null || exit 1  # COPY services/... 需仓库根上下文
    echo "[build] bh-openvino"
    echo "[build] 5 images done (已直接进入 k3s containerd，无需 load)"
}

deploy_all() {
    if [ "$(id -u)" != "0" ]; then
        echo "[deploy] 需要 root 权限（/etc/rancher/k3s/k3s.yaml 仅 root 可读）" >&2
        echo "        请用: sudo $0 deploy   或直接 bh update（自动提权）" >&2
        exit 1
    fi
    # 基础清单（与 GPU 无关，始终部署）
    for m in 00-namespace.yaml 01-configmap.yaml 02-secret.yaml 03-pvc.yaml \
             20-vault.yaml 21-ai.yaml 22-family.yaml 23-webui.yaml 24-traefik.yaml; do
        echo "[deploy] $m"
        k apply -f "$K8S_DIR/$m" >/dev/null || exit 1
    done
    # GPU 按需：有 Intel GPU 才部署 intel-gpu-plugin（kube-system）+ bh-openvino
    if has_intel_gpu; then
        for m in 10-intel-gpu-plugin.yaml 22a-openvino.yaml; do
            echo "[deploy] $m"
            k apply -f "$K8S_DIR/$m" >/dev/null || exit 1
        done
    else
        echo "[deploy] 无 Intel GPU：跳过 openvino 相关服务（10-intel-gpu-plugin / 22a-openvino）"
        # 之前部署过的话停掉，避免在无 GPU 节点上空转/崩溃循环
        # （DaemonSet 不支持 scale，用 delete --ignore-not-found；on 时 apply 清单会重建）
        k -n kube-system delete ds intel-gpu-plugin --ignore-not-found >/dev/null 2>&1 && \
            echo "[deploy] intel-gpu-plugin 已删除（无 GPU）"
        k -n "$NAMESPACE" scale deploy bh-openvino --replicas=0 >/dev/null 2>&1 && \
            echo "[deploy] bh-openvino 已缩容至 0"
    fi
    echo "[deploy] 滚动重启应用新镜像（本地 :latest 镜像不重启不会生效）"
    # 显式列出应用 deployment，避免误重启 bh-postgres（数据库无需随应用重建而重启）
    k -n "$NAMESPACE" rollout restart deployment bh-vault bh-ai bh-webui bh-family bh-openvino >/dev/null 2>&1 || true
    echo "[deploy] 等待应用滚动完成（rollout status，确保新 pod 全部就绪）..."
    k -n "$NAMESPACE" rollout status deployment bh-vault bh-ai bh-webui bh-family bh-openvino --timeout=300s \
        || echo "[deploy] 部分 deployment 未在 300s 内就绪（可稍后 bh status 复查，或 bh logs <svc> 查看原因）"
    status_all
}

# openvino 按需启停：on 启动（无 GPU 时拒绝，除非 BAIHUA_ENABLE_OPENVINO=1 强制），off 缩容至 0，status 查看
openvino_cmd() {
    local action="${1:-status}"
    case "$action" in
        on|start)
            if ! has_intel_gpu; then
                echo "[openvino] 未检测到 Intel GPU，拒绝启动。如确需强制请: BAIHUA_ENABLE_OPENVINO=1 $0 openvino on" >&2
                return 1
            fi
            k apply -f "$K8S_DIR/10-intel-gpu-plugin.yaml" -f "$K8S_DIR/22a-openvino.yaml" >/dev/null || exit 1
            k -n kube-system scale ds intel-gpu-plugin --replicas=1 >/dev/null 2>&1 || true
            k -n "$NAMESPACE" scale deploy bh-openvino --replicas=1 >/dev/null 2>&1
            echo "[openvino] 已启动，等待就绪（kubectl -n baihua rollout status deploy/bh-openvino）"
            ;;
        off|stop)
            k -n "$NAMESPACE" scale deploy bh-openvino --replicas=0 >/dev/null 2>&1
            # DaemonSet 不支持 scale，用 delete（on 时 apply 清单重建）
            k -n kube-system delete ds intel-gpu-plugin --ignore-not-found >/dev/null 2>&1
            echo "[openvino] 已停止（bh-openvino 缩容至 0、intel-gpu-plugin 已删除，可随时 on 恢复）"
            ;;
        status)
            echo "=== Intel GPU 探测 ==="
            has_intel_gpu
            echo ""
            echo "=== bh-openvino ==="
            k -n "$NAMESPACE" get deploy bh-openvino 2>/dev/null || echo "  (未部署)"
            k -n "$NAMESPACE" get pods -l app=bh-openvino 2>/dev/null || true
            echo ""
            echo "=== intel-gpu-plugin ==="
            k -n kube-system get ds intel-gpu-plugin 2>/dev/null || echo "  (未部署)"
            echo ""
            echo "=== 节点 Intel GPU 资源 ==="
            k get nodes -o custom-columns=NAME:.metadata.name,GPU:.status.capacity.'intel\.com/gpu' 2>/dev/null || echo "  (device-plugin 未注册)"
            ;;
        *)
            echo "用法: $0 openvino <on|off|status>"
            return 1
            ;;
    esac
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
    local token="" attempt
    # 服务可能刚滚动完还在热身（openvino 加载模型约 1 分钟），cli-token 获取失败自动重试
    for attempt in 1 2 3 4 5; do
        token=$(curl -s -m 5 -X POST "http://$host/api/auth/cli-token" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
        [ -n "$token" ] && break
        echo "[dashboard] cli-token 获取失败（第 $attempt/5 次，服务可能仍在热身），3 秒后重试 ..."
        sleep 3
    done
    if [ -n "$token" ]; then
        url="http://$host/?cli-token=$token"
        echo "[dashboard] cli-token 获取成功（5 分钟内可重复打开）"
    else
        echo "[dashboard] cli-token 获取失败（traefik :80 未就绪？先打开无 token URL）"
    fi
    echo "[dashboard] URL: $url"

    # root/sudo 下 xdg-open 无法直接访问用户桌面（X/Wayland 授权），
    # 尝试以原用户身份打开；失败则只打印 URL。
    if [ "$(id -u)" = "0" ] && [ -n "${SUDO_USER:-}" ]; then
        local uid xdgrt home
        uid="$(id -u "$SUDO_USER" 2>/dev/null || echo 0)"
        xdgrt="/run/user/$uid"
        home="$(getent passwd "$SUDO_USER" 2>/dev/null | cut -d: -f6 || echo "/home/$SUDO_USER")"
        if [ -d "$xdgrt" ]; then
            # 首选桌面 portal（GNOME/Wayland 打开 URL 的标准路径，最可靠）：
            # 直接经会话总线调 org.freedesktop.portal.OpenURI，绕开 xdg-open 的桌面探测。
            if sudo -u "$SUDO_USER" env HOME="$home" \
                DBUS_SESSION_BUS_ADDRESS="unix:path=$xdgrt/bus" \
                gdbus call --session --dest org.freedesktop.portal.Desktop \
                --object-path /org/freedesktop/portal/desktop \
                --method org.freedesktop.portal.OpenURI.OpenURI \
                "" "$url" {} >/dev/null 2>&1; then
                echo "[dashboard] 已以 $SUDO_USER 身份在桌面打开浏览器"
                return 0
            fi
            # 兜底：xdg-open（必须带全桌面环境变量，且加超时防挂死——
            # sudo 下缺 DBUS_SESSION_BUS_ADDRESS/XDG_CURRENT_DESKTOP 时 xdg-open 会阻塞）
            if sudo -u "$SUDO_USER" env HOME="$home" DISPLAY="${DISPLAY:-:0}" \
                WAYLAND_DISPLAY="${WAYLAND_DISPLAY:-wayland-0}" \
                XDG_RUNTIME_DIR="$xdgrt" \
                DBUS_SESSION_BUS_ADDRESS="unix:path=$xdgrt/bus" \
                XDG_CURRENT_DESKTOP="${XDG_CURRENT_DESKTOP:-ubuntu:GNOME}" \
                timeout 10 xdg-open "$url" >/dev/null 2>&1; then
                echo "[dashboard] 已以 $SUDO_USER 身份在桌面打开浏览器"
                return 0
            fi
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

# 找到真实用户的 SSH agent socket（sudo 默认剥离 SSH_AUTH_SOCK，而 GitHub 密钥常只存在于 agent）
find_ssh_auth_sock() {
    local uid sock
    [ -n "${SSH_AUTH_SOCK:-}" ] && [ -S "${SSH_AUTH_SOCK:-}" ] && { echo "$SSH_AUTH_SOCK"; return 0; }
    uid="$(id -u "${SUDO_USER:-$(id -un)}" 2>/dev/null || echo 1000)"
    for sock in "/run/user/$uid/gcr/ssh" "/run/user/$uid/keyring/ssh" "/run/user/$uid/gnupg/S.gpg-agent.ssh" /tmp/ssh-*/agent.*; do
        [ -S "$sock" ] && { echo "$sock"; return 0; }
    done
    return 1
}

# update：git pull + build + deploy
# 关键：git pull 必须以真实用户身份执行（root 的 SSH 密钥通常无 GitHub 权限，会 Permission denied）；
#       build/deploy 需要 root（k3s containerd socket / k3s.yaml 仅 root 可读），非 root 自动提权。
# 因此 update 无论 sudo 与否都能跑通：sudo bh update / bh update 均可。
update_all() {
    if [ "$(id -u)" = "0" ] && [ -n "${SUDO_USER:-}" ]; then
        local sock
        sock="$(find_ssh_auth_sock)" || {
            echo "[update] 找不到 $SUDO_USER 的 ssh-agent socket，git pull 无法认证" >&2
            echo "        请改用: bh update（不带 sudo，脚本会自动提权 build/deploy）" >&2
            return 1
        }
        echo "[update] git pull 以 $SUDO_USER 身份执行（agent: $sock）"
        sudo -u "$SUDO_USER" SSH_AUTH_SOCK="$sock" -H git pull origin main || { echo "[update] git pull 失败"; return 1; }
    else
        git pull origin main || { echo "[update] git pull 失败"; return 1; }
    fi
    local self
    self="$(readlink -f "$0")"
    if [ "$(id -u)" != "0" ]; then
        echo "[update] build/deploy 需要 root，自动提权执行"
        sudo "$self" build || return 1
        sudo "$self" deploy || return 1
    else
        build_all || return 1
        deploy_all || return 1
    fi
    echo "[update] 完成"
}

case "${1:-help}" in
    build)     build_all ;;
    deploy)    deploy_all ;;
    up)        build_all; deploy_all ;;
    update)    update_all ;;
    status)    status_all ;;
    openvino)  openvino_cmd "${2:-status}" ;;
    logs)      show_logs "${2:-bh-family}" "${3:-50}" ;;
    destroy)   k delete namespace "$NAMESPACE"; echo "[destroy] done" ;;
    dashboard) open_dashboard ;;
    help)      help_text ;;
    *)         help_text ;;
esac
