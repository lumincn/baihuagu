#!/bin/bash
# baihua - Linux + dotnet native CLI
# Cell of the matrix: OS=linux, deployment=dotnet-native
# Manages the 4 .NET services (vault/ai/family/webui) as local processes.
#
# Usage: ./tools/bh/linux/native/bh.sh <command> [args]
#   build       dotnet publish the 4 services to out/native/
#   start       start all 4 services (nohup + pid files)
#   stop        stop all 4 services
#   restart     stop + start
#   status      show port/process state per service
#   logs <svc> [n]   tail service log (default 50 lines)
#   dashboard   open browser with cli-token auto-login
#   open        open browser to http://localhost:5177
#   help        this help
set -u

ROOT="$(cd "$(dirname "$0")/../../../.." && pwd)"  # tools/bh/linux/native → 仓库根
OUT_DIR="$ROOT/out/native"
PID_DIR="$OUT_DIR/pids"
LOG_DIR="$OUT_DIR/logs"
DATA_HOME="${BAIHUA_HOME:-$HOME/.baihua}"

# 依赖链 ai -> vault -> family -> webui（vault 语义搜索经 HTTP 调 AI；family 转发 /mg/* 到 vault 并调 AI；webui 调三者）
# 数组即启动顺序（被依赖的先启动）；stop_all 逆序遍历（依赖者先停）-> webui -> family -> vault -> ai
SERVICES="ai:services/Baihua.AI:bh-ai:8791 vault:services/Baihua.Vault:bh-vault:8790 family:services/Baihua.Family:bh-family:8788 webui:services/Baihua.Web:bh-webui:5177"

help_text() {
    sed -n 's/^#   //p' "$0" | sed -n '2,12p'
}

