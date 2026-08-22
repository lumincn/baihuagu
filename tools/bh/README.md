# bh - 百花统一 CLI

矩阵式入口：OS × 部署方式 的 cell 脚本统一分派。

```
tools/bh/
├── bh.ps1          Windows 统一入口（分派器）
├── bh.cmd          Windows cmd shim（让 cmd/PowerShell 都能直接 `bh`）
├── bh.sh           Linux 统一入口（分派器）
├── win/
│   ├── native/     Windows native（dotnet 进程管理）bh.ps1
│   └── docker/     Windows docker（compose）bh.ps1
└── linux/
    ├── k8s/        Linux k3s（containerd，nerdctl 构建）bh.sh
    └── native/     Linux native（dotnet 进程管理）bh.sh
```

## 用法

```
bh <cell> <command> [args]    路由到指定 cell（cell: native | docker | k8s）
bh <command> [args]           默认 cell（Windows→native，Linux→k8s）
bh install / uninstall        安装到 PATH / 移除
```

- Windows 默认 cell 是 native（本机 dotnet 服务）；`bh k8s ...` 自动经 WSL root 路由到 Linux k3s。
- Linux 默认 cell 是 k8s；`build`/`deploy`/`update` 需 root（containerd socket / k3s.yaml 仅 root 可读），`status`/`logs`/`dashboard` 等只读命令检测到配置不可读时自动提权，无需手动 sudo。
- 各 cell 内部命令见 `bh <cell> help`。

### k8s cell 命令速查

| 命令 | 说明 |
|------|------|
| `bh build [img...]` | 构建镜像进 k3s containerd；默认全部 5 个，可指定部分（如 `bh build family webui`） |
| `bh deploy` | `kubectl apply` k8s/ 清单 + 滚动重启应用 |
| `bh up` | 按 git 变更只构建受影响镜像 + deploy（未变更镜像跳过）；`bh up --all` 强制全量 |
| `bh update` | `git pull` + `up` |
| `bh status` | pods / svc / pvc 总览（免 sudo） |
| `bh logs <svc> [n]` | tail pod 日志，默认 50 行（免 sudo） |
| `bh prune` | 清空 buildkit 构建缓存（释放磁盘、修复 nuget 缓存损坏导致的构建失败） |
| `bh dashboard` | 打开 WebUI（cli-token 自动登录） |
| `bh openvino <on\|off\|status>` | Intel GPU 相关服务按需启停 |
| `bh destroy` | 删除 baihua 命名空间 |

## 安装

```powershell
# Windows（写入用户 PATH，新终端生效）
.\tools\bh\bh.ps1 install

# Linux / WSL
bash tools/bh/bh.sh install            # 普通用户 → ~/.local/bin/bh（~/.bashrc 已含该目录时直接可用）
sudo bash tools/bh/bh.sh install       # root → /usr/local/bin/bh（在 sudo secure_path 内，sudo bh 也可用）
```

> **目录改名/移动后不用重装。** Linux 安装的是自包含定位器（`locator.sh` 的副本，非软链）。
> 每次调用按 `$BAIHUA_HOME` → 常见路径（`~/src/mdyj/baihua`、`~/src/baihuagu` 等）→ 当前目录向上
> 的顺序自动定位仓库根，再转发到仓库内的 `bh.sh`。只要仓库还在常见位置、或在仓库目录内执行、
> 或设置了 `BAIHUA_HOME`，`bh` 都能用。想要重新定位只需 `export BAIHUA_HOME=<新路径>`。

> **sudo 下找不到 bh？** `sudo bh` 用 root 的 `secure_path`（不含 `~/.local/bin`）。
> 用 `sudo bash tools/bh/bh.sh install`（装到 `/usr/local/bin/bh`）即可，或直接
> `sudo /usr/local/bin/bh <cmd>` / `sudo bash <仓库>/tools/bh/bh.sh install`。

## 踩坑记录（实现注意）

- PowerShell 5.1/7 中 splatting `[string]` 变量会把字符串拆成字符数组（`@Arg1` = 每个字符一个参数），string 必须直接位置传参，只有数组才 splat。
- 强类型数组 `$arr[1..1]` 单元素范围索引返回裸元素而非数组，收尾用 `@($arr | Select-Object -Skip 1)` 强制数组。
- PowerShell 脚本（含中文）必须存 UTF-8 with BOM，否则 5.1 按 GBK 误读破坏结构；`.cmd` 文件必须纯 ASCII（cmd 按 ANSI 读）。
- `wsl.exe` 传参剥离反斜杠，Windows→WSL 路径先 `-replace '\\','/'` 再 `wsl wslpath -u`。
- `wsl -e sudo` 会卡密码提示（WSL 默认 sudo 要密码），用 `wsl -u root` 免密。
