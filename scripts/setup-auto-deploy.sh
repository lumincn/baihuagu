#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
GIT_DIR="$PROJECT_ROOT/.git"
HOOK_FILE="$GIT_DIR/hooks/post-commit"

echo "========================================"
echo "  百花 - 自动构建部署环境初始化"
echo "========================================"

# 1. 确保脚本可执行
chmod +x "$SCRIPT_DIR/auto-deploy.sh"
chmod +x "$SCRIPT_DIR/watchdog.sh"
chmod +x "$SCRIPT_DIR/setup-auto-deploy.sh"
echo "✅ 脚本权限已设置"

# 2. 安装 Git Hooks（post-commit + post-merge）
for hook in post-commit post-merge; do
    HOOK_FILE="$GIT_DIR/hooks/$hook"
    if [ -f "$SCRIPT_DIR/hooks/$hook" ]; then
        cp "$SCRIPT_DIR/hooks/$hook" "$HOOK_FILE"
        chmod +x "$HOOK_FILE"
        echo "✅ Git $hook hook 已安装: $HOOK_FILE"
    else
        # 回退：内联生成
        cat > "$HOOK_FILE" << HOOKEOF
#!/bin/bash
SCRIPT_DIR="\$(cd "\$(dirname "\${BASH_SOURCE[0]}")/../.." && pwd)/scripts"
"\$SCRIPT_DIR/auto-deploy.sh" --force &
HOOKEOF
        chmod +x "$HOOK_FILE"
        echo "✅ Git $hook hook 已生成: $HOOK_FILE"
    fi
done

# 3. 安装 Systemd 用户服务
mkdir -p "$HOME/.config/systemd/user"

cat > "$HOME/.config/systemd/user/baihua.service" << EOF
[Unit]
Description=百花 - 文件变化监控与自动构建
After=docker.service

[Service]
Type=simple
ExecStart=$SCRIPT_DIR/watchdog.sh
Restart=always
RestartSec=5
StandardOutput=append:/tmp/baihua-watchdog.log
StandardError=append:/tmp/baihua-watchdog.log

[Install]
WantedBy=default.target
EOF

echo "✅ Systemd 服务已安装: ~/.config/systemd/user/baihua.service"

# 4. 启用并启动服务
systemctl --user daemon-reload
systemctl --user enable baihua.service

# 检查是否已经在运行
if systemctl --user is-active --quiet baihua.service; then
    echo "🔄 watchdog 已在运行，重新启动..."
    systemctl --user restart baihua.service
else
    echo "🚀 启动 watchdog..."
    systemctl --user start baihua.service
fi

echo ""
echo "========================================"
echo "  初始化完成！"
echo "========================================"
echo ""
echo "功能说明:"
echo "  1. 提交代码后自动构建部署（Git Hook）"
echo "  2. 文件变化实时监控自动构建（inotify）"
echo "  3. Systemd 守护进程，开机自启"
echo ""
echo "常用命令:"
echo "  systemctl --user status baihua   # 查看状态"
echo "  systemctl --user stop baihua     # 停止监控"
echo "  systemctl --user start baihua    # 启动监控"
echo "  journalctl --user -u baihua -f   # 查看实时日志"
echo "  cat /tmp/baihua-auto-deploy.log           # 查看构建日志"
echo "  cat /tmp/baihua-watchdog.log              # 查看监控日志"
echo ""
echo "访问地址: http://localhost:5177"
