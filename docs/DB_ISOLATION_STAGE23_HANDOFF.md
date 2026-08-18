# 一服务一数据库 · 阶段2/3 交接与部署注意事项

> 用途：阶段2（Family 删 AIDbContext）与阶段3（ai.db 使用点改道）的**代码已完成**（本机 Windows 仓库），
> 记录部署到 Linux（k8s 13 号机）时需要知道的变更点、回归清单与已知事项。
> 配套目标文档：`docs/ONE_SERVICE_ONE_DB.md`（设计）与 `docs/DB_ISOLATION_STAGE1_HANDOFF.md`（阶段1 部署）。

## 1. 一句话现状

代码已提交到本仓库 main（提交 `1c261c2`，标题含 db-isolation stage2/3），
构建通过（`services/BaiHua.slnx`，0 错误）、Family 单测 931 全绿。
**尚未构建镜像/部署**（由 Linux 侧执行）。阶段1 遗留的 bh-ai arguments 字符串修复（镜像重建）
同样未部署，需与本次改动一起重建。

## 2. 本次改动的部署影响面（按镜像）

### bh-ai（AI 服务）镜像 — 必须重建
- 新增 `ComfyArtworksController`：`/api/ai/comfy/artworks`（GET 列表 / POST 保存 / DELETE 删除）
- `AiConfigController` 新增：`GET /api/ai/config/export?password=`（备份导出，含重加密后的 key）、
  `POST /api/ai/config/import`（备份导入，体：`{ Providers, Password, ReplaceAll }`）
- `GET /api/ai/config/providers` 等响应类型由内部 ViewModel 换成 Contracts 的 `AiConfigProvider`
  （字段名/形状完全一致，WebUI 无感）
- Program.cs 新增注册：`DataEncryptionService`、`IAiConfigService`（映射到同一 `AiConfigService` 单例）
- 依赖注入变化：`AiSettingsService` 构造参数改为 `IAiConfigService`（原为 ServiceProvider 自解析）

### bh-family（Family 服务）镜像 — 必须重建
- **彻底移除 AIDbContext**：不再注册/打开 ai.db（启动不再等待 ai.db schema、不再做 key 迁移）
- AI 配置读取改经 HTTP：新增 `HttpAiConfigService`（读 `GET /api/ai/config/providers|apikeys|providers/{id}`，
  写 POST/DELETE providers）；Provider 列表在 Family 侧不再缓存（每次实时拉取）
- Comfy 历史改经 AI 服务 HTTP（`AiComfyArtworksClient`）；聊天记忆改存 family.db；测速历史改存 family.db
- 备份/恢复：AI 提供方部分改走 `GET /api/ai/config/export` + `POST /api/ai/config/import`
  （**备份需 AI 服务在线**；AI 服务不可达时全量备份会失败并提示）
- `EmbeddingService` 改经 `GET http://bh-ai:8791/api/embedding/config` 读嵌入配置（30s 缓存）；
  **注意**：k8s 里 Embedding 配置若此前存在 ai.db 的 EmbeddingConfigs 表，现在 Family/Vault 会实时
  从 AI 服务读取该表（同一数据源，无感知）；仅当 AI 服务不可达时才回退环境变量
  `TASK_RUNNER_EMBEDDING_URL/MODEL`（01-configmap.yaml 未设这两个变量，如需要可加）

### bh-vault（Vault 服务）镜像 — 建议重建（可选）
- 移除 AIDbContext 注册（此前为死注册 + EmbeddingService 依赖）；EmbeddingService 同样改走 HTTP。
  不重建也兼容（旧代码仍读 ai.db，但一服务一数据库目标要求 Vault 不碰 ai.db）

### 数据库迁移
- **family.db**：启动时自动 `Migrate()` 新增 `BenchmarkSessions` 表（EF 迁移
  `20260818163619_AddBenchmarkSessionsToFamily`）——无需人工操作
- **ai.db**：无新迁移。旧的 `BenchmarkSessions`/`ComfyArtworks` 表保留为孤儿表（不再被读写），
  后续如需清理可加 AI 迁移 DROP（**旧测速/Comfy 历史数据在切换后不再显示**，属预期一次性重置）

## 3. 回归清单（部署后逐项验证）

1. 测速（Family→shim→deepseek）：
   `curl -s -m 90 -X POST http://192.168.3.13/mg/benchmark/run -H 'Content-Type: application/json' -d '{"modelName":"deepseek-v4-flash"}'`
   → 成功后 `GET /mg/benchmark/history`（或 WebUI 排行榜）能看到记录（**存 family.db**）
2. 聊天（pool 网关非流式）：`POST http://192.168.3.13/mg/pool/v1/chat/completions`
3. 聊天记忆：WebUI 聊天页多轮对话后，重启 Family，同 sessionId 继续聊，应能回忆早期内容（三层记忆生效；
   此前因指向 AIDbContext 而静默失效，本次修复）
4. 工具调用端到端：拜师/任务/OpenClaw 走 Function Calling（shim 透传 + Family 执行工具）
5. 本地模型经 shim：OpenVINO（`qwen2-5-vl-7b-instruct-int4-ov`）聊天，AI 服务路由 `http://bh-openvino:8000/v1`
6. AI 配置页：WebUI 显示 Provider 列表/KeyMask 正常；保存 Provider（含 key）→ AI 服务 → 列表刷新
7. Comfy：生成图片/视频后历史列表出现记录；删除历史生效（存 ai.db，经 HTTP）
8. 全量备份/恢复：创建备份（含密码）→ ZIP 内 db/ai_providers.json 存在且 key 非明文；
   恢复后 AI 配置页 Provider 与 key 还原（AI 服务在线是前提）
9. 嵌入/RAG：知识库语义搜索仍生效（Embedding 配置经 AI 服务读取；本地 bge/OpenVINO 无需 key）
10. 算力池：对端注册/选用模型后，WebUI 模型列表**无需重启**即可见（Family 不再缓存 Provider 列表）
11. pod 稳定性：bh-ai / bh-family 无 CrashLoop、无 ai.db 锁等待日志

## 4. 回滚

- 单点回滚：本次改动与阶段1 共用 `AiClient__UseShim` 开关（Family 设 true）。
  出问题改回 `"false"` 即恢复 Family 直连模型，但注意：**新代码已移除 AIDbContext 注册**，
  直连模式需要 ai.db（Provider 配置/API Key）——因此回滚必须用旧镜像（git 回退到
  `77d80e4` 之前或使用上一版已部署镜像），不能只改配置开关。
- 建议：先在测试/低峰期 rollout，保留上一版镜像 tag 以便快速回退。

## 5. 关键设计决策（供评审）

- `IAiConfigService` 抽象：AI 进程 = `AiConfigService`（直读 ai.db）；Family = `HttpAiConfigService`
- 聊天记忆、测速历史 → **Family 自有库**（family.db）；Comfy 历史、备份 key 处理 → **AI 服务**（ai.db + HTTP）
- 备份导出/导入的 key 加解密只发生在 AI 服务进程（Family 全程不接触明文 key）
- 已知限制：云端**鉴权** Embedding 模型暂不支持（Family/Vault 拿不到 key；本地嵌入模型正常）。
  后续可做 AI 服务 embedding shim（`/mg/ai/v1/embeddings`）
