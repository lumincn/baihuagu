# 百花 SDK 架构设计与计划 v2

## 变更

1. **`baihuagu` → `baihua`** — 项目原名"百花谷"已改"百花"
2. **新增 `huage-sdk`** — 花阁（shzhengji.com）独立 SDK，不与 baihua-sdk 混用
3. **SRP 审计** — 每个 SDK 只含本域内容

## 最终格局

```
Kotlin:
  app ─┬─ baihua-sdk    (百花通信：HMAC/配对/同步/推送)
       ├─ huage-sdk     (花阁 shzhengji.com)
       ├─ ai-sdk        (DeepSeek/Anthropic AI，原 huage-sdk 改名)
       ├─ master-sdk    (拜师定义)
       ├─ vault-sdk     (知识库本地管理)
       └─ transfer-sdk  (BLE)

ArkTS / C#: 同格局，平台差异单独决策
```

## 执行状态

| # | 任务 | 状态 |
|---|------|------|
| 1 | Kotlin `huage-sdk` → `ai-sdk` 改名 | ✅ |
| 2 | Kotlin 全局 `baihuagu` → `baihua` | ✅ |
| 3 | Kotlin 新建 `huage-sdk` + HuageSyncGateway | ✅ |
| 4 | SRP 审计 | ✅ |
| 5 | Kotlin `baihua-sdk-v2` → 合并入 `baihua-sdk` | ✅ |
| 6 | Kotlin 删除废弃 AuthorizationWatcher/WebSocketPushService/IPushService | ✅ |
| 7 | Kotlin vault-sdk 解耦（ISyncService 注入） | ✅ |
| 8 | ArkTS `baihua_sdk` 新增 AuthService/AuthState/ServerAuthInfo | ✅ |
| 9 | ArkTS entry 迁移到新 AuthService（替换 AuthorizationWatcher） | ⏳ |
| 10 | C# 确认 | ⏳ |

### 完成：Kotlin `huage-sdk` → `ai-sdk` 改名
- 模块名、包名、目录、namespace、app 层 import

### 2. Kotlin 全局 `baihuagu` → `baihua` 改名
- 所有 SDK 包名 `com.lumin.baihuagu.sdk.*` → `com.lumin.baihua.sdk.*`
- 目录 `baihuagu/sdk/` → `baihua/sdk/`
- namespace、app 层 import

### 3. Kotlin 新建 `huage-sdk`
- 从 app 提取花阁访问逻辑（独立实现，不复用 baihua-sdk）

### 4. Kotlin 编译验证

### 5. ArkTS 对标：重命名 + 新建模块

### 6. C# 验证（命名空间已是 Baihua，确认 SRP）

## SRP 检查

| SDK | 应含 | 不含 |
|------|------|------|
| baihua-sdk | 百花通信(HMAC/配对/同步/授权/推送) | Markdown、AI、拜师 |
| huage-sdk | 花阁 shzhengji.com API | 百花本地 |
| ai-sdk | DeepSeek/Anthropic API | 百花/花阁 |
| master-sdk | Stage/Blessing 定义 | DB/Context |
| vault-sdk | 知识库本地管理 | 服务器通信(仅依赖接口) |
| transfer-sdk | BLE 传输 | — |

## 依赖关系（2026-07-31）

```
app ─┬─ baihua-sdk   (signing/sync/models/transport/AuthService/pairing)
     ├─ huage-sdk     (花阁 API，不依赖其他 SDK)
     ├─ ai-sdk        (DeepSeek，不依赖其他 SDK)
     ├─ master-sdk    (拜师，不依赖其他 SDK)
     ├─ vault-sdk     (知识库，仅依赖 baihua-sdk 的三个接口)
     │    └─→ baihua-sdk  (ISyncService + IRequestSigner + IVaultStorageAdapter)
     └─ transfer-sdk  (BLE，不依赖其他 SDK)
```

唯一跨 SDK 依赖：`vault-sdk → baihua-sdk`，且全部通过接口注入。
