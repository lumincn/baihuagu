# 一服务一数据库改造（One Service One DB）

> 目标：彻底消除 Family(8788) 与 AI(8791) 双进程共享同一个 SQLite 库（ai.db）的架构风险，
> 严格实现"一个服务一个数据库"——ai.db 完全归 AI 服务独占，Family 不再引用 `AIDbContext`、
> 不再直连云端/本地模型，全部模型推理经 AI 服务的 OpenAI 兼容 shim（`/mg/ai/v1`）转发，
> API Key 与模型路由只存在于 AI 服务进程内。

## 背景与动机

### 暴露过的问题链（API Key 401 事件）

1. **密钥漂移**：API Key 用 AES-256-GCM 加密，密钥派生曾依赖"pod 名|CPU 数"机器指纹，
   Family/AI 两个 pod 指纹不同 → AI 服务保存的 key，Family 解不开 → 测速以占位符
   `placeholder`（尾4位 "lder"）发出 → deepseek 报 `Your api key: ****lder is invalid`。
   → 已修复：`.baihua-key` 固定密钥文件（共享卷）+ `BAIHUA_ENCRYPTION_KEY` Secret 兜底。
2. **双进程并发迁移**：Family 与 AI 启动时都对 ai.db 执行 `Database.Migrate()`，
   `BEGIN EXCLUSIVE` 自锁曾卡死进程。
   → 已修复：迁移收口到 AI 服务独占，Family 只读轮询等待 schema 就绪。
3. **双进程并发写**：算力池自动注册、选用模型、本地模型配置等 Family 直写 ai.db，
   与 AI 服务保存配置抢写锁。
   → 已修复：写收口（`AiProviderRegistryClient` 经 AI 服务 HTTP API 保存）。

以上修的是"症状"；**一服务一数据库是从根上消除共享**：ai.db 只有一个进程访问。

## 当前架构（改造前）

```
Family(8788) ──直读 AIDbContext──► ai.db ◄──直读/写── AI(8791)
   │                                      │
   └──直连云端(deepseek等)/本地(Ollama/OpenVINO)──┘
        （持有 API Key，26 个推理调用文件）
```

- Family 直读 ai.db 的文件（7 个）：Program.cs（注册）、StartupOrchestratorHostedService、
  ComfyController、BackupService、RestoreService、ChatMemoryService、BenchmarkRepository
- Family 推理调用链文件：26 个（聊天/拜师/任务/OpenClaw，经 `AiClientService.CreateChatClient` 直连）

## 目标架构（改造后）

```
Family(8788) ──HTTP /mg/ai/v1──► AI(8791) ──直连──► 云端/本地模型
   （无 key、无 ai.db）              │
                                     ▼
                                  ai.db（独占）
```

- Family 推理统一经 shim 按**模型名**路由（AI 服务读自己的 ai.db 拿配置+key）
- Family 的 ai.db 使用点全部改走 AI 服务 HTTP API 或迁到 Family 自有库
- AI 服务作为"模型/配置"单一来源（openai 兼容 shim 已有 `/mg/ai/v1/chat/completions`）

## 阶段划分与进度

### 阶段 0：前置修复（✅ 已完成）
- `.baihua-key` 固定密钥 + Secret 兜底（密钥不再漂移）
- ai.db schema 迁移收口 AI 服务独占（Family 只读等待）
- ai.db 写收口（`AiProviderRegistryClient`）

### 阶段 1：推理出口切 shim + shim 补 Function Calling（🔄 进行中）
| 项 | 状态 | 说明 |
|----|------|------|
| shim 扩展 Function Calling | ✅ 代码完成 | `ParseTools` 解析 tools 数组；`ParseMessages` 支持 tool 角色与 assistant tool_calls；非流式返回 `tool_calls`（arguments 为 JSON 字符串）；`finish_reason=tool_calls` |
| Family 推理切 shim | ✅ 代码完成 | `AiClientService.CreateChatClient` 按 `AiClient__UseShim=true`（仅 Family 设）走 shim；AI 服务内部保持直连（避免自指转发） |
| AI 服务 OOM 修复 | ✅ 代码完成 | 转发代理不缓存响应：`NoOpDistributedCache` 替代无限内存缓存；shim 调用 `GetChatResponseWithAutoStartAsync(..., useCache:false)` |
| k8s AI 内存限制 | ✅ 已改 | 21-ai.yaml：limits 1Gi→2Gi（转发链路内存峰值） |
| 部署验证 | ⏳ 待完成 | 由 Linux 侧执行：bh-ai 新镜像（arguments 字符串修复）构建/rollout；测速/聊天/tools 端到端回归 |
| 全链路回归 | ⏳ 待做 | 流式、工具调用（拜师/任务）、本地模型（OpenVINO shim 转发） |

已验证（阶段1 部分）：测速 `Family→shim→deepseek` = 22.9 tok/s ✓；pool 网关聊天 ✓；
shim 直接带 tools 请求返回 `tool_calls` ✓（arguments 曾为对象格式，已修为 JSON 字符串，待部署验证）。

