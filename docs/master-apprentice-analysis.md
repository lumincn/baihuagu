# 拜师系统（Master-Apprentice）跨平台对比分析

> 生成日期：2026-07-27（第十轮 review 更新 — 花记优化落地）
> 分析范围：鸿蒙版 (ArkTS)、安卓版 (Kotlin/Compose)、WebUI (Blazor)、花圃 (MAUI)

---

## 一、系统架构概览

| 平台 | 技术栈 | 数据存储 | API 通信 | AI 接入方式 |
|------|--------|----------|----------|------------|
| 鸿蒙版 | ArkTS | 本地 RDB (SQLite) | 直连 DeepSeek API | 客户端直连 |
| 安卓版 | Kotlin + Jetpack Compose | 本地 SQLite | 直连 DeepSeek API | 客户端直连 |
| WebUI | Blazor Server | 服务端 SQLite | REST API + SSE | 服务端中转 |
| 花圃 | MAUI (Blazor Hybrid) | 服务端 SQLite + 本地缓存 | REST API + SSE | 服务端中转 |

### 后端服务架构

| 组件 | 位置 | 说明 |
|------|------|------|
| MasterController | `TaskRunner.Family/Controllers/AI/` | 16 个 API 端点（CRUD + 对话 + 阶段 + 知识库 + 数据淘汰） |
| VaultFocusController | `TaskRunner.Family/Controllers/AI/` | 知识库聚焦 4 个端点 |
| MasterPromptBuilder | `TaskRunner.Family/Services/` | 行业映射 + 阶段角色 + System Prompt 构建 + 安全过滤 |
| MasterDataRetentionService | `TaskRunner.Family/Services/` | 后台定时任务（24h 周期，7天压缩 + 30天淘汰） |
| MasterEntities | `TaskRunner.Data/Entities/Family/` | 7 张表（Master, MasterConversation, StageSummary, ApprenticeProfile, ExamCheckpoint, VaultFocusState, VaultFreeState） |
| MasterDtos | `TaskRunner.Contracts/Master/` | 共享 DTO 定义 |

---

## 二、功能对比

### 2.1 核心功能矩阵

