# 拜师系统（Master-Apprentice）跨平台对比分析

> 生成日期：2026-07-29（第七轮 review 更新）
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
| 学徒画像 | ✅ (**可编辑**) | ✅ (**可编辑**) 🆕 | ✅ (**可编辑**) | ✅ (**可编辑**) |
| 流式对话 | ✅ (sendMessageStream) | ✅ (Flow 真正流式) | ✅ (SSE) | ✅ (SSE) |
| 流式降级 fallback | ✅ | ✅ (非流式兜底) | ✅ (SSE+fallback) | ✅ |
| 阶段摘要生成 | ✅ (AI 自动生成) | ✅ (advanceStage 触发) | ✅ (服务端) | ✅ (服务端) |
| 工作记忆限制 | ✅ (20条) | ✅ (20条) | ✅ | ✅ (20条) |
| 数据驱逐/压缩 | ✅ (7天/30天) | ✅ (7天/30天) | ✅ (服务端API) | ✅ (服务端API) |
| 免责声明弹窗 | ✅ (含年龄确认) | ✅ (含年龄确认) | ✅ (含年龄确认) | ✅ (简化版) |
| 知识库联动 | ✅ (数据+UI+**Prompt注入**) | ✅ (数据+UI+Prompt注入) | ✅ (服务端API) | ✅ (数据+UI+**Prompt注入**) 🆕 |
| Markdown 渲染 | ✅ (SimpleMarkdown) | ✅ (WebView) | ✅ (Markdig) | ✅ (MarkdownView) |
| 停止生成 | ✅ 🆕 | ✅ | ✅ | ✅ |
| 快速提问按钮 | ✅ 🆕 | ✅ | ✅ | ✅ |
| 阶段完成小结 UI | ❌ | ❌ | ✅ | ✅ |
| 学徒画像编辑 | ✅ | ✅ 🆕 | ✅ | ✅ |
| 本地缓存 | ❌ | ❌ | ✅ (localStorage 聊天) | ✅ (SecureStore 全量) |
| API Key 预检 | ✅ | ✅ | ✅ | ✅ 🆕 |
| AI 未配置警告 | ❌ | ❌ | ✅ | ✅ 🆕 |
| 对话历史同步 | ❌ (仅本地) | ❌ (仅本地) | ✅ (服务端) | ✅ (双向同步) 🆕 |

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

### 3.1 鸿蒙版 (ArkTS) — 第六轮优化