### 阶段 2：Family 删 AIDbContext（✅ 代码完成，待部署验证）
- Program.cs 移除 `AddDbContext<AIDbContext>` / `AddDbContextFactory<AIDbContext>`（ai.db 不再注册）
- StartupOrchestratorHostedService：移除 ai.db schema 等待与 key 迁移（完全归 AI 服务）
- 新增 `IAiConfigService` 抽象（Core）：AI 服务用 `AiConfigService`（直读 ai.db），
  Family 用 `HttpAiConfigService`（经 `GET /api/ai/config/providers|apikeys|providers/{id}` + POST/DELETE 写入）
- `AiSettingsService` 改依赖接口；Family（shim 模式）不缓存 Provider 列表（对端注册后立即可见）
- Family 侧 `GetApiKey` 不再可用（HTTP 实现返回空串）；健康检查改依据 KeyMask/HasApiKey 摘要
- Vault 同步移除 AIDbContext 注册（Vault 也共享过 ai.db）；`EmbeddingService` 改经
  `GET /api/embedding/config` HTTP 读取嵌入配置（30s 缓存），不再直读 ai.db

### 阶段 3：ai.db 使用点改道（✅ 代码完成，待部署验证）
- `ChatMemoryService`：聊天记忆改存 Family 自有库（family.db 的 ChatMemoryEntries 表）——
  顺带修复了 2026-07-19 迁移后一直静默失效的记忆存取（原代码仍指向 AIDbContext，实体已不在 ai.db 模型）
- `ComfyController`：ComfyArtworks 归 AI 服务——新增 `ComfyArtworksController`
  （`/api/ai/comfy/artworks` GET/POST/DELETE），Family 经 `AiComfyArtworksClient` HTTP 读写，生成/取文件仍在 Family
- `BenchmarkRepository`：测速结果迁至 Family 自有库（family.db 新增 BenchmarkSessions 表 + EF 迁移）
  （旧数据仍留在 ai.db 的孤儿表，不影响；如需清理可后续 AI 迁移 DROP）
- `BackupService/RestoreService`：AI 提供方备份/恢复由 AI 服务负责——新增
  `GET /api/ai/config/export`（解密后用备份密码/机器密钥重加密）与 `POST /api/ai/config/import`
  （含 ReplaceAll=overwrite 语义）；Family 只搬运 JSON，不接触明文 key

### 收尾（⏳ 待做：部署在 Linux 侧执行）
- 三镜像重建 + rollout + 全链路回归（聊天/拜师/任务/算力池/本地模型）
- 部署注意事项：family.db 自动迁移新增 BenchmarkSessions 表；Embedding 配置经 AI 服务
  HTTP 读取（AI 服务需在线，否则回退环境变量 TASK_RUNNER_EMBEDDING_URL/MODEL）；
  ai.db 中 BenchmarkSessions/ComfyArtworks 旧表成为孤儿表（不影响运行）
- 后续可选项：AI 服务 embedding shim（云端鉴权嵌入模型支持）、ai.db 孤儿表清理迁移
- docs 更新（本文件 + LAN_COMPUTE_POOL.md 的 Provider 分层章节）
- 提交推送

## 关键实现点

- **shim 工具透传**：`OpenAiCompatController` 的 `ParseTools` 用
  `AIFunctionFactory.CreateDeclaration`（无实现体的工具声明）；模型返回 `tool_calls` 原样透传，
  Family 侧 OpenAI SDK 解析后**由 Family 执行工具**，二次调用（带 tool 角色消息）shim 已支持解析。
- **转发开关**：`AiSettingsService.RouteInferenceViaShim`（读 `AiClient__UseShim`）；
  Family Program.cs 显式设为 true；AI 服务默认 false（shim 内部直连真实 provider，
  避免 AI 服务自指转发——已踩坑：`Cannot assign requested address (bh-ai:8791)`）。
- **转发不缓存**：AI 服务是转发代理，缓存由调用方 Family 承担；
  `AddDistributedMemoryCache`（无限内存）→ `NoOpDistributedCache`，并给
  `GetChatResponseWithAutoStartAsync` 加 `useCache` 参数（shim 传 false）。

## 风险与注意事项

- **AI 服务成单点**：Family 推理全依赖 AI 服务；AI 挂 → 聊天/拜师/任务全不可用。
  可接受（同一 k8s 集群，重启自愈），但需保证 AI 服务资源充足（内存 2Gi）与健康探针有效。
- **本地模型地址**：AI 服务进程内直连本地模型须用集群可达地址（如 `http://bh-openvino:8000/v1`），
  不能是 `localhost`（AI pod 内 localhost 是 AI 容器自己）。
- **Traefik /mg/ai/**：对端走 `/mg/pool/v1`（Family 网关）即可，/mg/ai/ 无需暴露到 Traefik；
  Family→AI 用 service DNS（`BAIHUA_AI_URL=http://bh-ai:8791`）。
- **回滚**：阶段1 的转发由 `AiClient__UseShim` 开关控制，出问题改回 false 即恢复直连。
