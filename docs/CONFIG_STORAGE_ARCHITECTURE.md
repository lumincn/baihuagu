# 配置与存储架构

本文档汇总系统所有配置项的存储位置、读写方及设计依据。

## 存储机制一览

| 存储位置 | 数据内容 | 读写方 | 设计依据 |
|----------|----------|--------|----------|
| **SQLite** `Vaults` | 知识库（名称、路径、活跃状态） | TaskRunner.Vault | 多条记录，需查询关联 |
| **SQLite** `AiProviderSettings` | AI 提供商配置（加密 API Key、模型列表） | TaskRunner.AI | 多条记录，需排序/筛选 |
| **SQLite** `Tasks` | 任务历史（类型、状态、进度、输入输出） | TaskRunner.Family | 持续增长的结构化数据 |
| **SQLite** `AuthorizedDevices` | 授权设备（令牌、状态、IP） | TaskRunner.Family | 多条记录，需按 DeviceId 查询 |
| **SQLite** `ServerAddressSettings` | 服务器地址（移动端连接用） | TaskRunner.Family | 结构化实体，需持久化 |
| **JSON** `vault.root.path.json` | 知识库根路径偏好 | TaskRunner.Vault | 单值系统级设置，无需查询 |
| **JSON** `webui.settings.json` | WebUI 设置（BackendUrl、AI Key/Url/Model） | WebUI | 前端本地配置，不依赖后端启动 |
| **JSON** `user_preferences.json` | 用户偏好（字体大小、主题、自动保存） | WebUI | 纯 UI 偏好，后端无需感知 |
| **环境变量** | 数据目录、AI 超时/重试、嵌入服务 URL 等 | 全部服务 | 部署级配置，优先级最高 |
| **appsettings.json** | AI 默认 URL/Model、VaultPath 等默认值 | 各服务自身 | 开发/部署初始值，被环境变量覆盖 |

## 存储位置判断原则

| 适合存数据库 | 适合存 JSON 文件 |
|-------------|-----------------|
| 多条记录，需要 CRUD 操作 | 单值全局设置，只有 get/set |
| 需要查询/关联（如按 VaultId 查任务） | 不需要查询，启动时读一次即可 |
| 结构化实体，有唯一键、时间戳 | 简单键值，无复杂结构 |
| 可能增长（如任务历史、设备列表） | 固定不变（如密码哈希、根路径） |
| 需要事务（如删除 Vault 同时清理关联） | 无事务需求 |

## 优先级规则

```
环境变量 > 数据库/JSON 文件 > appsettings.json > 硬编码默认值
```

## 环境变量清单

| 环境变量 | 用途 | 默认值 |
|----------|------|--------|
| `BAIHUA_HOME` | 百花数据根目录（内含 vaults/db/logs） | `~\.baihua` (Windows) / `~/.baihua` (Linux) |
| `BAIHUA_ENCRYPTION_KEY` | API Key 加密密钥（可选，优先于自动生成的密钥文件） | 空 |
| `BAIHUA_AI_ANTHROPIC_BASE_URL` | Anthropic API 代理地址（可选） | 空 |
| `ASPNETCORE_URLS` | 服务监听地址 | `http://0.0.0.0:8788` / `8791` / `8790` |

## 数据库表结构

### Vaults

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | int (PK) | 自增主键 |
| VaultId | string (唯一) | 业务标识 |
| Name | string | 知识库名称 |
| Path | string (MaxLength=1000) | 文件系统路径 |
| IsActive | bool | 是否为当前活跃知识库 |
| CreatedAt / UpdatedAt | DateTime | 自动维护时间戳 |

### AiProviderSettings

| 字段 | 类型 | 说明 |
|------|------|------|
| Id | int (PK) | 自增主键 |
| ProviderId | string (唯一) | 提供商标识 |
| ProviderName | string | 显示名称 |
| BaseUrl | string | API 基础 URL |
| EncryptedApiKey | string | 加密存储的 API Key |
| ModelsJson | string | 模型列表（JSON 数组） |
| IsMain / IsEnabled | bool | 主提供商 / 启用状态 |
| SortOrder | int | 排序权重 |

