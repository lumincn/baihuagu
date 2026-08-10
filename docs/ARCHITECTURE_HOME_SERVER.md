# Home Server 架构方案

> 版本：v1.0  
> 日期：2026-05-01  
> 状态：草案，待评审  

## 1. 背景与问题

当前代码库采用 **Core + Entry** 模式：

```
services/
├── Baihua.Core/      ← 共享库（服务逻辑）
├── WebUI.Core/           ← 共享库（Razor 组件 + 页面）
├── TaskRunner.Cloud/     ← 官网版入口
├── Baihua.Family/    ← 家庭版入口
├── WebUI.Cloud/          ← 官网版入口
└── WebUI.Family/         ← 家庭版入口
```

**问题：**
- Core 与 Entry 深度耦合，任何改动需同时考虑两边兼容性
- `WebUI.Core` 必须为 `OutputType=Exe` 才能生成 Blazor 静态资源，导致设计矛盾
- 开发模式（`dotnet run`）与发布模式（`dotnet publish`）行为不一致，调试困难
- 两个版本的页面、路由、业务逻辑大量共享，但用户场景完全不同（家庭 vs 官网）
- 违背迪米特法则：Family 版被迫了解 Cloud 版的实现细节

## 2. 设计原则

1. **零共享代码**：Cloud 与 Family 之间不共享任何项目（除 `Baihua.Contracts` 接口契约）
2. **独立部署**：每个服务可独立构建、独立运行、独立升级
3. **显式接口**：服务间通信仅通过 HTTP API + 共享 DTO 契约
4. **简化单体**：家庭版不拆微服务，保持单体应用以降低运维复杂度
5. **配置即代码**：环境差异（端口、路径、功能开关）全部通过配置文件注入

## 3. 目标架构

### 3.1 项目结构

```
services/
├── Baihua.Contracts/     # 仅含 DTO 与接口定义（零实现）
│   ├── DTOs/
│   └── Interfaces/
│
├── TaskRunner.Cloud/         # 官网版后台（完整独立）
│   ├── Program.cs
│   ├── appsettings.json
│   └── ... 全部业务代码
│
├── WebUI.Cloud/              # 官网版前台（完整独立）
│   ├── Program.cs
│   ├── Components/
│   ├── Pages/
│   └── ... 全部 Razor 代码
│
├── Baihua.Family/        # 家庭版后台（完整独立）
│   ├── Program.cs
│   ├── appsettings.json
│   └── ... 全部业务代码
│
└── WebUI.Family/             # 家庭版前台（完整独立）
    ├── Program.cs
    ├── Components/
    ├── Pages/
    └── ... 全部 Razor 代码
```

**删除：** `Baihua.Core`、`WebUI.Core`

### 3.2 依赖关系

```
┌─────────────────┐      HTTP API      ┌─────────────────┐
│  WebUI.Cloud    │ ◄────────────────► │ TaskRunner.Cloud│
└─────────────────┘                    └─────────────────┘
         │                                      │
         │         引用（仅 DTO）                │
         └──────────────┬───────────────────────┘
                        │
              ┌─────────▼─────────┐
              │ Baihua.Contracts│
              └─────────┬─────────┘
                        │
         ┌──────────────┴───────────────────────┐
         │         引用（仅 DTO）                │
┌─────────────────┐                    ┌─────────────────┐
│  WebUI.Family   │ ◄────────────────► │ Baihua.Family│
└─────────────────┘      HTTP API      └─────────────────┘
```

**规则：**
- `Baihua.Contracts` 仅包含 `record` DTO 和 `interface` 定义，**无任何实现**
- WebUI 项目不直接引用 TaskRunner 项目，仅引用 Contracts 获取 DTO 类型
- 运行时通信通过配置的 `FamilyApi:BaseUrl` 进行 HTTP 调用

### 3.3 家庭版部署拓扑（Home Server）

