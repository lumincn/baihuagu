#!/bin/bash
# baihua - Linux + dotnet native CLI
# Cell of the matrix: OS=linux, deployment=dotnet-native
# Manages the 4 .NET services (vault/ai/family/webui) as local processes.
#
# Usage: ./bh-linux-native.sh <command> [args]
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

ROOT="$(cd "$(dirname "$0")" && pwd)"
OUT_DIR="$ROOT/out/native"
PID_DIR="$OUT_DIR/pids"
LOG_DIR="$OUT_DIR/logs"
DATA_HOME="${BAIHUA_HOME:-$HOME/.baihua}"

SERVICES="vault:services/Baihua.Vault:bh-vault:8790 ai:services/Baihua.AI:bh-ai:8791 family:services/Baihua.Family:bh-family:8788 webui:services/Baihua.Web:bh-webui:5177"

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

build_all() {
    if ! command -v dotnet >/dev/null; then echo "[build] dotnet not found"; exit 1; fi
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
    port_open "$port" && { echo "[$name] port $port already in use, skip"; return; }
    mkdir -p "$PID_DIR" "$LOG_DIR" "$DATA_HOME"
    local envs=(BAIHUA_HOME="$DATA_HOME" TASKRUNNER_SKIP_MUTEX=true ASPNETCORE_URLS="http://127.0.0.1:$port" OpenObserve__Enabled=false)
    case "$name" in
        family) envs+=(TASKRUNNER_VAULT_URL=http://127.0.0.1:8790 TASKRUNNER_AI_URL=http://127.0.0.1:8791) ;;
        webui)  envs+=(WEBUI_CONFIG_DIR="$DATA_HOME" TaskRunnerApi__BaseUrl=http://127.0.0.1:8788/ TaskRunnerAiApi__BaseUrl=http://127.0.0.1:8791/ TaskRunnerVaultApi__BaseUrl=http://127.0.0.1:8790/) ;;
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
    for entry in $SERVICES; do
        IFS=: read -r name proj exe port <<<"$entry"
        local pf="$PID_DIR/$name.pid"
        if [ -f "$pf" ]; then
            local pid
            pid=$(cat "$pf")
            kill "$pid" 2>/dev/null && echo "[$name] stopped pid=$pid" || echo "[$name] pid $pid not running"
            rm -f "$pf"
        fi
    done
    echo "[stop] done"
}

status_all() {
    for entry in $SERVICES; do
        IFS=: read -r name proj exe port <<<"$entry"
        local state=stopped pid=""
        if [ -f "$PID_DIR/$name.pid" ]; then
            pid=$(cat "$PID_DIR/$name.pid")
            kill -0 "$pid" 2>/dev/null && state=proc-alive
        fi
        port_open "$port" && state=RUNNING
        printf "%-8s port=%-5s %s%s\n" "$name" "$port" "$state" "${pid:+ (pid=$pid)}"
    done
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
        (xdg-open "http://127.0.0.1:5177/?cli-token=$token" >/dev/null 2>&1 &) || true
        echo "[dashboard] opened with cli-token"
    else
        (xdg-open "http://127.0.0.1:5177" >/dev/null 2>&1 &) || true
        echo "[dashboard] cli-token failed, opened plain URL"
    fi
}

case "${1:-help}" in
    build)     build_all ;;
    start)     start_all ;;
    stop)      stop_all ;;
    restart)   stop_all; start_all ;;
    status)    status_all ;;
    logs)      show_logs "${2:-family}" "${3:-50}" ;;
    dashboard) open_dashboard ;;
    open)      (xdg-open http://localhost:5177 >/dev/null 2>&1 &) || true ;;
    help)      help_text ;;
    *)         help_text ;;
esac