**本轮优化亮点** 🆕
- ✅ **停止生成功能** — `cancelGeneration()` 方法可中断正在进行的流式对话（[MasterService.ets#L502-L508](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/services/MasterService.ets#L502-L508)），UI 端"停止"按钮（[MasterChatPage.ets#L297-L304](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterChatPage.ets#L297-L304)）
- ✅ **快速提问按钮** — 新增 `QUICK_QUESTIONS` 数组，空状态时显示引导问题按钮（[MasterChatPage.ets#L18-L23](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterChatPage.ets#L18-L23)、[MasterChatPage.ets#L259-L279](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterChatPage.ets#L259-L279)）
- ✅ **知识库内容注入 Prompt** — `getVaultContentForPrompt()` 将关联知识库的笔记内容摘要注入到 AI 系统提示中（[MasterService.ets#L564-L588](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/services/MasterService.ets#L564-L588)）
- ✅ **停止生成 UI 完整** — `stopGeneration()` 方法 + 停止按钮 + 状态管理（[MasterChatPage.ets#L194-L198](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterChatPage.ets#L194-L198)）

**优点**
- ✅ 纯本地存储，离线可用，无网络依赖
- ✅ 完整的数据驱逐机制（7天压缩对话、30天合并到画像）
- ✅ 知识库联动最完整：数据层 + UI 选择器 + **Prompt 注入**（对话中真正利用知识库内容）
- ✅ 完整的工作记忆系统（20条对话限制）
- ✅ 阶段摘要自动生成并持久化
- ✅ 流式对话 + fallback，容错性提升
- ✅ Markdown 渲染支持（SimpleMarkdown）
- ✅ 免责声明弹窗，合规
- ✅ 学徒画像可编辑
- ✅ **停止生成**功能（本轮新增）
- ✅ **快速提问按钮**（本轮新增）
- ✅ 代码结构清晰，类型安全

**缺点**
- ❌ 直接调用 AI API，无服务端中转，Key 暴露风险
- ❌ 数据仅本地存储，设备丢失无法恢复
- ❌ 无阶段完成小结 UI
- ❌ 无 API Key 预检（虽然有 Key 配置检查，但不是预检）

**文件位置**
- 服务实现：[MasterService.ets](file:///c:/Users/lumin/.trae-cn/worktrees/arkts/ka-le-ma-Lm70Lk/entry/src/main/ets/services/MasterService.ets)
- 页面：[MasterListPage.ets](file:///c:/Users/lumin/.trae-cn/worktrees/arkts/ka-le-ma-Lm70Lk/entry/src/main/ets/pages/MasterListPage.ets)、[MasterChatPage.ets](file:///c:/Users/lumin/.trae-cn/worktrees/arkts/ka-le-ma-Lm70Lk/entry/src/main/ets/pages/MasterChatPage.ets)、[MasterProfilePage.ets](file:///c:/Users/lumin/.trae-cn/worktrees/arkts/ka-le-ma-Lm70Lk/entry/src/main/ets/pages/MasterProfilePage.ets)、[MasterStagePage.ets](file:///c:/Users/lumin/.trae-cn/worktrees/arkts/ka-le-ma-Lm70Lk/entry/src/main/ets/pages/MasterStagePage.ets)
- 组件：`SimpleMarkdown.ets`、`MasterDisclaimerDialog.ets`

---

### 3.2 安卓版 (Kotlin/Compose) — 第六轮优化

**本轮优化亮点** 🆕
- ✅ **学徒画像编辑** — `MasterProfilePage.kt` 完整实现查看/编辑双模式，支持修改基础、学习风格、优势、薄弱环节（[MasterProfilePage.kt#L26-L184](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterProfilePage.kt#L26-L184)）
- ✅ **知识库关联 UI** — `MasterStagePage.kt` 新增 vault focus 卡片 + 选择器对话框，可关联/更换/取消关联知识库（[MasterStagePage.kt#L166-L234](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterStagePage.kt#L166-L234)）

**优点**
- ✅ 声明式 UI，Compose 现代化
- ✅ Flow 响应式数据流，真正流式对话 + fallback
- ✅ 知识库联动最完整：数据层 + UI 选择器 + Prompt 注入（对话中真正利用知识库内容）
- ✅ Markdown 渲染支持（WebView）
- ✅ 代码组织良好（MasterPrompts、StageDefs、MasterNames 分离）
- ✅ 阶段推进时自动生成摘要
- ✅ 数据驱逐机制完善，自动触发
- ✅ 快速提问按钮降低使用门槛
- ✅ 停止生成功能，用户体验提升
- ✅ 免责声明弹窗
- ✅ **学徒画像可编辑**（本轮新增）
- ✅ **知识库关联 UI**（本轮新增）

**缺点**
- ❌ 直连 AI API，Key 暴露风险
- ❌ 数据仅本地存储
- ❌ 与鸿蒙版存在代码重复，维护成本高
- ❌ 无阶段完成小结 UI

**文件位置**
- 服务实现：[MasterService.kt](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/data/master/MasterService.kt)
- Prompt 定义：`MasterPrompts`（同文件内）
- 数据存储：`VaultFocusStore.kt`
- 页面：[MasterChatPage.kt](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterChatPage.kt)、[MasterStagePage.kt](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterStagePage.kt)、[MasterProfilePage.kt](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterProfilePage.kt)

---

### 3.3 WebUI (Blazor Server) — 第四轮优化

**本轮优化亮点** 🆕
- ✅ **数据驱逐** — 调用服务端 `POST /api/master/evict-all` 批量清理旧数据
- ✅ **画像编辑** — `MasterProfile` 页面支持编辑学徒画像并保存
- ✅ **知识库联动** — 获取/设置 VaultFocus 关联，支持解绑
- ✅ **快速提问按钮** — 空状态显示预设引导问题
- ✅ **流式 fallback** — SSE 失败后降级到非流式对话

**优点**
- ✅ 真正的 SSE 流式对话
- ✅ 服务端统一管理，数据一致性好
- ✅ Markdown 渲染支持（Markdig 库）
- ✅ 双栏布局，师父列表与对话同屏
- ✅ 停止生成功能
- ✅ AI 配置预检，友好提示
- ✅ 本地聊天历史缓存（localStorage）
- ✅ 免责声明弹窗
- ✅ 删除师父功能
- ✅ 阶段完成小结 UI
- ✅ 数据驱逐、画像编辑、知识库联动（服务端 API 全覆盖）

**缺点**
- ❌ 需要服务端运行，无法独立运行
- ❌ 依赖浏览器，移动端体验受限

**文件位置**
- 页面：[MasterChat.razor](file:///c:/Users/lumin/src/baihuagu/services/WebUI.Family/Pages/MasterChat.razor)、`MasterStage.razor`
- API：`WebUI.Services/ApiService.cs`

---

### 3.4 花圃 (MAUI) — 第七轮优化

**本轮优化亮点** 🆕
- ✅ **对话历史双向同步** — 新增 `GetConversationsFromServerAsync`（拉取服务端历史）和 `SyncConversationsToServerAsync`（推送本地对话到服务端），MasterChatPage 优先从服务端加载，失败降级本地缓存
- ✅ **后端 API 扩展** — `MasterController` 新增 `GET /api/master/{id}/conversations`（获取对话历史，支持 limit 参数）和 `POST /api/master/{id}/conversations/sync`（批量同步对话），新增 `ConversationHistoryItem`、`ConversationHistoryResponse`、`ConversationSyncRequest`、`ConversationSyncResponse` DTO
- ✅ **API Key 预检** — `CheckAiConfiguredAsync` 方法检查 `/api/ai/providers`，MasterChatPage 进入时自动检测
- ✅ **AI 未配置警告** — 新增警告横幅提示用户配置 AI，`CanSend` 智能禁用发送按钮
- ✅ **知识库联动完整实现** — MasterStagePage 新增 VaultFocus UI，支持查看/添加/移除关联知识库，与后端 `BuildVaultContextAsync` Prompt 注入形成完整链路；HttpTransport 新增 `DeleteJsonAsync` 方法

**上一轮（第四轮）优化成果**
- ✅ **免责声明弹窗** — 进入聊天时展示，医疗/法律行业额外提示
- ✅ **缓存持久化** — 免责声明接受状态通过 SecureStore 持久化
- ✅ **Markdown 渲染** — `MarkdownView` 组件完整支持
- ✅ **流式 fallback** — `TryCollectFallbackAsync` + `FallbackNonStreamChatAsync` 两级降级
- ✅ **快速提问按钮** — 空状态时显示 3 个预设问题
- ✅ **画像编辑完整** — `MasterProfilePage` 查看/编辑双模式切换
- ✅ **阶段页面优化** — `MasterStagePage` 含阶段小结卡片、完成按钮、进度条

**优点**
- ✅ 基于服务端 API，数据与 WebUI 一致
- ✅ SSE 流式对话 + fallback
- ✅ 本地缓存机制（SecureStore 三要素：对话/画像/列表，200条/师父）
- ✅ **对话历史双向同步**（本轮新增，四端唯一）
- ✅ **API Key 预检 + AI 未配置警告**（本轮新增，四端唯一）
- ✅ **知识库联动完整**（本轮新增，四端唯一：数据+UI+Prompt注入）
- ✅ 学徒画像可编辑（四端唯一）
- ✅ 快速提问按钮
- ✅ 停止生成功能
- ✅ Markdown 渲染
- ✅ 完整的错误处理和加载状态
- ✅ 利用 BaihuaSdk 的签名通信，安全可靠
- ✅ 跨平台（Android/iOS）
- ✅ 事件驱动更新（OnProfileUpdated / OnMastersUpdated）
- ✅ 免责声明弹窗（合规）

**缺点**
- ❌ 依赖服务端，离线不可用
- ❌ 鸿蒙/安卓本地存储的数据无法直接与花圃互通（架构差异）

**文件位置**
- 数据模型：[MasterModels.cs](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Services/MasterModels.cs)
- 服务层：[MasterService.cs](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Services/MasterService.cs)
- 缓存服务：[MasterCacheService.cs](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Services/MasterCacheService.cs)
- 后端 API：[MasterController.cs](file:///c:/Users/lumin/src/baihuagu/services/TaskRunner.Family/Controllers/AI/MasterController.cs)、[VaultFocusController.cs](file:///c:/Users/lumin/src/baihuagu/services/TaskRunner.Family/Controllers/AI/VaultFocusController.cs)
- 契约 DTO：[MasterDtos.cs](file:///c:/Users/lumin/src/baihuagu/services/TaskRunner.Contracts/Master/MasterDtos.cs)
- SDK 传输：[HttpTransport.cs](file:///c:/Users/lumin/src/baihuagu/libs/BaihuaSdk/src/Transport/HttpTransport.cs)
- 页面：[MasterChatPage.razor](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Pages/MasterChatPage.razor)、[MasterProfilePage.razor](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Pages/MasterProfilePage.razor)、[MasterStagePage.razor](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Pages/MasterStagePage.razor)

---

## 四、跨平台差距与优化建议

### 4.1 当前差距矩阵

| 维度 | 最强 | 中等 | 最弱 |
|------|------|------|------|
| 流式对话 | 安卓（Flow+fallback+Prompt注入） | 鸿蒙、花圃（流式+fallback） | WebUI（SSE+fallback） |
| Markdown 渲染 | 全平台已覆盖 ✅ | — | — |
| 数据驱逐 | 全平台已覆盖 ✅ | — | — |
| 知识库联动 | 鸿蒙、安卓、花圃（数据+UI+Prompt注入） | WebUI（服务端API） | — |
| 画像管理 | 全平台可编辑 ✅ | — | — |
| 快速提问 | 全平台已覆盖 ✅ | — | — |
| 停止生成 | 全平台已覆盖 ✅ | — | — |
| 本地缓存 | 花圃（全量SecureStore） | WebUI（localStorage聊天） | 鸿蒙、安卓（仅SQLite） |
| 离线可用 | 鸿蒙、安卓（纯本地） | 花圃（缓存优先） | WebUI（必须在线） |
| 容错性 | 安卓（fallback+Prompt注入） | 鸿蒙、花圃、WebUI（fallback） | — |
| 免责声明 | 全平台已覆盖 ✅ | — | — |
| 阶段完成小结 UI | WebUI、花圃 | — | 鸿蒙、安卓 |
| 对话历史同步 | 花圃（双向同步） | WebUI（服务端） | 鸿蒙、安卓（仅本地） |

### 4.2 各平台待优化项

#### 🔴 鸿蒙版
1. **知识库内容注入 Prompt** — ✅ 已实现（本轮新增）
2. **停止生成功能** — ✅ 已实现（本轮新增）
3. **快速提问按钮** — ✅ 已实现（本轮新增）
4. **添加阶段完成小结 UI** — 完成阶段后展示师父寄语
5. **Key 安全处理** — 考虑将 AI Key 迁移至服务端

#### 🔴 安卓版
1. **添加学徒画像编辑** — ✅ 已实现（本轮新增）
2. **添加知识库关联 UI** — ✅ 已实现（本轮新增）
3. **添加阶段完成小结 UI** — 完成阶段后展示师父寄语
4. **Key 安全处理** — 同鸿蒙

#### ✅ WebUI（本轮已优化）
1. ~~数据驱逐机制~~ ✅ 已实现
2. ~~知识库联动~~ ✅ 已实现
3. ~~快速提问按钮~~ ✅ 已实现
4. ~~学徒画像编辑~~ ✅ 已实现
5. ~~流式 fallback~~ ✅ 已实现
6. **对话历史持久化** — 当前仅 localStorage，可考虑服务端同步

#### 🔴 花圃（本轮已优化）
1. ~~**对话历史双向同步**~~ ✅ 已实现（本轮新增）
2. ~~**添加 API Key 预检**~~ ✅ 已实现（本轮新增）
3. **知识库 Prompt 注入** — 关联知识库后在服务端对话中利用知识库内容（需后端 VaultFocus 数据联动）
4. **添加知识库关联 UI** — 类似安卓版 MasterStagePage，支持选择/关联/解绑知识库
5. **增强离线模式** — 当前缓存优先加载，需增加网络断开检测与自动恢复

### 4.3 跨平台统一建议

| 优先级 | 项目 | 说明 |
|--------|------|------|
| **P0** | 服务端知识库联动 API | 统一 `VaultFocus` 接口，四端一致的关联/注入体验 |
| **P1** | 知识库 Prompt 注入 | 花圃需实现 VaultFocus 数据联动，在对话中注入知识库内容 |
| **P1** | 鸿蒙/安卓 Key 安全 | 考虑将 AI Key 迁移至服务端中转 |
| **P2** | 统一 Prompt 管理 | `Core.Shared` 集中管理所有 Prompt 模板，客户端仅传参，避免漂移 |
| **P2** | 统一快速提问 | 四端一致的新用户引导，降低冷启动门槛 |
| **P3** | 统一阶段完成小结 UI | 鸿蒙/安卓需添加，花圃/WebUI 已有 |
| **P3** | 统一声明文案 | 各端免责声明内容保持一致 |
| **P3** | 知识库关联 UI | 花圃需添加，参考安卓版实现 |

### 4.4 推荐架构演进

```
TaskRunner.Contracts (共享 DTO)
    └── Master/ (MasterDto, ApprenticeProfileDto, StageSummaryDto, VaultFocusDto)

Core.Shared (服务端共享)
    └── MasterEngine (核心业务逻辑 + Prompt 管理 + 数据驱逐)
    └── VaultFocusService (知识库联动 + Prompt 注入)

TaskRunner.Family (API 服务)
    └── MasterController (REST API + SSE + 数据驱逐端点)

BaihuaSdk (移动端 SDK)
    └── MasterService (IMasterService 接口 + HttpTransport)

统一能力矩阵（当前 → 目标）：
┌─────────────────┬──────────────┬──────────────┬──────────────┬──────────────┐
│ 能力            │ 鸿蒙         │ 安卓         │ WebUI        │ 花圃         │
├─────────────────┼──────────────┼──────────────┼──────────────┼──────────────┤
│ 流式对话        │ ✅+fb        │ ✅+fb+注入   │ ✅           │ ✅+fb        │
│ 数据驱逐        │ ✅           │ ✅           │ ❌ → 服务端  │ ❌ → 服务端  │
│ 知识库联动      │ ✅ 数据+UI   │ ✅ 完整      │ ❌ → API     │ ❌ → API     │
│ Markdown        │ ✅           │ ✅           │ ✅           │ ✅           │
│ 画像编辑        │ ❌ → 花圃    │ ❌ → 花圃    │ ❌ → 花圃    │ ✅           │
│ 快速提问        │ ❌ → 花圃    │ ✅           │ ❌ → 花圃    │ ✅           │
│ 停止生成        │ ❌ → 添加    │ ❌ → 添加    │ ✅           │ ✅           │
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

## 六、总结

### 七轮优化成果演进

| 轮次 | 鸿蒙 | 安卓 | WebUI | 花圃 |
|------|------|------|-------|------|
| 第一轮 | 流式对话 ✅<br>免责声明 ✅<br>数据驱逐 ✅ | 真正流式 ✅<br>免责声明 ✅<br>数据驱逐 ✅ | AI 配置检查 ✅<br>localStorage ✅ | 本地缓存 ✅<br>画像编辑 ✅<br>快速提问 ✅ |
| 第二轮 | 流式 fallback ✅<br>Markdown ✅<br>知识库 UI ✅ | 知识库联动 ✅<br>Prompt 注入 ✅ | — | — |
| 第三轮 | Markdown ✅<br>知识库 UI ✅<br>fallback ✅ | Markdown ✅<br>快速提问 ✅<br>Prompt 注入 ✅ | 删除师父 ✅ | Markdown ✅<br>fallback ✅<br>快速提问 ✅<br>页面优化 ✅ |
| 第四轮 | — | — | 数据驱逐 ✅<br>画像编辑 ✅<br>知识库联动 ✅<br>快速提问 ✅<br>fallback ✅ | 免责声明 ✅<br>缓存持久化 ✅ |
| 第五轮 | 画像编辑 ✅<br>知识库 CRUD ✅ | 停止生成 ✅<br>快速提问 ✅<br>fallback ✅<br>自动驱逐 ✅ | — | — |
| **第六轮** | 停止生成 ✅<br>快速提问 ✅<br>知识库 Prompt 注入 ✅ | 画像编辑 ✅<br>知识库关联 UI ✅ | — | — |
| **第七轮** | — | — | — | 对话历史双向同步 ✅<br>API Key 预检 ✅<br>AI 未配置警告 ✅<br>知识库联动完整 ✅ |

### 已基本解决的问题 ✅

| 问题 | 解决情况 |
|------|----------|
| Markdown 渲染 | ✅ 四端全覆盖 |
| 流式 fallback | ✅ 全平台已实现 |
| 知识库联动 | ✅ **全平台覆盖**（鸿蒙/安卓/花圃含数据+UI+Prompt注入，WebUI 服务端 API） |
| 免责声明 | ✅ 四端全覆盖 |
| 学徒画像编辑 | ✅ **四端全覆盖** |
| 快速提问 | ✅ **四端全覆盖** |
| 停止生成 | ✅ **四端全覆盖** |
| 本地缓存 | ✅ WebUI（聊天）、花圃（全量） |
| 数据驱逐 | ✅ 全平台覆盖 |
| 知识库 Prompt 注入 | ✅ **全平台覆盖**（鸿蒙/安卓/花圃均已实现） |
| 对话历史双向同步 | ✅ **花圃已实现**（本轮新增，四端唯一） |
| API Key 预检 | ✅ **花圃已实现**（本轮新增，四端唯一） |
| AI 未配置警告 | ✅ 花圃、WebUI 已实现 |
| 知识库关联 UI | ✅ **花圃已实现**（本轮新增，参考安卓版） |

### 仍需优化（按优先级）

**P0 — 核心缺失（影响所有平台用户体验）**
1. **鸿蒙/安卓 阶段完成小结 UI** — 完成阶段后无师父寄语

**P1 — 功能补强（影响单平台体验）**
2. **鸿蒙/安卓 Key 安全** — 迁移至服务端
3. **WebUI 对话历史持久化** — 当前仅 localStorage
4. **增强离线模式** — 花圃需增加网络断开检测与自动恢复

**P2 — 架构改进**
5. **统一 Prompt 管理** — `Core.Shared` 集中维护
6. **统一声明文案**

### 下一步建议

1. **鸿蒙/安卓 阶段完成小结 UI** — 完成阶段后展示师父寄语
2. **架构统一** — Prompt 管理迁移到 `Core.Shared`，四端引用同一模板
3. **Key 安全迁移** — 鸿蒙/安卓将 AI Key 迁移至服务端
4. **WebUI 对话历史持久化** — 考虑服务端同步
5. **真机验证** — 所有变更部署到设备测试