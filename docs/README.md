# Doctor Notes 文档中心

## 文档目标

本文档中心提供项目相关的设计文档、配置指南和运维手册。

## 现有文档

### 核心文档

| 文档 | 用途 |
|---|---|
| [ARCHITECTURE_HOME_SERVER.md](ARCHITECTURE_HOME_SERVER.md) | Home Server 架构方案 |
| [CONFIG_STORAGE_ARCHITECTURE.md](CONFIG_STORAGE_ARCHITECTURE.md) | 配置与存储架构 |
| [MDNS_CONFIGURATION.md](MDNS_CONFIGURATION.md) | mDNS 服务发现配置指南 |
| [NAMING_CONVENTION.md](NAMING_CONVENTION.md) | 项目多端命名规范 |
| [TODO_NEXT.md](TODO_NEXT.md) | 下一阶段任务清单 |
| [sync_protocol.md](sync_protocol.md) | 移动端与后端同步协议 |
| [mobile_vault_distribution.md](mobile_vault_distribution.md) | 知识库分发/导入方案 |
| [SDK_ARCHITECTURE_PLAN.md](SDK_ARCHITECTURE_PLAN.md) | 百花 SDK 架构设计（6 SDK SRP 拆分） |
| [huapu-communication-optimization.md](huapu-communication-optimization.md) | 花圃↔百花三端通信优化方案 |

### 上架与合规

| 文档 | 位置 | 用途 |
|---|---|---|
| AGC_SETUP_GUIDE.md | `arkts/docs/` | 华为 AGC 配置指南 |
| AGC_SUBMISSION_COPY.md | `arkts/docs/` | 应用商店上架文案 |
| CHECKLIST_BEFORE_SUBMIT.md | `arkts/docs/` | 上架前检查清单 |
| APP_ICON_GUIDE.md | `arkts/docs/` | 应用图标制作指南 |

## 相关文档位置

- **项目根目录**：`baihua/README.md` - 项目介绍
- **开发助手**：`baihua/AGENTS.md` - 开发助手说明
- **移动端文档**：`arkts/docs/` / `kotlin/docs/`
- **后端服务文档**：`services/Baihua.Family/` 内联文档
- **WebUI 文档**：`services/Baihua.Web/` 内联文档

## 补充说明

- 历史分析类、故障快照类、临时修复记录类文档已逐步清理。
- 与平台构建相关的专项文档继续保留在各子目录。

最后更新：2026-08-01
