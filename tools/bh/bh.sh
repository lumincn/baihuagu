#!/bin/bash
# bh - baihua 统一 CLI 入口（Linux）
# 路由到 tools/bh/linux/<deployment>/ 下的 cell 脚本。
#
# Cells:
#   k8s      Linux k3s（containerd，nerdctl 构建）  linux/k8s/bh.sh
#   native   Linux native（dotnet 进程）            linux/native/bh.sh
#
# 用法:
#   bh <cell> <command> [args]   路由到指定 cell
#   bh <command> [args]          使用默认 cell（k8s）
#   bh install                   软链到 ~/.local/bin/bh（须在 PATH）
#   bh uninstall                 移除软链
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

case "${1:-}" in
    k8s|native)
        cell="$1"; shift
        exec "$ROOT/linux/$cell/bh.sh" "$@"
        ;;
    install)
        bin="${HOME}/.local/bin"
        if ! mkdir -p "$bin" 2>/dev/null; then
            echo "[install] 无法创建 $bin（权限不足）"
            echo "         请用 root 执行: sudo bash $ROOT/bh.sh install  （或 WSL: wsl -u root）"
            exit 1
        fi
        ln -sf "$ROOT/bh.sh" "$bin/bh" && echo "[install] 已软链: $bin/bh -> $ROOT/bh.sh"
        case ":$PATH:" in
            *":$bin:"*) echo "[install] ~/.local/bin 已在 PATH，直接可用: bh <command>" ;;
            *)
                if grep -q 'local/bin' "${HOME}/.bashrc" 2>/dev/null; then
                    echo "[install] ~/.local/bin 已配置在 ~/.bashrc（重新登录或 source ~/.bashrc 后生效）"
                else
                    echo "export PATH=\"$bin:\$PATH\"" >> "${HOME}/.bashrc" && \
                        echo "[install] 已把 ~/.local/bin 追加到 ~/.bashrc（重新登录或 source ~/.bashrc 后生效）"
                fi
                ;;
        esac
        ;;
    uninstall)
        rm -f "${HOME}/.local/bin/bh" 2>/dev/null || echo "[uninstall] 无权限删除 ${HOME}/.local/bin/bh（请用 root）"
        echo "[uninstall] 已移除 ${HOME}/.local/bin/bh"
        ;;
    help|-h|--help|"")
        echo "bh - baihua 统一 CLI（Linux）"
        echo ""
        echo "用法:"
        echo "  bh <cell> <command> [args]    路由到指定 cell（k8s | native）"
        echo "  bh <command> [args]           默认 cell（k8s）"
        echo "  bh install / uninstall        安装到 ~/.local/bin / 移除"
        echo ""
        echo "提示: k8s cell 需要 root（k3s 配置 /etc/rancher/k3s/k3s.yaml 仅 root 可读），WSL 下用 wsl -u root"
        echo "cell 内可用命令: bh <cell> help"
        ;;
    *)
        exec "$ROOT/linux/k8s/bh.sh" "$@"
        ;;
esac
