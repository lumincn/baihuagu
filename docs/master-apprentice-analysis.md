# 拜师系统（Master-Apprentice）跨平台对比分析

> 生成日期：2026-07-27（第三轮 review 更新）
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
| 学徒画像 | ✅ (只读) | ✅ (只读) | ✅ (只读) | ✅ (**可编辑**) |
| 流式对话 | ✅ (sendMessageStream) | ✅ (Flow 真正流式) | ✅ (SSE) | ✅ (SSE) |
| 流式降级 fallback | ✅ 🆕 | ✅ (非流式兜底) | ✅ (SSE 内置) | ✅ 🆕 |
| 阶段摘要生成 | ✅ (AI 自动生成) | ✅ (advanceStage 触发) | ✅ (服务端) | ✅ (服务端) |
| 工作记忆限制 | ✅ (20条) | ✅ (20条) | ✅ | ✅ (20条) |
| 数据驱逐/压缩 | ✅ (7天/30天) | ✅ (7天/30天) | ✅ (服务端API) 🆕 | ✅ (服务端API) 🆕 |
| 免责声明弹窗 | ✅ (含年龄确认) | ✅ (含年龄确认) | ✅ (含年龄确认) | ✅ (简化版) 🆕 |
| 知识库联动 | ✅ (数据+UI) | ✅ (数据+UI+注入Prompt) | ✅ (服务端API) 🆕 | ✅ (服务端API) 🆕 |
| Markdown 渲染 | ✅ 🆕 (SimpleMarkdown) | ✅ 🆕 (WebView) | ✅ (Markdig) | ✅ 🆕 (MarkdownView) |
| 停止生成 | ❌ | ❌ | ✅ | ✅ |
| 快速提问按钮 | ❌ | ✅ 🆕 | ❌ | ✅ 🆕 |
| 阶段完成小结 UI | ❌ | ❌ | ✅ | ✅ |
| 学徒画像编辑 | ❌ | ❌ | ❌ | ✅ |
| 本地缓存 | ❌ | ❌ | ✅ (localStorage 聊天) | ✅ (SecureStore 全量) |
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

### 3.1 鸿蒙版 (ArkTS) — 第三轮优化