| 功能 | 鸿蒙版 | 安卓版 | WebUI | 花圃 |
|------|--------|--------|-------|------|
| 师父列表管理 | ✅ | ✅ | ✅ | ✅ |
| 行业-师父映射 | ✅ | ✅ | ✅ | ✅ |
| 5阶段修炼体系 | ✅ | ✅ | ✅ | ✅ |
| 学徒画像 | ✅ (可编辑) | ✅ (可编辑) | ✅ (可编辑) | ✅ (可编辑) |
| 流式对话 | ✅ (sendMessageStream) | ✅ (Flow 真正流式) | ✅ (SSE) | ✅ (SSE) |
| 流式降级 fallback | ✅ | ✅ (非流式兜底) | ✅ (SSE+fallback) | ✅ |
| 阶段摘要生成 | ✅ (AI 自动生成) | ✅ (advanceStage 触发) | ✅ (服务端) | ✅ (服务端) |
| 工作记忆限制 | ✅ (20条) | ✅ (20条) | ✅ | ✅ (20条) |
| 数据驱逐/压缩 | ✅ (7天/30天) | ✅ (7天/30天) | ✅ (服务端API) | ✅ (服务端API) |
| 免责声明弹窗 | ✅ (含年龄确认+声明复选, 130行) | ✅ (含年龄确认+声明复选, 95行) | ✅ (含年龄确认) | ✅ (含年龄确认+声明复选) |
| 知识库联动 | ✅ (数据+UI+Prompt注入) | ✅ (数据+UI+Prompt注入) | ✅ (服务端API) | ✅ (数据+UI+Prompt注入) |
| Markdown 渲染 | ✅ (SimpleMarkdown) | ✅ (WebView) | ✅ (Markdig) | ✅ (MarkdownView) |
| 停止生成 | ✅ | ✅ | ✅ | ✅ |
| 快速提问按钮 | ✅ | ✅ | ✅ | ✅ |
| 阶段完成小结 UI | ✅ (祝福语+摘要+纠正) | ✅ (祝福语+摘要+纠正) 🆕 | ✅ (弹窗+祝福+纠正) | ✅ (摘要弹窗+祝福+纠正) |
| 阶段祝福语 | ✅ (STAGE_BLESSINGS) | ✅ (StageBlessings.generate) 🆕 | ✅ (后端生成) | ✅ (后端返回) |
| 关键纠正展示 | ✅ | ✅ | ✅ 🆕 | ✅ 🆕 |
| 本地缓存 | ❌ | ❌ | ✅ (localStorage 聊天) | ✅ (SecureStore 全量) |
| API Key 预检 | ✅ (调/models验证) | ✅ (5状态枚举+调/models验证) | ✅ (GlobalStateService) | ✅ (调'api/ai/providers) |
| AI 未配置警告 | ✅ (简单横幅) | ✅ (5状态枚举+顶栏图标) | ✅ (弹窗) | ✅ (横幅) |
| 对话历史同步 | ❌ (仅本地) | ❌ (仅本地) | ❌ (仅localStorage) | ✅ (双向同步) |
| 知识库关联 UI | ✅ (选择器弹窗) | ✅ (选择器弹窗) | ✅ (多选切换) 🆕 | ✅ (最完整, 多选) |
| 数据淘汰定时任务 | ✅ (本地自动) | ✅ (本地自动) | ⚠️ (手动触发) | ⚠️ (依赖后端) |

### 2.2 阶段定义

五个阶段在所有平台保持一致：

```
入道 → 筑基 → 精进 → 磨砺 → 出师
```

| 阶段 | 角色定位 | 核心职责 | Prompt 风格 |
|------|----------|----------|-------------|
| 入道 | 引路人 | 评估基础、明确目标 | 温和、好奇、善问，通过提问引导自我认知 |
| 筑基 | 严师 | 建立知识框架、每日任务 | 有耐心但要求严格，扎实掌握基础 |
| 精进 | 匠人 | 分科细化、攻克薄弱 | 极其耐心、绝不放过细节错误 |
| 磨砺 | 考官 | 模拟考试、查漏补缺 | 严格限时，考后分析错题 |
| 出师 | 前辈 | 实战建议、报考指导 | 分享经验和技巧 |

### 2.3 行业-师父映射

| 行业 | 师父名 | 适用领域 |
|------|--------|----------|
| 中医/医学 | 岐伯 | 医学类考试 |
| 计算机/IT | 图灵 | 计算机认证 |
| 会计/财务 | 算圣 | 财会类考试 |
| 教资/教育 | 夫子 | 教师资格 |
| 法律 | 廷尉 | 法律职业资格 |
| 建筑 | 鲁班 | 建筑工程类 |
| 通用 | 先生 | 其他学习目标 |

### 2.4 考试大纲资源

后端内嵌 4 份考试大纲 JSON（`services/TaskRunner.Family/data/ExamOutlines/`）：

| 文件 | 覆盖考试 |
|------|----------|
| 执业医师.json | 执业医师资格考试五阶段大纲 |
| 软考.json | 软考五阶段大纲 |
| 会计.json | 会计类考试大纲 |
| 教资.json | 教师资格考试大纲 |

---

## 三、各平台优缺点分析

### 3.1 鸿蒙版 (ArkTS) — 第十轮 review

**优点**
- ✅ 纯本地存储，离线可用，无网络依赖
- ✅ 完整的数据驱逐机制（7天压缩对话、30天合并到画像）
- ✅ 知识库联动最完整：数据层 + UI 选择器 + Prompt 注入
- ✅ 完整的工作记忆系统（20条对话限制）
- ✅ 阶段摘要自动生成并持久化
- ✅ 流式对话 + fallback，容错性提升
- ✅ Markdown 渲染支持（SimpleMarkdown）
- ✅ 免责声明弹窗（130行，含年龄确认+受限行业特别声明）
- ✅ 学徒画像可编辑
- ✅ 停止生成功能
- ✅ 快速提问按钮
- ✅ **阶段祝福语**（STAGE_BLESSINGS 常量，每阶段3条随机模板，四端唯一）
- ✅ **阶段完成小结 UI 最完整**（祝福语+摘要+关键纠正，四端唯一）
- ✅ **关键纠正展示**（⚠️ 橙色高亮）
- ✅ AI 未配置警告横幅
- ✅ 代码结构清晰，类型安全

**缺点**
- ❌ 直接调用 AI API，无服务端中转，Key 暴露风险
- ❌ 数据仅本地存储，设备丢失无法恢复
- ❌ API Key 预检仅检查非空，未实际验证有效性 → ✅ **已增强**：调用 `/models` 端点实际验证（第九轮）
- ❌ 无对话历史同步
- ❌ 无知识库多选关联

**文件位置**
- 服务实现：`MasterService.ets`
- 页面：`MasterListPage.ets`、`MasterChatPage.ets`、`MasterProfilePage.ets`、`MasterStagePage.ets`
- 组件：`SimpleMarkdown.ets`、`MasterDisclaimerDialog.ets`、`DiscoverTabContent.ets`

---

### 3.2 安卓版 (Kotlin/Compose) — 第十轮 review

**本轮优化亮点** 🆕
- ✅ **阶段祝福语** — 新增 `StageBlessings` object，5阶段×3模板随机生成，与鸿蒙端模板一致
- ✅ **阶段完成弹窗增强** — `StageCompleteDialog` 新增祝福语展示（💌 师父寄语卡片）
- ✅ **关键纠正 AI 生成** — `advanceStage` 请求 AI 按结构化格式输出摘要，`extractKeyCorrections()` 解析【关键纠正】部分
- ✅ **免责声明增强** — 增加 `confirmedDisclaimer` 复选框，确认按钮需同时勾选声明+18+（如适用）

**优点**
- ✅ 声明式 UI，Compose 现代化
- ✅ Flow 响应式数据流，真正流式对话 + fallback
- ✅ 知识库联动完整：数据层 + UI 选择器 + Prompt 注入
- ✅ Markdown 渲染支持（WebView）
- ✅ 代码组织良好（MasterPrompts、StageDefs、MasterNames 分离）
- ✅ 阶段推进时自动生成摘要
- ✅ 数据驱逐机制完善，自动触发
- ✅ 快速提问按钮降低使用门槛
- ✅ 停止生成功能
- ✅ 免责声明弹窗（95行，含年龄确认+受限行业特别声明）
- ✅ 学徒画像可编辑
- ✅ 知识库关联 UI（选择器弹窗）
- ✅ **AI 预检最精细**（5状态枚举：Idle/Checking/Valid/Invalid/NotConfigured，顶栏状态图标+横幅+发送按钮联动，调 `/models` 验证）
- ✅ **阶段完成小结 UI**（祝福语+摘要+关键纠正弹窗）
- ✅ **关键纠正展示**（⚠️ 橙色高亮，AI 结构化生成+正则解析）
- ✅ **阶段祝福语**（StageBlessings.generate，5阶段×3模板）
- ✅ **免责声明完整**（18+确认+声明复选框，确认按钮联动）

**缺点**
- ❌ 直连 AI API，Key 暴露风险
- ❌ 数据仅本地存储
- ❌ 无阶段祝福语 → ✅ **已实现**：StageBlessings object（第十轮）
- ❌ 无对话历史同步
- ❌ 与鸿蒙版存在代码重复，维护成本高

**文件位置**
- 服务实现：`MasterService.kt`（含 MasterPrompts、StageDefs、MasterNames 内嵌 object）
- 数据存储：`VaultFocusStore.kt`（SharedPreferences）、`NoteDatabase.kt`
- 页面：`MasterListPage.kt`、`MasterChatPage.kt`、`MasterStagePage.kt`、`MasterProfilePage.kt`
- 组件：`MasterDisclaimerDialog.kt`

---

### 3.3 WebUI (Blazor Server) — 第九轮 review

**本轮优化亮点** 🆕
- ✅ **阶段完成弹窗** — 完成阶段后展示祝福语+摘要+关键纠正的弹窗（对齐鸿蒙端）
- ✅ **知识库关联 UI** — 支持多选切换关联/解绑知识库
- ✅ **关键纠正展示** — 已完成阶段在时间线中显示 ⚠️ 关键纠正

**上一轮（第四轮）优化成果**
- ✅ 真正的 SSE 流式对话
- ✅ 服务端统一管理，数据一致性好
- ✅ Markdown 渲染支持（Markdig 库）
- ✅ 双栏布局，师父列表与对话同屏
- ✅ 停止生成功能
- ✅ AI 配置预检（GlobalStateService），弹窗提示
- ✅ 本地聊天历史缓存（localStorage）
- ✅ 免责声明弹窗
- ✅ 删除师父功能
- ✅ 数据驱逐、画像编辑、知识库联动（服务端 API 全覆盖）
- ✅ 快速提问按钮
- ✅ 流式 fallback

**缺点**
- ❌ 需要服务端运行，无法独立运行
- ❌ 依赖浏览器，移动端体验受限
- ❌ 阶段完成小结 UI 不完整（仅摘要文本，无弹窗/纠正/祝福语）
- ❌ 无关键纠正展示
- ❌ 无知识库关联 UI
- ❌ 对话历史仅 localStorage，无服务端同步
- ❌ 数据淘汰仅手动触发，无定时任务

**文件位置**
- 页面：`MasterChat.razor`、`MasterStage.razor`、`MasterDisclaimerDialog.razor`
- API：`ApiService.cs`

---

### 3.4 花圃 (MAUI) — 第九轮 review

**本轮优化亮点** 🆕
- ✅ **关键纠正展示** — 阶段完成弹窗中新增 ⚠️ 关键纠正区域
- ✅ **阶段祝福语** — 后端 stage-complete 返回 Blessing 字段，弹窗中展示
- ✅ **免责声明增强** — MasterChatPage 增加 18+ 年龄确认复选框 + 声明确认复选框，确认按钮需两者均勾选才启用

**上一轮（第七轮）优化成果**
- ✅ 基于服务端 API，数据与 WebUI 一致
- ✅ SSE 流式对话 + fallback
- ✅ 本地缓存机制（SecureStore 三要素：对话/画像/列表，200条/师父）
- ✅ **对话历史双向同步**（四端唯一：GetConversationsFromServerAsync + SyncConversationsToServerAsync）
- ✅ **API Key 预检**（CheckAiConfiguredAsync 调 /api/ai/providers）
- ✅ **AI 未配置警告**（横幅 + 智能禁用发送按钮）
- ✅ 知识库联动完整（数据+UI+Prompt注入）
- ✅ **知识库关联 UI 最完整**（多选切换，四端唯一）
- ✅ 学徒画像可编辑
- ✅ 快速提问按钮
- ✅ 停止生成功能
- ✅ Markdown 渲染
- ✅ 完整的错误处理和加载状态
- ✅ 利用 BaihuaSdk 的签名通信，安全可靠
- ✅ 跨平台（Android/iOS）
- ✅ 事件驱动更新（OnProfileUpdated / OnMastersUpdated）
- ✅ 免责声明弹窗
- ✅ 阶段完成弹窗

**缺点**
- ❌ 依赖服务端，离线不可用
- ❌ 无阶段祝福语
- ❌ 无关键纠正展示
- ❌ 免责声明为简化版（无18+年龄确认复选框）
- ❌ 鸿蒙/安卓本地存储的数据无法直接与花圃互通（架构差异）

**文件位置**
- 数据模型：`MasterModels.cs`
- 服务层：`MasterService.cs`
- 缓存服务：`MasterCacheService.cs`
- 后端 API：`MasterController.cs`、`VaultFocusController.cs`
- 契约 DTO：`MasterDtos.cs`
- 页面：`MasterChatPage.razor`、`MasterProfilePage.razor`、`MasterStagePage.razor`、`MasterListPage.razor`

---

## 四、跨平台差距与优化建议

### 4.1 当前差距矩阵

| 维度 | 最强 | 中等 | 最弱 |
|------|------|------|------|
| 流式对话 | 安卓（Flow+fallback+Prompt注入） | 鸿蒙、花圃（流式+fallback） | WebUI（SSE+fallback） |
| Markdown 渲染 | ✅ 全平台覆盖 | — | — |
| 数据驱逐 | ✅ 全平台覆盖 | — | — |
| 知识库联动 | 鸿蒙、安卓、花圃（数据+UI+Prompt注入） | WebUI（服务端API） | — |
| 画像管理 | ✅ 全平台可编辑 | — | — |
| 快速提问 | ✅ 全平台覆盖 | — | — |
| 停止生成 | ✅ 全平台覆盖 | — | — |
| 本地缓存 | 花圃（全量SecureStore） | WebUI（localStorage聊天） | 鸿蒙、安卓（仅SQLite） |
| 离线可用 | 鸿蒙、安卓（纯本地） | 花圃（缓存优先） | WebUI（必须在线） |
| AI 预检 | 安卓（5状态枚举+顶栏图标） | WebUI、花圃（API验证）、鸿蒙（调/models验证） 🆕 | — |
| 免责声明 | 鸿蒙（130行,含18+确认）、安卓（95行,含18+确认） | WebUI（组件化）、花圃（含18+确认+声明复选） 🆕 | — |
| 阶段完成小结 UI | ✅ 全平台覆盖（鸿蒙/安卓/花圃含弹窗+祝福+纠正，WebUI 含弹窗+祝福+纠正） |
| 阶段祝福语 | ✅ **全平台覆盖**（鸿蒙/安卓本地模板，WebUI/花圃后端生成） |
| 关键纠正展示 | ✅ 全平台覆盖 | — | — |
| 对话历史同步 | 花圃（双向同步） | WebUI（localStorage） | 鸿蒙、安卓（仅本地） |
| 知识库关联 UI | 花圃（多选切换）、WebUI（多选切换） 🆕 | 鸿蒙、安卓（单选弹窗） | — |

### 4.2 各平台待优化项

#### 🔴 鸿蒙版
1. **对话历史同步** — 增加与服务端的对话同步能力

#### 🔴 安卓版
1. **对话历史同步** — 增加与服务端的对话同步能力

#### 🔴 WebUI
1. **对话历史持久化** — 当前仅 localStorage，可考虑服务端同步
2. **数据淘汰定时任务** — 当前仅手动触发

#### 🔴 花圃
1. **增强离线模式** — 当前缓存优先加载，需增加网络断开检测与自动恢复

### 4.3 跨平台统一建议

| 优先级 | 项目 | 说明 |
|--------|------|------|

| **P1** | 鸿蒙/安卓 Key 安全 | 考虑将 AI Key 迁移至服务端中转 |
| **P2** | 统一 Prompt 管理 | `Core.Shared` 集中管理所有 Prompt 模板，客户端仅传参 |
| **P2** | 对话历史同步统一 | 鸿蒙/安卓/WebUI 增加与服务端的对话同步 |
| **P3** | 统一声明文案 | 各端免责声明内容保持一致 |
| **P3** | WebUI 数据淘汰定时任务 | 后端已有 BackgroundService，WebUI 可自动触发 |

### 4.4 推荐架构演进

```
TaskRunner.Contracts (共享 DTO)
    └── Master/ (MasterDto, ApprenticeProfileDto, StageSummaryDto, VaultFocusDto)

Core.Shared (服务端共享)
    └── MasterEngine (核心业务逻辑 + Prompt 管理 + 数据驱逐)
    └── VaultFocusService (知识库联动 + Prompt 注入)

TaskRunner.Family (API 服务)
    └── MasterController (REST API + SSE + 数据淘汰端点)

BaihuaSdk (移动端 SDK)
    └── MasterService (IMasterService 接口 + HttpTransport)

统一能力矩阵（当前状态）：
┌─────────────────┬──────────────┬──────────────┬──────────────┬──────────────┐
│ 能力            │ 鸿蒙         │ 安卓         │ WebUI        │ 花圃         │
├─────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
│ 流式对话        │ ✅+fb        │ ✅+fb+注入   │ ✅+fb        │ ✅+fb        │
│ 数据驱逐        │ ✅ 自动      │ ✅ 自动      │ ⚠️ 手动      │ ⚠️ 依赖后端  │
│ 知识库联动      │ ✅ 完整      │ ✅ 完整      │ ✅ 完整+UI   │ ✅ 完整      │
│ Markdown        │ ✅           │ ✅           │ ✅           │ ✅           │
│ 画像编辑        │ ✅           │ ✅           │ ✅           │ ✅           │
│ 快速提问        │ ✅           │ ✅           │ ✅           │ ✅           │
│ 停止生成        │ ✅           │ ✅           │ ✅           │ ✅           │
│ 阶段小结UI      │ ✅ 祝福+纠正 │ ✅ 纠正      │ ✅ 祝福+纠正 │ ✅ 祝福+纠正 │
│ 祝福语          │ ✅ 本地模板   │ ✅ 本地模板   │ ✅ 后端生成  │ ✅ 后端返回  │
│ AI预检          │ ✅ 调API     │ ✅ 5状态     │ ✅           │ ✅           │
│ 对话同步        │ ❌           │ ❌           │ ❌           │ ✅ 双向      │
│ 本地缓存        │ ❌           │ ❌           │ 聊天         │ 全量         │
│ 离线可用        │ ✅           │ ✅           │ ❌           │ 部分         │
└─────────────────┴──────────────┴──────────────┴──────────────┴──────────────┘
```

---

## 五、关于"自由/路径"模式

用户反馈的"松散严格模式"对应鸿蒙版中的 `homeMode`（自由模式/路径模式），实现位置：

- `Index.ets`: 第 70-72 行声明 `homeMode`，第 842-900 行实现选择对话框
- `BrowseVaultPage.ets`: 使用 `homeMode` 控制知识库浏览行为

**结论**：
1. 该模式仅存在于鸿蒙版知识库浏览功能中
2. 与拜师系统无关
3. 花圃实现中**不包含**此模式切换
4. 如需移除，需在鸿蒙项目中修改

---

## 六、后端 API 端点全览

### MasterController（16 个端点）

| HTTP 方法 | 路由 | 功能 |
|-----------|------|------|
| POST | `api/master/create` | 创建师父（goal+industry → masterId+masterName+五阶段+AI欢迎语） |
| POST | `api/master/chat/stream` | SSE 流式对话（System Prompt+画像+摘要+知识库注入→AI→流式返回） |
| POST | `api/master/{id}/stage-complete` | 阶段完成（AI生成摘要→推进阶段→知识库状态切换） |
| GET | `api/master/{id}/profile` | 获取学徒画像 |
| PUT | `api/master/{id}/profile` | 更新学徒画像 |
| POST | `api/master/{id}/assess` | 能力评估（daily/weekly/stage/capability） |
| GET | `api/master` | 列出所有活跃师父 |
| DELETE | `api/master/{id}` | 删除师父（软删除） |
| POST | `api/master/{id}/compress` | 数据压缩（7天前对话→AI摘要→删除原文） |
| POST | `api/master/{id}/evict` | 数据淘汰（30天前摘要→AI提取画像→删除摘要） |
| POST | `api/master/evict-all` | 批量驱逐所有师父旧数据 |
| GET | `api/master/{id}/vault-focus` | 获取知识库关联列表 |
| POST | `api/master/{id}/vault-focus` | 更新知识库关联 |
| DELETE | `api/master/{id}/vault-focus/{vaultId}` | 取消知识库关联 |
| GET | `api/master/{id}/conversations` | 获取对话历史（支持 limit 参数） |
| POST | `api/master/{id}/conversations/sync` | 批量同步对话 |

### VaultFocusController（4 个端点）

| HTTP 方法 | 路由 | 功能 |
|-----------|------|------|
| GET | `api/master/{masterId}/vault-focus` | 获取聚焦的知识库列表 |
| POST | `api/master/{masterId}/vault-focus/focus` | 聚焦知识库 |
| POST | `api/master/{masterId}/vault-focus/archive` | 归档知识库 |
| GET | `api/master/{masterId}/vault-focus/all` | 获取所有知识库状态 |

---

## 七、数据库模型全览

### 7 张表定义

| 表名 | 用途 | 关键索引 |
|------|------|----------|
| Masters | 师父主表 | MasterId 唯一 |
| MasterConversations | 对话记录 | MasterId + (MasterId, CreatedAt) 复合 |
| StageSummaries | 阶段摘要 | (MasterId, StageName) 唯一 |
| ApprenticeProfiles | 学徒画像 | MasterId 唯一 |
| ExamCheckpoints | 考试检查点 | MasterId + (MasterId, StageName) 复合 |
| VaultFocusStates | 知识库聚焦关联 | MasterId + (MasterId, VaultId) 唯一 |
| VaultFreeStates | 知识库自由状态 | VaultId 唯一 |

### 内容安全过滤关键词

`真实诊断`、`开处方`、`开药方`、`真实法律建议`、`代理诉讼`、`医疗诊断`、`处方药`、`手术方案`、`法律代理`

---

## 八、总结

### 八轮优化成果演进

| 轮次 | 鸿蒙 | 安卓 | WebUI | 花圃 |
|------|------|------|-------|------|
| 第一轮 | 流式对话 ✅<br>免责声明 ✅<br>数据驱逐 ✅ | 真正流式 ✅<br>免责声明 ✅<br>数据驱逐 ✅ | AI 配置检查 ✅<br>localStorage ✅ | 本地缓存 ✅<br>画像编辑 ✅<br>快速提问 ✅ |
| 第二轮 | 流式 fallback ✅<br>Markdown ✅<br>知识库 UI ✅ | 知识库联动 ✅<br>Prompt 注入 ✅ | — | — |
| 第三轮 | Markdown ✅<br>知识库 UI ✅<br>fallback ✅ | Markdown ✅<br>快速提问 ✅<br>Prompt 注入 ✅ | 删除师父 ✅ | Markdown ✅<br>fallback ✅<br>快速提问 ✅<br>页面优化 ✅ |
| 第四轮 | — | — | 数据驱逐 ✅<br>画像编辑 ✅<br>知识库联动 ✅<br>快速提问 ✅<br>fallback ✅ | 免责声明 ✅<br>缓存持久化 ✅ |
| 第五轮 | 画像编辑 ✅<br>知识库 CRUD ✅ | 停止生成 ✅<br>快速提问 ✅<br>fallback ✅<br>自动驱逐 ✅ | — | — |
| 第六轮 | 停止生成 ✅<br>快速提问 ✅<br>知识库 Prompt 注入 ✅ | 画像编辑 ✅<br>知识库关联 UI ✅ | — | — |
| 第七轮 | — | — | — | 对话历史双向同步 ✅<br>API Key 预检 ✅<br>AI 未配置警告 ✅<br>知识库联动完整 ✅ |
| **第八轮** | 阶段祝福语 ✅<br>阶段完成小结UI(祝福+纠正) ✅<br>AI未配置警告 ✅ | AI预检5状态 ✅<br>阶段完成小结UI(纠正) ✅<br>知识库关联UI ✅ | — | 知识库关联UI(多选) ✅<br>阶段完成弹窗 ✅ |
| **第九轮** | API Key预检增强(调/models) ✅ | — | 阶段完成弹窗(祝福+纠正) ✅<br>知识库关联UI(多选) ✅ | 关键纠正展示 ✅<br>免责声明增强(18+确认) ✅ |
| **第十轮** | — | 阶段祝福语(StageBlessings) ✅<br>阶段完成弹窗增强(祝福+纠正) ✅<br>关键纠正AI生成+解析 ✅<br>免责声明增强(声明复选) ✅ | — | — |

### 已基本解决的问题 ✅

| 问题 | 解决情况 |
|------|----------|
| Markdown 渲染 | ✅ 四端全覆盖 |
| 流式 fallback | ✅ 全平台已实现 |
| 知识库联动 | ✅ 全平台覆盖（鸿蒙/安卓/花圃含数据+UI+Prompt注入，WebUI 服务端 API） |
| 免责声明 | ✅ 四端全覆盖（花圃为简化版） |
| 学徒画像编辑 | ✅ 四端全覆盖 |
| 快速提问 | ✅ 四端全覆盖 |
| 停止生成 | ✅ 四端全覆盖 |
| 数据驱逐 | ✅ 全平台覆盖 |
| 知识库 Prompt 注入 | ✅ 全平台覆盖 |
| AI 未配置警告 | ✅ 四端全覆盖 |
| 阶段完成小结 UI | ✅ 全平台覆盖（鸿蒙/安卓/花圃含弹窗，WebUI 含弹窗+祝福+纠正） |
| 阶段祝福语 | ✅ **全平台覆盖**（鸿蒙/安卓本地模板，WebUI/花圃后端生成） |
| 关键纠正展示 | ✅ **全平台覆盖** |
| API Key 预检 | ✅ **全平台覆盖**（鸿蒙调/models验证，安卓5状态枚举，WebUI/花圃API验证） |
| 免责声明 | ✅ **四端全覆盖**（含18+确认+声明复选） |
| 知识库关联 UI | ✅ **全平台覆盖**（花圃/WebUI多选，鸿蒙/安卓单选弹窗） |

### 仍需优化（按优先级）

**P1 — 功能补强（影响单平台体验）**
1. **鸿蒙/安卓 Key 安全** — 迁移至服务端
2. **对话历史同步统一** — 鸿蒙/安卓/WebUI 增加与服务端同步

**P2 — 架构改进**
4. **统一 Prompt 管理** — `Core.Shared` 集中维护
5. **WebUI 数据淘汰定时任务** — 自动触发

**P3 — 体验优化**
6. **增强花圃离线模式** — 网络断开检测与自动恢复

### 下一步建议

1. **架构统一** — Prompt 管理迁移到 `Core.Shared`，四端引用同一模板
2. **Key 安全迁移** — 鸿蒙/安卓将 AI Key 迁移至服务端
3. **对话历史同步统一** — 鸿蒙/安卓/WebUI 增加与服务端同步
4. **真机验证** — 所有变更部署到设备测试