```
┌──────────────────────────────────────────────────────────────────┐
│                         Home Server                               │
│  (Raspberry Pi / NUC / 旧笔记本 / WSL)                             │
│                                                                   │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐           │
│  │ WebUI.Family │  │Baihua.AI │  │Baihua.Vault           │
│  │    :5177     │  │    :8791     │  │    :8790     │           │
│  │ (Blazor SSR) │  │(ASP.NET Core)│  │(ASP.NET Core)│           │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘           │
│         │                 │                  │                   │
│         │    ┌────────────┴──────────────────┘                   │
│         │    │                                                    │
│         │    ▼                                                    │
│         │  ┌─────────────────┐                                    │
│         └──┤ Baihua.Family│                                   │
│            │     :8788        │                                   │
│            │  (ASP.NET Core)  │                                   │
│            └────────┬─────────┘                                   │
│                     │                                             │
│            ┌────────▼─────────┐                                   │
│            │  SQLite (共享)   │                                   │
│            │  taskrunner.db   │                                   │
│            │  ai.db           │                                   │
│            └──────────────────┘                                   │
│                                                                   │
│  ┌─────────────────────────────────────────────────────────┐     │
│  │  Nginx (可选)                                            │     │
│  │  - 端口 80 → 反向代理到 WebUI.Family:5177                │     │
│  │  - /api/search/ → Baihua.Vault:8790                  │     │
│  │  - /vault/ → Baihua.Vault:8790                       │     │
│  └─────────────────────────────────────────────────────────┘     │
└──────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                    ┌─────────────────┐
                    │   局域网设备      │
                    │  (手机/平板/PC)  │
                    └─────────────────┘
```

**说明：**
- 家庭版为 **3 后端 + 1 前端** 架构：Baihua.Family (8788) + Baihua.AI (8791) + Baihua.Vault (8790) + WebUI.Family (5177)
- 数据库使用 SQLite（零配置、单文件、备份简单），Family 与 Vault 共享 `taskrunner.db`，AI 使用独立的 `ai.db`
- Nginx 可选，用于提供统一入口和静态缓存
- 移动端通过局域网 IP 连接 `:8788`，Family 自动将 Vault 域 API 转发到 `:8790`
- 移动端代码无需任何改动

## 4. 技术选型

| 层级 | 技术 | 选型理由 |
|------|------|----------|
| 运行时 | .NET 10 | 已有技术栈，长期支持 |
| Web 框架 | Blazor Server (SSR) | 减少 JS 依赖，C# 全栈 |
| 数据库 | SQLite | 零配置、单文件、适合家庭场景 |
| 缓存 | 内存缓存 (`IMemoryCache`) | 无需外部依赖 |
| 日志 | Serilog + 文件 | 简单、可轮转 |
| 进程守护 | systemd / tmux | Linux 标准方案 |
| 反向代理 | Nginx | 静态缓存、统一入口 |
| 发现服务 | mDNS (avahi-daemon) | 局域网自动发现 |

## 5. 数据流

### 5.1 移动端同步（Family 版）

```
移动端 App ──mDNS──► 发现 Home Server IP
       │
       └──HTTP──► Baihua.Family:8788
              │
              ├──► 接收增量变更 (JSON)
              ├──► 返回知识库数据
              └──► 写入 SQLite
```

### 5.2 Web 管理（Family 版）

```
浏览器 ──► WebUI.Family:5177
    │
    ├──► Blazor SSR 渲染页面
    ├──► JS Interop 调用本地 API
    ├──► WebUI ──HTTP──► Baihua.Family:8788  (任务、成就、设备)
    ├──► WebUI ──HTTP──► Baihua.AI:8791     (AI 配置、对话)
    └──► WebUI ──HTTP──► Baihua.Vault:8790  (知识库、搜索、同步)
```

## 6. 配置策略

### 6.1 Family 版 appsettings.json

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5177" }
    }
  },
  "FamilyApi": {
    "BaseUrl": "http://127.0.0.1:8788/"
  },
  "AiApi": {
    "BaseUrl": "http://127.0.0.1:8791/"
  },
  "VaultApi": {
    "BaseUrl": "http://127.0.0.1:8790/"
  },
  "AllowedHosts": "*"
}
```

### 6.2 Cloud 版 appsettings.json

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": { "Url": "http://0.0.0.0:5177" }
    }
  },
  "FamilyApi": {
    "BaseUrl": "http://127.0.0.1:8788/"
  },
  "AiApi": {
    "BaseUrl": "http://127.0.0.1:8791/"
  },
  "VaultApi": {
    "BaseUrl": "http://127.0.0.1:8790/"
  },
  "BasePath": "/admin/",
  "AllowedHosts": "*"
}
```

