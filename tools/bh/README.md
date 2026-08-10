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
- Linux 默认 cell 是 k8s；k8s cell 需要 root（k3s 配置仅 root 可读）。
- 各 cell 内部命令见 `bh <cell> help`。

## 安装

```powershell
# Windows（写入用户 PATH，新终端生效）
.\tools\bh\bh.ps1 install

# Linux / WSL（软链到 ~/.local/bin/bh，需 root 时用 sudo 或 wsl -u root）
bash tools/bh/bh.sh install
```

## 踩坑记录（实现注意）

- PowerShell 5.1/7 中 splatting `[string]` 变量会把字符串拆成字符数组（`@Arg1` = 每个字符一个参数），string 必须直接位置传参，只有数组才 splat。
- 强类型数组 `$arr[1..1]` 单元素范围索引返回裸元素而非数组，收尾用 `@($arr | Select-Object -Skip 1)` 强制数组。
- PowerShell 脚本（含中文）必须存 UTF-8 with BOM，否则 5.1 按 GBK 误读破坏结构；`.cmd` 文件必须纯 ASCII（cmd 按 ANSI 读）。
- `wsl.exe` 传参剥离反斜杠，Windows→WSL 路径先 `-replace '\\','/'` 再 `wsl wslpath -u`。
- `wsl -e sudo` 会卡密码提示（WSL 默认 sudo 要密码），用 `wsl -u root` 免密。
