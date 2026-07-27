# 拜师系统（Master-Apprentice）跨平台对比分析

> 生成日期：2026-07-27（第二轮 review 更新）
> 分析范围：鸿蒙版 (ArkTS)、安卓版 (Kotlin/Compose)、WebUI (Blazor)、花圃 (MAUI)

---

## 一、系统架构概览

| 平台 | 技术栈 | 数据存储 | API 通信 | AI 接入方式 |
|------|--------|----------|----------|------------|
| 鸿蒙版 | ArkTS | 本地 SQLite | 直连 DeepSeek API | 客户端直连 |
| 安卓版 | Kotlin + Jetpack Compose | 本地 SQLite | 直连 DeepSeek API | 客户端直连 |
| WebUI | Blazor Server | 服务端 SQLite | REST API + SSE | 服务端中转 |
| 花圃 | MAUI (Blazor Hybrid) | 服务端 SQLite + 本地缓存 | REST API + SSE | 服务端中转 |

---

## 二、功能对比

### 2.1 核心功能矩阵

| 功能 | 鸿蒙版 | 安卓版 | WebUI | 花圃 |
|------|--------|--------|-------|------|
| 师父列表管理 | ✅ | ✅ | ✅ | ✅ |
| 行业-师父映射 | ✅ | ✅ | ✅ | ✅ |
| 5阶段修炼体系 | ✅ | ✅ | ✅ | ✅ |
| 学徒画像 | ✅ | ✅ | ✅ (只读) | ✅ (可编辑) |
| 流式对话 | ✅ (sendMessageStream) | ✅ (Flow 真正流式) | ✅ (SSE) | ✅ (SSE) |
| 流式降级 fallback | ❌ | ✅ (非流式兜底) | ❌ | ❌ |
| 阶段摘要生成 | ✅ (AI 自动生成) | ✅ (advanceStage 触发) | ✅ (服务端) | ✅ (服务端) |
| 工作记忆限制 | ✅ (20条) | ✅ (20条) | ✅ | ✅ (20条) |
| 数据驱逐/压缩 | ✅ (7天压缩/30天合并) | ✅ (7天压缩/30天合并) | ❌ | ❌ |
| 免责声明弹窗 | ✅ (含年龄确认) | ✅ (含年龄确认) | ✅ (含年龄确认) | ✅ (简化版) |
| 知识库联动 | ✅ (VaultFocusState) | ✅ (VaultFocusStore) | ❌ | ❌ |
| 关联知识库 UI | ❌ (仅数据层) | ✅ (页面选择器) | ❌ | ❌ |
| 本地缓存 | ❌ | ❌ | ✅ (localStorage 聊天) | ✅ (SecureStore 全量) |
| Markdown 渲染 | ❌ | ❌ | ✅ | ❌ |
| 停止生成 | ❌ | ❌ | ✅ | ✅ |
| 快速提问按钮 | ❌ | ❌ | ❌ | ✅ |
| 阶段完成小结 | ❌ | ❌ | ✅ | ✅ |
| 学徒画像编辑 | ❌ | ❌ | ❌ | ✅ |
| API Key 预检 | ✅ | ✅ | ✅ | ❌ (依赖服务端) |
| AI 未配置警告 | ❌ | ❌ | ✅ | ❌ |

### 2.2 阶段定义

五个阶段在所有平台保持一致：

```
入道 → 筑基 → 精进 → 磨砺 → 出师
```

| 阶段 | 角色定位 | 核心职责 |
|------|----------|----------|
| 入道 | 引路人 | 评估基础、明确目标 |
| 筑基 | 严师 | 建立知识框架、每日任务 |
| 精进 | 匠人 | 分科细化、攻克薄弱 |
| 磨砺 | 考官 | 模拟考试、查漏补缺 |
| 出师 | 前辈 | 实战建议、报考指导 |

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

---

## 三、各平台优缺点分析

### 3.1 鸿蒙版 (ArkTS) — 已优化

**本轮优化亮点** 🆕
- ✅ 新增 `sendMessageStream()` 方法，支持流式对话（`StreamChunkCallback` 回调）
- ✅ 新增 `MasterDisclaimerDialog` 组件，含医疗/法律行业特别提示和年龄确认
- ✅ 实现 `runDataEviction()` 数据驱逐机制（7天压缩对话、30天合并到画像）
- ✅ 实现 `VaultFocusState` 知识库联动（关联/置顶知识库）

