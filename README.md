# 百花

家庭版后端服务 + bh CLI 工具，面向本地/局域网使用。

## 项目结构

```
services/
  Baihua.Family/    # 家庭版主服务（亲子功能、任务、设备管理、家庭病历本）
  Baihua.AI/        # AI 模型、聊天、配置管理
  Baihua.Vault/     # 知识库、同步、搜索、索引
  Baihua.Web/       # 家庭版 Web 界面（Blazor Server）
  Baihua.Contracts/ # 共享 DTO 与接口契约
  Baihua.Core/      # 共享服务层（安全、设备、索引等）
  Baihua.Data/      # 共享 EF Core 数据层
libs/
  BaihuaSdk/        # 跨平台移动端 SDK（net9.0;net10.0，零 MAUI 依赖）
  MobileContract/   # 移动端契约（DTO、接口定义）
clients/
  Huapu/            # 花圃（BaiHua.Nursery）— MAUI 技术验证客户端
scripts/            # 开发、发布、部署脚本
docs/               # 文档
docker/             # Docker 配置
tools/bh/           # 极简 CLI 工具（Windows/Linux，native/docker/k8s）
tests/
  Baihua.Family.Tests/  # 后端服务测试
  Baihua.Sdk.Tests/     # SDK 单元测试与集成测试
  Huapu.Tests/          # MAUI DI 回归测试
  e2e/                  # Playwright E2E 测试
```

## 快速启动

```bash
# 一键打开管理面板（自动启动服务）
./bh dashboard

# 手动启动
cd services/Baihua.Family && dotnet run
cd services/Baihua.Web && dotnet run
```

## DSH 插件生态（DeepSeek Harness × 百花）

> **定位：百花 = 能力提供方**（算力池 / 本机模型 / 知识库 / 家庭数据），**DSH（DeepSeek Harness）= 编排与交互面**。
> 插件仓库位于 `~/src/`（org `luminsw`，均已公开）；部署与配置总文档见 `docs/DSH_INTEGRATION.md`。