**本轮优化亮点** 🆕
- ✅ **流式 fallback** — `sendMessageStream` 中实现降级逻辑，流式失败自动切换非流式 `chatCompletion`（[MasterService.ets#L463-L477](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/services/MasterService.ets#L463-L477)）
- ✅ **Markdown 渲染** — 引入 `SimpleMarkdown` 组件渲染 AI 回复（[MasterChatPage.ets#L282-L285](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterChatPage.ets#L282-L285)）
- ✅ **知识库 UI 选择器** — `MasterStagePage` 新增完整的 VaultPicker 对话框（[MasterStagePage.ets#L334-L398](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterStagePage.ets#L334-L398)）
- ✅ **知识库关联逻辑** — `selectVault()` 实现关联/取消关联知识库的完整流程（[MasterStagePage.ets#L97-L116](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterStagePage.ets#L97-L116)）
- ✅ **画像编辑入口** — `MasterChatPage` 顶部新增👤按钮跳转画像页（[MasterChatPage.ets#L209-L214](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterChatPage.ets#L209-L214)）

**优点**
- ✅ 纯本地存储，离线可用，无网络依赖
- ✅ 完整的数据驱逐机制（7天压缩对话、30天合并到画像）
- ✅ 知识库联动：数据层 + UI 选择器完整
- ✅ 完整的工作记忆系统（20条对话限制）
- ✅ 阶段摘要自动生成并持久化
- ✅ 流式对话 + fallback，容错性提升
- ✅ Markdown 渲染支持
- ✅ 免责声明弹窗，合规
- ✅ 代码结构清晰，类型安全

**缺点**
- ❌ 直接调用 AI API，无服务端中转，Key 暴露风险
- ❌ 无停止生成功能
- ❌ 无学徒画像编辑（仅只读查看）
- ❌ 无快速提问按钮
- ❌ 数据仅本地存储，设备丢失无法恢复
- ❌ 知识库关联内容未注入到 AI Prompt（仅数据层关联，未在对话中利用）

**文件位置**
- 服务实现：[MasterService.ets](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/services/MasterService.ets)
- 页面：[MasterListPage.ets](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterListPage.ets)、[MasterChatPage.ets](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterChatPage.ets)、[MasterStagePage.ets](file:///c:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/MasterStagePage.ets)
- 组件：`SimpleMarkdown.ets`、`MasterDisclaimerDialog.ets`

---

### 3.2 安卓版 (Kotlin/Compose) — 第三轮优化

**本轮优化亮点** 🆕
- ✅ **Markdown 渲染** — `MarkdownRenderer.toHtml()` 转换 + `AndroidView` WebView 渲染（[MasterChatPage.kt#L234-L248](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterChatPage.kt#L234-L248)）
- ✅ **快速提问按钮** — 空状态时显示 4 个预设引导问题（[MasterChatPage.kt#L169-L188](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterChatPage.kt#L169-L188)）
- ✅ **知识库内容注入 Prompt** — `chat()` 方法中自动获取关联知识库的前 5 篇笔记片段注入到 system prompt（[MasterService.kt#L167-L176](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/data/master/MasterService.kt#L167-L176)）
- ✅ **出师按钮** — 最后阶段显示绿色"出师！"按钮（[MasterStagePage.kt#L261-L275](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterStagePage.kt#L261-L275)）
- ✅ **阶段进度可视化** — `MasterStagePage` 进度条 + 节点指示

**优点**
- ✅ 声明式 UI，Compose 现代化
- ✅ Flow 响应式数据流，真正流式对话 + fallback
- ✅ 知识库联动最完整：数据层 + UI 选择器 + **Prompt 注入**（对话中真正利用知识库内容）
- ✅ Markdown 渲染支持
- ✅ 代码组织良好（MasterPrompts、StageDefs、MasterNames 分离）
- ✅ 阶段推进时自动生成摘要
- ✅ 数据驱逐机制完善
- ✅ 快速提问按钮降低使用门槛

**缺点**
- ❌ 直连 AI API，Key 暴露风险
- ❌ 无停止生成功能
- ❌ 无学徒画像编辑
- ❌ 数据仅本地存储
- ❌ 与鸿蒙版存在代码重复，维护成本高

**文件位置**
- 服务实现：[MasterService.kt](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/data/master/MasterService.kt)
- Prompt 定义：`MasterPrompts`（同文件内）
- 数据存储：`VaultFocusStore.kt`
- 页面：[MasterChatPage.kt](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterChatPage.kt)、[MasterStagePage.kt](file:///c:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/MasterStagePage.kt)

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

### 3.4 花圃 (MAUI) — 第四轮优化

**本轮优化亮点** 🆕
- ✅ **免责声明弹窗** — 进入聊天时展示，医疗/法律行业额外提示（[MasterChatPage.razor#L9-L33](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Pages/MasterChatPage.razor#L9-L33)）
- ✅ **缓存持久化** — 免责声明接受状态通过 SecureStore 持久化（[MasterCacheService.cs#L135-L146](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Services/MasterCacheService.cs#L135-L146)）
- ✅ **Markdown 渲染** — `MarkdownView` 组件完整支持
- ✅ **流式 fallback** — `TryCollectFallbackAsync` + `FallbackNonStreamChatAsync` 两级降级
- ✅ **快速提问按钮** — 空状态时显示 3 个预设问题
- ✅ **画像编辑完整** — `MasterProfilePage` 查看/编辑双模式切换
- ✅ **阶段页面优化** — `MasterStagePage` 含阶段小结卡片、完成按钮、进度条

**优点**
- ✅ 基于服务端 API，数据与 WebUI 一致
- ✅ SSE 流式对话 + fallback
- ✅ 本地缓存机制（SecureStore 三要素：对话/画像/列表，200条/师父）
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
- ❌ 对话历史仅本地缓存，不与服务端同步回写
- ❌ 无 API Key 预检（依赖服务端配置）

**文件位置**
- 数据模型：`Services/MasterModels.cs`
- 服务层：[MasterService.cs](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Services/MasterService.cs)
- 缓存服务：[MasterCacheService.cs](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Services/MasterCacheService.cs)
- 页面：[MasterChatPage.razor](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Pages/MasterChatPage.razor)、[MasterProfilePage.razor](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Pages/MasterProfilePage.razor)、[MasterStagePage.razor](file:///c:/Users/lumin/src/baihuagu/clients/MobileApp.Maui/Pages/MasterStagePage.razor)

---

## 四、跨平台差距与优化建议

### 4.1 当前差距矩阵

| 维度 | 最强 | 中等 | 最弱 |
|------|------|------|------|
| 流式对话 | 安卓（Flow+fallback+Prompt注入） | 鸿蒙、花圃（流式+fallback） | WebUI（SSE+fallback） |
| Markdown 渲染 | 全平台已覆盖 ✅ | — | — |
| 数据驱逐 | 鸿蒙、安卓（本地）、WebUI/花圃（服务端） | — | — |
| 知识库联动 | 安卓（数据+UI+Prompt注入） | 鸿蒙（数据+UI）、WebUI/花圃（服务端API） | — |
| 画像管理 | 花圃（可编辑）、WebUI（可编辑） | 鸿蒙、安卓（只读） | — |
| 快速提问 | 安卓、花圃、WebUI | — | 鸿蒙 |
| 停止生成 | WebUI、花圃 | — | 鸿蒙、安卓 |
| 本地缓存 | 花圃（全量SecureStore） | WebUI（localStorage聊天） | 鸿蒙、安卓（仅SQLite） |
| 离线可用 | 鸿蒙、安卓（纯本地） | 花圃（缓存优先） | WebUI（必须在线） |
| 容错性 | 安卓（fallback+Prompt注入） | 鸿蒙、花圃、WebUI（fallback） | — |
| 免责声明 | 全平台已覆盖 ✅ | — | — |

### 4.2 各平台待优化项

#### 🔴 鸿蒙版
1. **添加停止生成功能** — 支持中断正在进行的流式对话
2. **添加学徒画像编辑** — 参考花圃 `MasterProfilePage` 实现
3. **添加快速提问按钮** — 空状态引导新用户
4. **知识库内容注入 Prompt** — 关联知识库后在对话中实际利用知识库内容（参考安卓 `MasterService.kt#L167-L176`）
5. **Key 安全处理** — 考虑将 AI Key 迁移至服务端

#### 🔴 安卓版
1. **添加停止生成功能** — 支持中断正在进行的流式对话
2. **添加学徒画像编辑** — 参考花圃 `MasterProfilePage`
3. **Key 安全处理** — 同鸿蒙

#### ✅ WebUI（本轮已优化）
1. ~~数据驱逐机制~~ ✅ 已实现
2. ~~知识库联动~~ ✅ 已实现
3. ~~快速提问按钮~~ ✅ 已实现
4. ~~学徒画像编辑~~ ✅ 已实现
5. ~~流式 fallback~~ ✅ 已实现
6. **对话历史持久化** — 当前仅 localStorage，可考虑服务端同步

#### ✅ 花圃（本轮已优化）
1. ~~免责声明弹窗~~ ✅ 已实现
2. **对话历史双向同步** — 本地缓存回写服务端
3. **添加 API Key 预检** — 虽然依赖服务端，但可检查服务端 AI 配置状态

### 4.3 跨平台统一建议

| 优先级 | 项目 | 说明 |
|--------|------|------|
| **P0** | 服务端数据驱逐 API | `TaskRunner.Family` 实现数据驱逐端点，WebUI/花圃可调用；鸿蒙/安卓本地实现已完成 |
| **P0** | 服务端知识库联动 API | 统一 `VaultFocus` 接口，四端一致的关联/注入体验 |
| **P1** | 统一 Prompt 管理 | `Core.Shared` 集中管理所有 Prompt 模板，客户端仅传参，避免漂移 |
| **P1** | 统一快速提问 | 四端一致的新用户引导，降低冷启动门槛 |
| **P2** | 统一画像编辑 | 四端支持查看/编辑学徒画像 |
| **P2** | 统一停止生成 | 鸿蒙/安卓需添加，服务端需支持中断 |
| **P3** | 统一声明文案 | 各端免责声明内容保持一致 |
| **P3** | 对话历史双向同步 | 移动端缓存与服务端同步回写 |

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

### 四轮优化成果演进

| 轮次 | 鸿蒙 | 安卓 | WebUI | 花圃 |
|------|------|------|-------|------|
| 第一轮 | 流式对话 ✅<br>免责声明 ✅<br>数据驱逐 ✅ | 真正流式 ✅<br>免责声明 ✅<br>数据驱逐 ✅ | AI 配置检查 ✅<br>localStorage ✅ | 本地缓存 ✅<br>画像编辑 ✅<br>快速提问 ✅ |
| 第二轮 | 流式 fallback ✅<br>Markdown ✅<br>知识库 UI ✅ | 知识库联动 ✅<br>Prompt 注入 ✅ | — | — |
| 第三轮 | Markdown ✅<br>知识库 UI ✅<br>fallback ✅ | Markdown ✅<br>快速提问 ✅<br>Prompt 注入 ✅ | 删除师父 ✅ | Markdown ✅<br>fallback ✅<br>快速提问 ✅<br>页面优化 ✅ |
| **第四轮** | — | — | 数据驱逐 ✅<br>画像编辑 ✅<br>知识库联动 ✅<br>快速提问 ✅<br>fallback ✅ | 免责声明 ✅<br>缓存持久化 ✅ |

### 已基本解决的问题 ✅

| 问题 | 解决情况 |
|------|----------|
| Markdown 渲染 | ✅ 四端全覆盖 |
| 流式 fallback | ✅ 全平台已实现 |
| 知识库联动 | ✅ 全平台覆盖（安卓最完整） |
| 免责声明 | ✅ 四端全覆盖 |
| 学徒画像 | ✅ 花圃/WebUI 可编辑，鸿蒙/安卓只读 |
| 快速提问 | ✅ 安卓/花圃/WebUI 已实现 |
| 停止生成 | ✅ WebUI/花圃已实现 |
| 本地缓存 | ✅ WebUI（聊天）、花圃（全量） |
| 数据驱逐 | ✅ 全平台覆盖 |
| 流式 fallback | ✅ WebUI 已补充 |

### 仍需优化（按优先级）

**P0 — 核心缺失（影响所有平台用户体验）**
1. **鸿蒙/安卓 停止生成** — 无法中断 AI 生成
2. **鸿蒙/安卓 画像编辑** — 只读模式无法让学徒修正画像

**P1 — 功能补强（影响单平台体验）**
3. **鸿蒙 快速提问** — 降低新用户门槛
4. **鸿蒙 知识库 Prompt 注入** — 关联了知识库但对话未利用
5. **对话历史双向同步** — 移动端缓存回写服务端

**P2 — 架构改进**
6. **统一 Prompt 管理** — `Core.Shared` 集中维护
7. **统一声明文案**
8. **鸿蒙/安卓 Key 安全** — 迁移至服务端
9. **WebUI 对话历史持久化** — 当前仅 localStorage

### 下一步建议

1. **鸿蒙/安卓功能对齐** — 补充停止生成、画像编辑、快速提问
2. **架构统一** — Prompt 管理迁移到 `Core.Shared`，四端引用同一模板
3. **对话历史同步** — 移动端缓存与服务端双向同步
4. **Key 安全迁移** — 鸿蒙/安卓将 AI Key 迁移至服务端
5. **真机验证** — 所有变更部署到设备测试