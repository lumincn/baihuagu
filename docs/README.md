# 百花（Baihua）文档中心

## 文档目标

本文档中心提供项目相关的设计文档、配置指南和运维手册。**贯彻代码即文档：历史计划、一次性修复记录、已完成快照一律删除，以 git 历史为准。**

## 现有文档

| 文档 | 用途 | 状态 |
|---|---|---|
| [ARCHITECTURE_HOME_SERVER.md](ARCHITECTURE_HOME_SERVER.md) | Home Server 架构方案（4 服务拓扑 / 端口） | 保留 |
| [CONFIG_STORAGE_ARCHITECTURE.md](CONFIG_STORAGE_ARCHITECTURE.md) | 配置与存储架构（三库分离 / 密钥加密） | 保留 |
| [mg-auth-config-implementation.md](mg-auth-config-implementation.md) | `/mg/auth/config` 端点实现记录 | 保留 |
| [SDK_ARCHITECTURE_PLAN.md](SDK_ARCHITECTURE_PLAN.md) | 百花 SDK 架构设计（6 SDK SRP 拆分，2026-07-31 完成快照） | 保留 |
| [sync_protocol.md](sync_protocol.md) | 移动端与后端同步协议（需按 /mg/* 路径更新） | 保留 |
| [mobile_vault_distribution.md](mobile_vault_distribution.md) | 知识库分发/导入方案 | 保留 |
| [huapu-communication-optimization.md](huapu-communication-optimization.md) | 花圃↔百花三端通信优化方案 | 保留 |

## 相关文档位置

- **项目根目录**：`README.md` - 项目介绍（baihuagu 仓库）
- **开发助手**：`AGENTS.md` - 开发操作说明（构建 / 测试命令）
- **移动端文档**：`arkts/docs/`（鸿蒙）/ `kotlin/docs/`（安卓）
- **上架与合规**：鸿蒙 `arkts/docs/` 下 AGC_SETUP_GUIDE / AGC_SUBMISSION_COPY / CHECKLIST_BEFORE_SUBMIT / APP_ICON_GUIDE

## 补充说明

- 历史分析类、故障快照类、临时修复记录类文档已清理（2026-08-06），以 git 历史为准
- 服务端 OneHop（TCP 配对）已于 2026-08 删除，相关文档表述仅作历史记录（详见 `ONEHOP_SIMPLIFICATION_PLAN.md`）

最后更新：2026-08-08