port_open() {
    (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null && { exec 3>&- 3<&-; return 0; } || return 1
}

wait_port() {
    local port=$1 secs=$2
    for _ in $(seq 1 $((secs * 2))); do
        port_open "$port" && return 0
        sleep 0.5
    done
    return 1
}

# 等待端口关闭（restart 时 kill 后 socket 释放有延迟，避免 start_one 误判 already in use）
wait_port_closed() {
    local port=$1 secs=$2
    for _ in $(seq 1 $((secs * 2))); do
        port_open "$port" || return 0
        sleep 0.5
    done
    return 1
}

ensure_dotnet() {
    if command -v dotnet >/dev/null 2>&1; then return 0; fi
    echo "[deps] dotnet 缺失，自动安装到 ~/.dotnet（dotnet-install.sh）..."
    local tmp
    tmp="$(mktemp -d)"
    if ! curl -fsSL -o "$tmp/dotnet-install.sh" https://dot.net/v1/dotnet-install.sh; then
        echo "[deps] 下载 dotnet-install.sh 失败，请手动安装 .NET SDK 10（见 README）"
        rm -rf "$tmp"
        exit 1
    fi
    bash "$tmp/dotnet-install.sh" --channel 10.0 --install-dir "$HOME/.dotnet"         || { echo "[deps] dotnet 安装失败，请手动安装"; rm -rf "$tmp"; exit 1; }
    rm -rf "$tmp"
    echo "[deps] dotnet 已安装到 ~/.dotnet，请将以下加入 shell 配置（~/.bashrc）："
    echo '        export PATH="$HOME/.dotnet:$PATH"'
    export PATH="$HOME/.dotnet:$PATH"
}

# 一键更新：git pull 最新代码 → 重新构建 → 重启（供局域网内其他百花机器升级用）
update_all() {
    local root
    root="$(cd "$(dirname "$0")/../../../.." && pwd)"
    echo "[update] git pull origin main ..."
    git -C "$root" pull origin main || { echo "[update] git pull 失败，请检查 .9 的网络/代理"; exit 1; }
    echo "[update] build ..."
    build_all
    echo "[update] restart ..."
    stop_all
    start_all
    echo "[update] done"
}

build_all() {
    ensure_dotnet
    for entry in $SERVICES; do
        IFS=: read -r name proj exe port <<<"$entry"
        echo "[build] $name ..."
        dotnet publish "$proj" -c Release -r linux-x64 --self-contained false -o "$OUT_DIR/$name" >/dev/null 2>&1 || { echo "[build] FAILED: $name"; exit 1; }
    done
    echo "[build] done -> $OUT_DIR"
}

start_one() {
    IFS=: read -r name proj exe port <<<"$1"
    local bin="$OUT_DIR/$name/$exe"
    [ -x "$bin" ] || { echo "[$name] not built: $bin (run build first)"; exit 1; }
    if port_open "$port"; then
        # 端口被占：若是我们的残留进程（pid 文件在且进程活着），补杀后重试；否则跳过
        local pf="$PID_DIR/$name.pid"
        if [ -f "$pf" ]; then
            local oldpid
            oldpid=$(cat "$pf")
            if kill -0 "$oldpid" 2>/dev/null; then
                echo "[$name] port $port 被残留进程 $oldpid 占用，补杀后重试"
                kill "$oldpid" 2>/dev/null
                if ! wait_port_closed "$port" 10; then echo "[$name] port $port 仍被占用，跳过"; return; fi
            else
                echo "[$name] port $port already in use, skip"; return
            fi
        else
            echo "[$name] port $port already in use, skip"; return
        fi
    fi
    mkdir -p "$PID_DIR" "$LOG_DIR" "$DATA_HOME"
    # family 是跨机入口（算力池 /mg/capabilities、/mg/ai/、/mg/pool/、服务器互联），
    # 必须绑定 0.0.0.0 才能被局域网内其他百花服务器访问；其余服务保持回环。
    local bind="127.0.0.1"
    [ "$name" = "family" ] && bind="0.0.0.0"
    local envs=(BAIHUA_HOME="$DATA_HOME" BAIHUA_SKIP_MUTEX=true ASPNETCORE_URLS="http://$bind:$port" OpenObserve__Enabled=false)
    case "$name" in
        family) envs+=(BAIHUA_VAULT_URL=http://127.0.0.1:8790 BAIHUA_AI_URL=http://127.0.0.1:8791) ;;
        webui)  envs+=(WEBUI_CONFIG_DIR="$DATA_HOME" FamilyApi__BaseUrl=http://127.0.0.1:8788/ AiApi__BaseUrl=http://127.0.0.1:8791/ VaultApi__BaseUrl=http://127.0.0.1:8790/) ;;
    esac
    nohup env "${envs[@]}" "$bin" >>"$LOG_DIR/$name.log" 2>&1 &
    echo $! >"$PID_DIR/$name.pid"
    echo "[$name] started pid=$! port=$port log=$LOG_DIR/$name.log"
}

start_all() {
    for entry in $SERVICES; do start_one "$entry"; done
    echo "[start] waiting for health ..."
    local ok=1
    for entry in $SERVICES; do
        IFS=: read -r name proj exe port <<<"$entry"
        if wait_port "$port" 60; then echo "[$name] ready on $port"; else echo "[$name] NOT ready in 60s"; ok=0; fi
    done
    [ "$ok" = 1 ] && echo "[start] all services up. WebUI: http://localhost:5177"
}

stop_all() {
    # 停止顺序与启动相反：先停依赖者（webui/family），被依赖的（ai/vault）最后停，
    # 避免停止过程中仍有服务在调用已死的下游（如 family 转发 /mg/* 到 vault）。
    local entries=()
    for entry in $SERVICES; do entries+=("$entry"); done
    for ((i = ${#entries[@]} - 1; i >= 0; i--)); do
        IFS=: read -r name proj exe port <<<"${entries[$i]}"
        local pf="$PID_DIR/$name.pid"
        if [ -f "$pf" ]; then
            local pid
            pid=$(cat "$pf")
            kill "$pid" 2>/dev/null && echo "[$name] stopped pid=$pid" || echo "[$name] pid $pid not running"
            rm -f "$pf"
        fi
    done
    # 等待端口全部释放（kill 后 socket 关闭有延迟，restart 立即 start 会误判 already in use）
    for entry in $SERVICES; do
        IFS=: read -r name proj exe port <<<"$entry"
        if ! wait_port_closed "$port" 15; then echo "[$name] port $port 15s 内未释放（有残留进程？）"; fi
    done
    echo "[stop] done"
}

status_all() {
    for entry in $SERVICES; do
        IFS=: read -r name proj exe port <<<"$entry"
        local state=stopped pid=""
        if [ -f "$PID_DIR/$name.pid" ]; then
            pid=$(cat "$PID_DIR/$name.pid" | tr -d "\r")
            kill -0 "$pid" 2>/dev/null && state=proc-alive
        fi
        port_open "$port" && state=RUNNING
        printf "%-8s port=%-5s %s%s\n" "$name" "$port" "$state" "${pid:+ (pid=${pid})}"
    done
    # OpenVINO 宿主（独立手动管理，bh 仅展示状态——对齐 win/native；端口 8866）
    local ov_port=8866
    if port_open "$ov_port"; then
        printf "%-8s port=%-5s %s\n" "openvino" "$ov_port" "RUNNING (port $ov_port)"
    else
        printf "%-8s port=%-5s %s\n" "openvino" "$ov_port" "stopped"
    fi
}

show_logs() {
    local name=$1 n=${2:-50}
    local log="$LOG_DIR/$name.log"
    [ -f "$log" ] || { echo "no log yet: $log"; return; }
    tail -n "$n" "$log"
}

open_dashboard() {
    local token
    token=$(curl -s -m 5 -X POST http://127.0.0.1:5177/api/auth/cli-token | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')
    if [ -n "$token" ]; then
        open_browser "http://127.0.0.1:5177/?cli-token=$token"
        echo "[dashboard] opened with cli-token"
    else
        open_browser "http://127.0.0.1:5177"
        echo "[dashboard] cli-token failed, opened plain URL"
    fi
}

# 在用户桌面打开浏览器：普通用户直接 xdg-open；
# root/sudo 下优先走桌面 portal（GNOME 标准路径），兜底 xdg-open 并补全
# 桌面环境变量 + 超时保护（sudo 下缺 DBUS/XDG_CURRENT_DESKTOP 时 xdg-open 会挂死）。
open_browser() {
    local url="$1"
    if [ "$(id -u)" = "0" ] && [ -n "${SUDO_USER:-}" ]; then
        local uid xdgrt home
        uid="$(id -u "$SUDO_USER" 2>/dev/null || echo 0)"
        xdgrt="/run/user/$uid"
        home="$(getent passwd "$SUDO_USER" 2>/dev/null | cut -d: -f6 || echo "/home/$SUDO_USER")"
        [ -d "$xdgrt" ] || return 0
        if sudo -u "$SUDO_USER" env HOME="$home" \
            DBUS_SESSION_BUS_ADDRESS="unix:path=$xdgrt/bus" \
            gdbus call --session --dest org.freedesktop.portal.Desktop \
            --object-path /org/freedesktop/portal/desktop \
            --method org.freedesktop.portal.OpenURI.OpenURI \
            "" "$url" {} >/dev/null 2>&1; then
            return 0
        fi
        sudo -u "$SUDO_USER" env HOME="$home" DISPLAY="${DISPLAY:-:0}" \
            WAYLAND_DISPLAY="${WAYLAND_DISPLAY:-wayland-0}" \
            XDG_RUNTIME_DIR="$xdgrt" \
            DBUS_SESSION_BUS_ADDRESS="unix:path=$xdgrt/bus" \
            XDG_CURRENT_DESKTOP="${XDG_CURRENT_DESKTOP:-ubuntu:GNOME}" \
            timeout 10 xdg-open "$url" >/dev/null 2>&1
        return 0
    fi
    (xdg-open "$url" >/dev/null 2>&1 &) || true
}

case "${1:-help}" in
    build)     build_all ;;
    start)     start_all ;;
    stop)      stop_all ;;
    restart)   stop_all; start_all ;;
    update)    update_all ;;
    status)    status_all ;;
    logs)      show_logs "${2:-family}" "${3:-50}" ;;
    dashboard) open_dashboard ;;
    open)      (xdg-open http://localhost:5177 >/dev/null 2>&1 &) || true ;;
    help)      help_text ;;
    *)         help_text ;;
esac