**优点**
- ✅ 纯本地存储，离线可用，无网络依赖
- ✅ 完整的数据驱逐机制（7天压缩、30天合并到画像）
- ✅ 知识库联动（VaultFocusState 关联师父与知识库）
- ✅ 完整的工作记忆系统（20条对话限制）
- ✅ 阶段摘要自动生成并持久化
- ✅ 流式对话已实现，体验提升
- ✅ 免责声明弹窗，合规
- ✅ 代码结构清晰，类型安全

**缺点**
- ❌ 直接调用 AI API，无服务端中转，Key 暴露风险
- ❌ 无流式降级 fallback（网络波动时直接报错）
- ❌ 数据仅本地存储，设备丢失无法恢复
- ❌ 无关联知识库的 UI 选择器（仅数据层支持）
- ❌ 与服务器端架构不同步，功能难以统一迭代
- ❌ 无 Markdown 渲染能力

**文件位置**
- 服务实现：`arkts/entry/src/main/ets/services/MasterService.ets`
- 数据库：`arkts/entry/src/main/ets/NoteDatabase.ets`
- 页面：`arkts/entry/src/main/ets/pages/MasterListPage.ets`、`MasterChatPage.ets`、`MasterStagePage.ets`
- 组件：`arkts/entry/src/main/ets/components/MasterDisclaimerDialog.ets`

---

### 3.2 安卓版 (Kotlin/Compose) — 已优化

**本轮优化亮点** 🆕
- ✅ `chat()` 方法返回 `Flow<String>`，真正实现流式对话
- ✅ 流式降级 fallback：流式失败时自动降级为非流式（`client.chatCompletion`）
- ✅ 新增 `MasterDisclaimerDialog` 组件，含年龄确认
- ✅ 实现 `runDataEviction()` 数据驱逐机制
- ✅ 知识库联动：`chat()` 中自动注入关联知识库内容（最多5篇笔记片段）
- ✅ `MasterStagePage` 新增关联知识库 UI 选择器（`VaultPicker`）
- ✅ `advanceStage()` 在推进阶段时自动调用 AI 生成摘要
- ✅ 行业匹配支持 `contains()` 模糊匹配（比鸿蒙版更灵活）

**优点**
- ✅ 声明式 UI，Compose 现代化
- ✅ Flow 响应式数据流，真正流式对话
- ✅ 流式 fallback 机制，容错性好
- ✅ 数据驱逐机制完善
- ✅ 知识库联动：数据层 + UI 选择器完整
- ✅ 代码组织良好（MasterPrompts、StageDefs、MasterNames 分离）
- ✅ 阶段推进时自动生成摘要

**缺点**
- ❌ 直连 AI API，Key 暴露风险
- ❌ 无 Markdown 渲染
- ❌ 无学徒画像编辑功能
- ❌ 数据仅本地存储
- ❌ 与鸿蒙版存在代码重复，维护成本高

**文件位置**
- 服务实现：`kotlin/app/src/main/java/com/lumin/huaji/android/data/master/MasterService.kt`
- 页面：`kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/`
  - `MasterListPage.kt`、`MasterChatPage.kt`、`MasterStagePage.kt`
  - `MasterDisclaimerDialog.kt`

---

### 3.3 WebUI (Blazor Server)

**本轮变化**
- ✅ AI 配置检查（`showAiConfigWarning`），未配置时提示
- ✅ localStorage 聊天历史持久化（`LoadChatHistoryAsync` / `SaveChatHistoryAsync`）
- ✅ Markdown 渲染支持（Markdig 库）

**优点**
- ✅ 真正的 SSE 流式对话
- ✅ 服务端统一管理，数据一致性好
- ✅ 免责声明弹窗
- ✅ 双栏布局，师父列表与对话同屏
- ✅ Markdown 渲染支持（代码块、列表、引用等）
- ✅ 与后端 API 版本同步，功能迭代快
- ✅ AI 配置预检，友好提示
- ✅ 本地聊天历史缓存（localStorage）
- ✅ 停止生成功能

**缺点**
- ❌ 需要服务端运行，无法独立运行
- ❌ 无数据驱逐机制
- ❌ 无知识库联动
- ❌ 无学徒画像编辑（仅查看）
- ❌ 依赖浏览器，移动端体验受限
- ❌ 无快速提问按钮
- ❌ 无阶段完成小结 UI

**文件位置**
- 页面：`services/WebUI.Family/Pages/MasterChat.razor`、`MasterStage.razor`、`MasterDisclaimerDialog.razor`
- API：`services/WebUI.Family/Services/ApiService.cs`

---

### 3.4 花圃 (MAUI) — 最新实现