### Tasks

| 字段 | 类型 | 说明 |
|------|------|------|
| TaskId | string (唯一) | 任务标识 |
| TaskType | string | 任务类型（ai_query / split_atom_notes） |
| Status | string | 执行状态 |
| Progress | int | 进度 0-100 |
| Input / Output | string (JSON) | 任务输入输出 |
| Error | string | 错误信息 |

### AuthorizedDevices

| 字段 | 类型 | 说明 |
|------|------|------|
| DeviceId | string (唯一) | 设备标识 |
| DeviceName | string | 设备名称 |
| AccessToken | string (唯一) | 访问令牌 |
| Status | string | 授权状态 |
| IpAddress | string | IP 地址 |
| TokenExpiresAt | DateTime? | 令牌过期时间 |

### ServerAddressSettings

| 字段 | 类型 | 说明 |
|------|------|------|
| HttpUrl | string | HTTP 地址 |
| ServerInstanceId | string | 服务器实例标识 |

## JSON 文件详情

| 文件 | 路径 | 格式示例 |
|------|------|----------|
| `vault.root.path.json` | _(已废弃)_ | 知识库根路径改为环境变量 `TASKRUNNER_VAULT_ROOT` 配置，不再使用此文件 |
| `admin.password.json` | `{BaseDirectory}/` | `{"AdminPasswordHash":"<SHA256 Base64>"}` |
| `webui.settings.json` | `{BaseDirectory}/` | `{"BackendUrl":"http://127.0.0.1:8788",...}` |
| `user_preferences.json` | `{BaseDirectory}/data/` | `{"FontSize":16.0,"Theme":"light","AutoSave":true}` |

## 历史迁移

旧版知识库路径存储在 `vault.runtime.settings.json` 中。系统启动时 `MigrateFromJsonIfNeeded()` 会自动迁移到 SQLite `Vaults` 表，原文件重命名为 `.backup`。

## 备份与恢复

### 备份格式

全量备份为 ZIP 文件，内部结构：

```
huaji_backup_yyyyMMdd_HHmmss.zip
├── manifest.json          # 元数据（版本、时间、源平台、是否有密码）
├── db/
│   ├── vaults.json        # Vaults 表（含相对路径，用于跨平台重映射）
│   ├── ai_providers.json  # AI 提供商配置（API Key 用备份密码加密或明文）
│   ├── tasks.json         # 任务历史
│   ├── devices.json       # 授权设备（恢复后标记为 PendingReauth）
│   └── server_address.json # 服务器地址（ServerInstanceId 不恢复）
├── config/
│   ├── webui_settings.json
│   └── user_preferences.json
└── vaults/
    └── {vault_name}/
        ├── notes/
        ├── cards/
        └── images/
```

### 跨平台路径重映射

- 备份时：每个知识库路径同时存储绝对路径和相对路径（相对于知识库根路径）
- 相对路径统一使用正斜杠 `/`，跨平台兼容
- 恢复时：优先使用 `相对路径 + 新根路径` 组合，实现 Windows → Linux 等跨平台恢复

### API Key 安全处理

| 场景 | 处理方式 |
|------|----------|
| 有备份密码 | 解密 API Key → 用备份密码通过 AES-256-CBC (PBKDF2) 重新加密 → 存入备份 |
| 无备份密码 | 解密 API Key → 以 `PLAINTEXT:` 前缀明文存入备份（仅限本地可信环境） |
| 恢复时 | 用备份密码解密 → 用目标机器指纹通过 AES-256-GCM 重新加密 → 存入数据库 |

### 不恢复的数据

- `ServerAddressSettings.ServerInstanceId`：本机唯一标识，恢复时保留本机的
- `AuthorizedDevices.Status`：恢复后标记为 `PendingReauth`，需重新授权
- 内存中的会话令牌：临时数据，不备份
