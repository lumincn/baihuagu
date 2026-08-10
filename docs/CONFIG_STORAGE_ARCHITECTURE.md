# 配置与存储架构

本文档汇总系统所有配置项的存储位置、读写方及设计依据。

## 目录结构

```
$BAIHUA_HOME/                    # 百花数据根目录
├── db/                          # 数据库 + 密钥
│   ├── family.db                # 家庭/设备/任务等
│   ├── vault.db                 # 知识库管理
│   ├── ai.db                    # AI Provider 配置
│   ├── .baihua-key              # AES-256 加密密钥文件（自动生成）
│   └── *.db-shm / *.db-wal      # SQLite WAL 日志
├── vaults/                      # 知识库文件
│   └── local/{行业}/{知识库名}/
└── logs/                        # 运行日志
```

**跨盘映射**：通过 OS 级 symlink/junction 实现，代码无感。
```powershell
cmd /c mklink /J "C:\Users\lumin\.baihua" "D:\BaihuaData"
```

## 环境变量

### 核心

| 变量 | 用途 | 默认值 |
|------|------|--------|
| `BAIHUA_HOME` | 数据根目录（db + vaults + logs） | `%USERPROFILE%\.baihua` (Win) / `~/.baihua` (Linux) |
| `BAIHUA_ENCRYPTION_KEY` | 手动指定 API Key 加密密钥（优先级高于 .baihua-key 文件） | 空（自动生成 .baihua-key） |
| `ASPNETCORE_URLS` | 服务监听地址 | `http://0.0.0.0:8788` / `8791` / `8790` |
| `ASPNETCORE_ENVIRONMENT` | 运行环境 | `Production` |

### AI 请求参数

| 变量 | 用途 | 默认值 (appsettings.json) |
|------|------|--------------------------|
| `TASK_RUNNER_AI_API_URL` | AI 服务地址（WebUI 用） | `http://127.0.0.1:8791` |
| `TASK_RUNNER_AI_REQUEST_TIMEOUT_MINUTES` | AI 请求超时（分钟） | `5` |
| `TASK_RUNNER_AI_REQUEST_MAX_ATTEMPTS` | AI 请求最大重试次数 | `3` |
| `TASK_RUNNER_AI_REQUEST_INITIAL_BACKOFF_MS` | 重试初始退避（毫秒） | `1000` |
| `TASK_RUNNER_AI_REQUEST_MAX_BACKOFF_MS` | 重试最大退避（毫秒） | `30000` |

### 辅助

| 变量 | 用途 | 默认值 |
|------|------|--------|
| `BAIHUA_VAULT_URL` | Vault 服务地址（Family 内部调用） | `http://127.0.0.1:8790` |
| `BAIHUA_EMBEDDING_URL` | Embedding 服务地址 | 空（从 DB 配置读取） |
| `BAIHUA_EMBEDDING_MODEL` | Embedding 模型名 | 空（从 DB 配置读取） |
| `BAIHUA_LOCAL_MODEL_DIR` | 本地模型下载目录 | 空（使用 LocalAI 配置） |
| `BAIHUA_OBSIDIAN_EXE_PATH` | Obsidian 可执行文件路径 | 自动检测 |
| `BAIHUA_OBSIDIAN_EXE` | Obsidian 可执行文件路径（备用名） | 自动检测 |
| `WEBUI_CONFIG_DIR` | WebUI 配置文件目录 | `BAIHUA_HOME/db` |
| `USE_AVAHI` | Linux 下强制使用 Avahi mDNS | 空（自动检测） |
| `DOTNET_RUNNING_IN_CONTAINER` | Docker 环境检测（自动设置） | 空 |
| `OTEL_DEPLOYMENT_ENVIRONMENT` | OpenTelemetry 部署环境标识 | 空 |

## 优先级规则

```
环境变量 > 数据库/JSON 文件 > appsettings.json > 硬编码默认值
```

## 密钥与加密

### .baihua-key 密钥文件

| 属性 | 说明 |
|------|------|
| 位置 | `$BAIHUA_HOME/db/.baihua-key` |
| 内容 | 64 字符十六进制（256-bit AES 密钥） |
| 生成 | 首次启动自动生成，随机不可预测 |
| 权限 | Linux/macOS: 600（仅所有者读写）；Windows: 隐藏属性 |

### 加密/解密流程

```
API Key 明文
    ↓ 读 .baihua-key → SHA256 → HMAC-SHA256 派生 AES Key
    ↓ AES-256-GCM（随机 Nonce + 认证 Tag）
    ↓ Base64 + "A:" 前缀
存入 AiProviderSettings.EncryptedApiKey
```

### 密钥来源优先级

```
.baihua-key 文件  >  BAIHUA_ENCRYPTION_KEY 环境变量  >  机器指纹(OS级 MachineGuid)
```

**机器指纹**（仅在密钥文件丢失时兜底）：
- Windows: `HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid`
- Linux: `/etc/machine-id`
- macOS: `ioreg IOPlatformUUID`

## 存储机制一览

| 存储位置 | 数据内容 | 读写方 |
|----------|----------|--------|
| `family.db` | 家庭任务、成就、设备授权、Onboarding 状态等 | Baihua.Family |
| `vault.db` | 知识库配置、同步状态、搜索索引 | Baihua.Vault |
| `ai.db` | AI Provider 配置（加密 API Key）、Embedding 配置、模型列表 | Baihua.AI |
| `.baihua-key` | AES-256 加密密钥 | AiConfigService |
| `webui.settings.json` | WebUI 后端 URL 配置 | WebUI |
| `user_preferences.json` | 用户偏好（字体、主题） | WebUI |
| `appsettings.json` | 服务默认配置（端口、超时等） | 各服务 |
| 环境变量 | 部署级覆盖配置 | 全部服务 |

## 服务端口

| 服务 | 端口 | 说明 |
|------|------|------|
| Baihua.Family | 8788 | 家庭/亲子/设备管理 API |
| Baihua.AI | 8791 | AI 模型、聊天、配置 API |
| Baihua.Vault | 8790 | 知识库、同步、搜索 API |
| WebUI.Family | 5177 | Blazor Server 管理面板 |

## 备份与恢复

### 备份格式

```
baihua_backup_yyyyMMdd_HHmmss.zip
├── manifest.json          # 元数据
├── db/                    # 数据库 JSON 导出
├── config/                # WebUI 配置文件
└── vaults/                # 知识库文件
```

### API Key 安全

| 场景 | 处理方式 |
|------|----------|
| 有备份密码 | API Key 解密后用备份密码 AES-256-CBC 重加密 |
| 无备份密码 | API Key 明文导出（仅本地可信环境） |
| 恢复时 | 备份密码解密 → 用目标机器 .baihua-key 重加密 |

### 不恢复的数据

- `ServerInstanceId`：本机唯一标识，保留本机的
- 授权设备标记为 `PendingReauth`，需重新确认
- 内存会话令牌：临时数据，不备份