**本轮优化亮点** 🆕
- ✅ `MasterCacheService`：本地缓存对话历史（200条/师父）、画像、师父列表
- ✅ `MasterProfilePage`：学徒画像查看/编辑页面（基础、学习风格、优势、薄弱）
- ✅ 快速提问按钮："我该如何开始学习？"、"请评估一下我的水平"、"制定一个学习计划"
- ✅ 阶段提示：进入新阶段时显示该阶段说明
- ✅ 流式输出带光标闪烁效果
- ✅ `MasterService` 集成缓存读写，API 调用优先使用缓存
- ✅ API 新增 `PutJsonAsync` 支持画像更新
- ✅ 删除师父时自动清除缓存
- ✅ 停止生成功能
- ✅ 加载状态、错误重试、网络异常提示

**优点**
- ✅ 基于服务端 API，数据与 WebUI 一致
- ✅ SSE 流式对话
- ✅ 本地缓存机制（SecureStore，三要素：对话/画像/列表）
- ✅ 免责声明弹窗
- ✅ 移动端原生体验
- ✅ 学徒画像可编辑
- ✅ 利用 BaihuaSdk 的签名通信，安全可靠
- ✅ 跨平台（Android/iOS）
- ✅ 完善的错误处理和加载状态
- ✅ 快速提问按钮，降低使用门槛
- ✅ 事件驱动更新（OnProfileUpdated / OnMastersUpdated）

**缺点**
- ❌ 依赖服务端，离线不可用
- ❌ 无数据驱逐机制（依赖服务端）
- ❌ 无知识库联动
- ❌ 无 Markdown 渲染（服务端返回 Markdown 无法正确展示）
- ❌ 无流式降级 fallback
- ❌ 对话历史仅本地缓存，不与服务端同步回写
- ❌ 阶段进度页面缺少中文本地化

**文件位置**
- 数据模型：`clients/MobileApp.Maui/Services/MasterModels.cs`
- 服务层：`clients/MobileApp.Maui/Services/MasterService.cs`
- 缓存服务：`clients/MobileApp.Maui/Services/MasterCacheService.cs`
- 页面：`clients/MobileApp.Maui/Pages/`
  - `MasterListPage.razor`、`MasterChatPage.razor`
  - `MasterStagePage.razor`、`MasterProfilePage.razor`

---

## 四、跨平台差距与优化建议

### 4.1 当前差距矩阵

| 维度 | 最强 | 中等 | 最弱 |
|------|------|------|------|
| 流式对话 | 安卓（真正流式+fallback） | 鸿蒙、WebUI、花圃 | — |
| 数据驱逐 | 鸿蒙、安卓 | — | WebUI、花圃 |
| 知识库联动 | 安卓（数据+UI完整） | 鸿蒙（仅数据层） | WebUI、花圃 |
| 免责声明 | 鸿蒙、安卓、WebUI | 花圃（简化版） | — |
| 本地缓存 | 花圃（全量缓存） | WebUI（仅聊天） | 鸿蒙、安卓（仅 SQLite） |
| 画像管理 | 花圃（可编辑） | 鸿蒙、安卓（仅系统生成） | WebUI（只读） |
| UI 体验 | 安卓（Material3 Compose） | 花圃（MAUI Blazor） | 鸿蒙、WebUI |
| 离线可用 | 鸿蒙、安卓（纯本地） | 花圃（缓存优先） | WebUI（必须在线） |
| Markdown | WebUI | — | 鸿蒙、安卓、花圃 |
| 容错性 | 安卓（fallback） | 花圃（错误提示） | 鸿蒙、WebUI |

### 4.2 各平台待优化项

#### 🔴 鸿蒙版
1. **添加流式降级 fallback** — `sendMessageStream` 失败时降级为 `sendMessage`
2. **添加知识库 UI 选择器** — 参考安卓 `MasterStagePage` 的 `VaultPicker`
3. **添加 Markdown 渲染** — AI 返回内容包含 Markdown 格式
4. **Key 安全处理** — 考虑将 AI Key 迁移至服务端

#### 🔴 安卓版
1. **添加 Markdown 渲染** — 使用 Markdown Compose 库
2. **添加学徒画像编辑** — 参考花圃 `MasterProfilePage`
3. **添加快速提问按钮** — 降低新用户使用门槛
4. **统一 Prompt 管理** — 与服务端 Prompt 对齐，避免漂移

#### 🟡 WebUI
1. **添加数据驱逐机制** — 服务端实现，供所有平台复用
2. **添加知识库联动** — 关联 Vault 内容到对话上下文
3. **添加学徒画像编辑** — 参考花圃实现
4. **添加快速提问按钮** — 花圃已验证可行
5. **添加阶段完成小结 UI** — 进度页展示摘要