| 插件 | 方向 | 作用 | 安装位置 |
|---|---|---|---|
| [baihua-dsh-plugin](https://github.com/luminsw/baihua-dsh-plugin) | 百花 Web → DSH | 桥接：agent 会话驱动（HTTP+WS `/dsh-bridge/*`）、`bh_*` 运维工具、`baihua_draw*` 绘图、DSH 设置页「百花服务状态」卡片 | DSH web profile（127.0.0.1:3080），`lanListen 0.0.0.0:3081` 局域网桥 |
| [baihua-local-ai-dsh-plugin](https://github.com/luminsw/baihua-local-ai-dsh-plugin) | DSH → 百花本地 AI | 探测 OVMS/shim/算力池，注册 `baihua-local` LLM provider + `local_ai_small_task` 小任务工具（省线上 token） | DSH web profile |
| [baihua-mcp-server](https://github.com/luminsw/baihua-mcp-server) | 百花 → 任意 MCP 客户端 | 标准 MCP（stdio）：知识库 / 家庭只读能力 | 任意 MCP 客户端 |
| [hysteria-dsh-plugin](https://github.com/luminsw/hysteria-dsh-plugin) | 本机代理 | DSH 内管理 Hysteria 2 代理（启停/状态/连通性 + `proxy_retry` 失败兜底） | DSH web profile |

百花 Web 的 `/dsh` 页已改为 iframe 内嵌 DSH 官方 Web UI（AI 消费型交互统一交给 DSH 智能体），右上角「🧰 运维」保留百花服务运维面板。

> 百花**只读数据**（知识库/记账/任务）统一经 [`baihua-mcp-server`](https://github.com/luminsw/baihua-mcp-server)
> 以标准 MCP 暴露，DSH 内工具名为 `mcp__baihua__*`；`baihua-dsh-plugin` 只负责桥接/运维/绘图，不再注册数据工具。

## 访问授权

- **WebUI（5177）**：CLI Token Cookie 登录，`./bh dashboard` 一键授权（Token 5 分钟有效）。
- **管理 API（8788/8791/8790）**：默认仅允许本机（loopback）访问；局域网设备只能访问移动端公开端点（`/mg/*`、配对/同步等），并需 HMAC 签名设备鉴权。
- **容器/反向代理部署**：非 loopback 来源（如 WebUI 容器）访问管理 API，需用环境变量显式放行：
  - `BAIHUA_ADMIN_ALLOWED_NETS`：允许访问管理 API 的网段（逗号分隔 CIDR，如 `172.16.0.0/12`）
  - `BAIHUA_TRUSTED_PROXY_NETS`：受信任反向代理网段（其 `X-Forwarded-For` 头才会被采信）

```bash
./bh dashboard   # 本机一键访问
```

## 端口

| 服务 | 端口 | 说明 |
|------|------|------|
| Baihua.Family | 8788 | 家庭/亲子功能（任务、成就、OpenClaw、设备配对、家庭病历本/AI 诊断） |
| Baihua.AI | 8791 | AI 模型、聊天、配置管理 |
| Baihua.Vault | 8790 | 知识库、同步、搜索、索引 |
| Baihua.Web | 5177 | Blazor Server 管理后台 |
| OpenVINO Model Server | 8000 | 本地 OpenVINO 推理（OVMS，OpenAI 兼容 /v3：对话/视觉/嵌入） |

## 本地 OpenVINO 推理（OVMS）

百花本地 OpenVINO 推理由 **Intel OVMS（OpenVINO Model Server）** 统一承载（OpenAI 兼容 `/v3/chat/completions`、`/v3/embeddings`，模型 id：`qwen2.5` / `qwen2.5-vl-3b` / `qwen2.5-vl-7b` / `bge-small-zh`），不再运行自研 Python 服务。

- **Windows**：管理员运行 `scripts/install-openvino-ovms-service.ps1` 一键安装为系统服务（`ovms`，:8000），模型目录 `%USERPROFILE%/.baihua/models/`
- **Linux k8s**：`bh-openvino` Deployment（官方 `openvino/model_server` 镜像），详见 `k8s/README.md`
- 模型目录需包含 4 个 OpenVINO IR 模型目录（缺失的模型 OVMS 不会加载）


## Windows (PowerShell) 运行

### 推荐使用 PowerShell 7 (pwsh)

强烈建议安装 **PowerShell 7**，它默认使用 UTF-8 编码，中文显示正常，不会出现乱码问题。

| 版本 | 命令 | 默认编码 | 中文支持 |
|------|------|----------|----------|
| PowerShell 7 | `pwsh` | UTF-8 | ✅ 正常 |
| PowerShell 5 | `powershell` | GBK | ⚠️ 需要特殊处理 |

**安装 PowerShell 7：**

```powershell
# 使用 winget 安装（推荐）
winget install --id Microsoft.PowerShell --source winget

# 或下载安装包：https://github.com/PowerShell/PowerShell/releases
```

**验证安装：**

```powershell
pwsh --version
# PowerShell 7.6.4
```

### 使用方法

示例（在仓库根目录执行）：

```powershell
# 使用 PowerShell 7（推荐）
pwsh -ExecutionPolicy Bypass -File .\bh.ps1 dashboard

# 或直接运行（PowerShell 7 已添加到 PATH）
.\bh.ps1 dashboard

# 启动所有服务（后台）
.\bh.ps1 start

# 停止所有服务
.\bh.ps1 stop

# 查看运行状态
.\bh.ps1 status

# 打开管理面板（浏览器）
.\bh.ps1 dashboard

# 查看实时日志（例如 family）
.\bh.ps1 logs family

# 开发模式（监听文件变动自动重编译）
.\bh.ps1 dev

# 启动全部服务（含 OpenObserve 监控）
.\bh.ps1 all
```

### 注意事项

- 该脚本使用 `dotnet run` 启动服务，需在 PATH 中有 .NET SDK。
- 日志与 PID 文件位于系统临时目录（%TEMP%），文件名格式为 `bh-<service>.log` / `bh-<service>.pid`。
- 如果受限执行策略阻止运行，请使用：
  ```powershell
  pwsh -ExecutionPolicy Bypass -File .\bh.ps1 start
  ```
- 如果出现中文乱码，请确保使用 PowerShell 7 (`pwsh`)，脚本已内置 `chcp 65001` 自动处理编码。