## 7. 迁移计划

### 阶段一：代码复制 ✅ 已完成
1. ~~将 `Baihua.Core` 的全部代码复制到 `TaskRunner.Cloud` 和 `Baihua.Family`~~
2. ~~将 `WebUI.Core` 的全部代码复制到 `WebUI.Cloud` 和 `WebUI.Family`~~
3. ~~删除 `Baihua.Core` 和 `WebUI.Core` 项目~~
4. ~~调整命名空间，确保编译通过~~

**验证结果**：`TaskRunner.Cloud`、`Baihua.Family`、`WebUI.Cloud`、`WebUI.Family` 四项目 `dotnet build` 和 `dotnet publish` 均通过。

### 阶段二：去耦合 ✅ 已完成（核心部分）
1. ~~策略分离~~ ✅：Program.cs 中 Cloud 固定注册 Cloud 策略，Family 固定注册 Family 策略；删除对方策略文件
2. ~~局域网服务分离~~ ✅：Cloud 中 OneHop/OneHopManager/mDNS 替换为空实现（`CloudOneHopStub`），不启动局域网发现后台服务
3. ~~部署模式固化~~ ✅：Cloud 和 Family 拆分为独立仓库后，部署模式在编译期即已确定，`IDeploymentContext` 及 `DeploymentMode` 环境变量已移除
4. ~~WebUI 页面/导航简化~~ ✅：Home.razor 和 NavMenu.razor 去掉条件分支，直接渲染对应组件；删除对方模式的页面和导航组件
5. 待后续演进：进一步删除各项目中不需要的 Controller/Service/Entity（需配合 DbContext 和 Migration 调整，建议在独立演进阶段逐步进行）
6. 待后续演进：将公共 DTO 提取到 `Baihua.Contracts`（目前 Contracts 已包含 DTO，两边直接引用即可）

### 阶段三：独立演进（持续）
1. Cloud 版可按需引入 PostgreSQL、Redis、Kubernetes
2. Family 版保持精简，专注稳定性和易用性
3. 两边独立版本号、独立发布节奏

## 8. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| 代码重复导致两边 bug 不同步 | 中 | 建立自动化 E2E 测试覆盖两边核心流程 |
| 迁移期间编译失败 | 低 | 在 feature 分支进行，主分支保持稳定 |
| 家庭版数据库迁移 | 低 | SQLite 单文件，备份后原地升级 |
| Cloud 版与 Family 版 Contracts 不兼容 | 中 | Contracts 版本化，遵循 SemVer |

## 9. 决策记录

### ADR-001：为什么不用微服务拆分家庭版？

**决定：** 家庭版保持单体双进程（WebUI + TaskRunner），不拆分为更细粒度服务。

**理由：**
- 家庭用户运维能力有限，进程越少越稳定
- 资源有限（树莓派/旧笔记本），减少上下文切换开销
- 当前问题主要是"共享代码耦合"而非"单体过大"
- 未来如需拆分，独立代码库后更容易进行

### ADR-002：为什么保留 Blazor Server 而非改用 API + Vue？

**决定：** 继续使用 Blazor Server SSR。

**理由：**
- 现有技术栈，迁移成本最低
- 家庭版无需 SEO，SSR 足够
- C# 全栈减少技术栈复杂度
- 未来可考虑 Blazor WASM 离线模式

## 10. 附录

### A. 目录对比

| 当前 | 目标 |
|------|------|
| `services/Baihua.Core/` | **删除** |
| `services/WebUI.Core/` | **删除** |
| `services/Baihua.Cloud/` | 保留，包含完整代码 |
| `services/Baihua.Family/` | 保留，包含完整代码 |
| `services/WebUI.Cloud/` | 保留，包含完整代码 |
| `services/WebUI.Family/` | 保留，包含完整代码 |
| `services/Baihua.Contracts/` | 保留，精简为仅 DTO |