#### 🟡 花圃
1. **添加 Markdown 渲染** — 服务端 AI 返回 Markdown，需正确展示
2. **添加知识库联动** — 对接服务端 VaultFocus API
3. **添加流式降级 fallback** — 网络异常时降级为非流式
4. **对话历史双向同步** — 本地缓存回写服务端
5. **阶段页面中文化** — 当前 `MasterStagePage` 大量英文
6. **添加数据驱逐机制** — 调用服务端 API 清理旧数据
7. **免责声明增强** — 参考鸿蒙/安卓的详细声明格式

### 4.3 跨平台统一建议

| 优先级 | 项目 | 说明 |
|--------|------|------|
| **P0** | 统一 Markdown 渲染 | 所有平台 AI 回复包含 Markdown，需一致渲染 |
| **P0** | 统一数据驱逐 | 服务端 `TaskRunner.Family` 实现数据驱逐 API，各平台调用 |
| **P1** | 统一知识库联动 | 服务端 VaultFocus API，四端统一 UI 模式 |
| **P1** | 统一 Prompt 管理 | `Core.Shared` 管理所有 Prompt，客户端仅传参 |
| **P2** | 统一画像编辑 | 四端支持查看/编辑学徒画像 |
| **P2** | 统一流式 fallback | 网络异常时降级为非流式模式 |
| **P3** | 统一快速提问 | 新用户引导，降低冷启动门槛 |
| **P3** | 统一声明文案 | 各端免责声明内容保持一致 |

### 4.4 推荐架构演进

```
TaskRunner.Contracts (共享 DTO)
    └── Master/ (MasterDto, ApprenticeProfileDto, StageSummaryDto)

Core.Shared (服务端共享)
    └── MasterEngine (核心业务逻辑 + Prompt 管理)
    └── VaultFocusService (知识库联动)

TaskRunner.Family (API 服务)
    └── MasterController (REST API + SSE + 数据驱逐端点)

BaihuaSdk (移动端 SDK)
    └── MasterService (IMasterService 接口 + HttpTransport)

统一能力矩阵：
┌─────────────┬────────┬────────┬───────┬──────┐
│ 能力        │ 鸿蒙   │ 安卓   │ WebUI │ 花圃 │
├─────────────┼────────┼────────┼───────┼──────┤
│ 流式对话    │ ✅     │ ✅+fb  │ ✅    │ ✅    │
│ 数据驱逐    │ ✅     │ ✅     │ ❌    │ ❌    │
│ 知识库联动  │ 数据层 │ 完整   │ ❌    │ ❌    │
│ Markdown    │ ❌     │ ❌     │ ✅    │ ❌    │
│ 画像编辑    │ ❌     │ ❌     │ ❌    │ ✅    │
│ 本地缓存    │ ❌     │ ❌     │ 聊天  │ 全量  │
│ 离线可用    │ ✅     │ ✅     │ ❌    │ 部分  │
└─────────────┴────────┴────────┴───────┴──────┘
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

## 六、总结

### 本轮优化成果

| 平台 | 关键改进 |
|------|----------|
| 鸿蒙 | 流式对话 ✅、免责声明 ✅、数据驱逐 ✅、知识库联动 ✅ |
| 安卓 | 真正流式+fallback ✅、免责声明 ✅、数据驱逐 ✅、知识库UI ✅ |
| WebUI | AI配置检查 ✅、localStorage缓存 ✅、Markdown ✅ |
| 花圃 | 本地缓存 ✅、画像编辑 ✅、快速提问 ✅、停止生成 ✅ |

### 仍需优化（按优先级）

**P0 — 核心体验**
1. **Markdown 渲染**：所有平台 AI 回复均为 Markdown 格式，当前仅 WebUI 支持
2. **数据驱逐**：WebUI 和花圃缺少数据清理机制

**P1 — 功能完善**
3. **知识库联动**：WebUI 和花圃尚不支持 VaultFocus
4. **流式 fallback**：鸿蒙、WebUI、花圃需添加降级机制

**P2 — 体验增强**
5. **学徒画像编辑**：鸿蒙/安卓/WebUI 需添加
6. **快速提问按钮**：鸿蒙/安卓/WebUI 需添加

**P3 — 一致性**
7. 统一免责声明文案
8. 统一 Prompt 管理（服务端集中维护）

### 下一步建议
1. **先统一 Markdown 渲染** — 四端使用同一 Markdown 库
2. **服务端实现数据驱逐 API** — 供 WebUI 和花圃调用
3. **对接 VaultFocus API** — 四端一致的知识库联动体验
4. **统一 Prompt 到 Core.Shared** — 避免各端 Prompt 漂移
5. **真机验证** — 所有变更部署到设备测试