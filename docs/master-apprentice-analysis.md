# 拜师系统（Master-Apprentice）跨平台对比分析

> 生成日期：2026-07-27
> 分析范围：鸿蒙版 (ArkTS)、安卓版 (Kotlin/Compose)、WebUI (Blazor)、花圃 (MAUI)

---

## 一、系统架构概览

| 平台 | 技术栈 | 数据存储 | API 通信 |
|------|--------|----------|----------|
| 鸿蒙版 | ArkTS | 本地 SQLite | 直连 DeepSeek API |
| 安卓版 | Kotlin + Jetpack Compose | 本地 SQLite | 直连 DeepSeek API |
| WebUI | Blazor Server | 服务端 SQLite | REST API + SSE |
| 花圃 | MAUI (Blazor Hybrid) | 服务端 SQLite | REST API + SSE (通过 BaihuaSdk) |

---

## 二、功能对比

### 2.1 核心功能矩阵

| 功能 | 鸿蒙版 | 安卓版 | WebUI | 花圃 |
|------|--------|--------|-------|------|
| 师父列表管理 | ✅ | ✅ | ✅ | ✅ |
| 行业-师父映射 | ✅ | ✅ | ✅ | ✅ |
| 5阶段修炼体系 | ✅ | ✅ | ✅ | ✅ |
| 学徒画像 | ✅ | ✅ | ✅ | ✅ |
| 流式对话 | ❌ | ❌ | ✅ | ✅ |
| 阶段摘要生成 | ✅ | ✅ | ✅ | ✅ |
| 工作记忆限制 | ✅ (20条) | ✅ (20条) | ✅ | ✅ |
| 数据驱逐/压缩 | ✅ | ✅ | ❌ | ❌ |
| 免责声明弹窗 | ❌ | ❌ | ✅ | ✅ |
| 知识库联动 | ✅ (VaultFocus) | ❌ | ❌ | ❌ |

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

### 3.1 鸿蒙版 (ArkTS)

**优点**
- ✅ 纯本地存储，离线可用，无网络依赖
- ✅ 实现数据驱逐机制（7天压缩、30天合并到画像）
- ✅ 知识库联动（VaultFocusState 关联师父与知识库）
- ✅ 完整的工作记忆系统（20条对话限制）
- ✅ 阶段摘要自动生成并持久化
- ✅ 代码结构清晰，类型安全

**缺点**
- ❌ 无流式对话，体验较差
- ❌ 直接调用 AI API，无服务端中转
- ❌ 无免责声明机制
- ❌ 数据仅本地存储，设备丢失无法恢复
- ❌ 与服务器端架构不同步，功能难以统一迭代

**文件位置**
- 服务实现：`arkts/entry/src/main/ets/services/MasterService.ets`
- 数据库：`arkts/entry/src/main/ets/NoteDatabase.ets`
- 页面：`arkts/entry/src/main/ets/pages/MasterListPage.ets`、`MasterChatPage.ets`、`MasterStagePage.ets`

---

### 3.2 安卓版 (Kotlin/Compose)

**优点**
- ✅ 声明式 UI，Compose 现代化
- ✅ Flow 响应式数据流
- ✅ 实现 `chat()` 返回 `Flow<String>`，为流式提供基础
- ✅ 数据驱逐机制类似鸿蒙版
- ✅ 代码组织良好（MasterPrompts、StageDefs、MasterNames 分离）

**缺点**
- ❌ `chat()` 实际一次性返回，非真正流式
- ❌ 无免责声明
- ❌ 无知识库联动
- ❌ 直连 AI API，无服务端中转
- ❌ 与鸿蒙版存在代码重复，维护成本高

**文件位置**
- 服务实现：`kotlin/app/src/main/java/com/lumin/huaji/android/data/master/MasterService.kt`
- 页面：`kotlin/app/src/main/java/com/lumin/huaji/android/ui/master/`

---

### 3.3 WebUI (Blazor Server)

**优点**
- ✅ 真正的 SSE 流式对话
- ✅ 服务端统一管理，数据一致性好
- ✅ 免责声明弹窗
- ✅ 双栏布局，师父列表与对话同屏
- ✅ Markdown 渲染支持
- ✅ 与后端 API 版本同步，功能迭代快

**缺点**
- ❌ 需要服务端运行，无法独立运行
- ❌ 无数据驱逐机制
- ❌ 无知识库联动
- ❌ 依赖浏览器，移动端体验受限

**文件位置**
- 页面：`services/WebUI.Family/Pages/MasterChat.razor`、`MasterStage.razor`
- API：`services/WebUI.Family/Services/ApiService.cs`

---

### 3.4 花圃 (MAUI) - 最新实现

**优点**
- ✅ 基于服务端 API，数据与 WebUI 一致
- ✅ SSE 流式对话
- ✅ 免责声明弹窗
- ✅ 移动端原生体验
- ✅ 利用 BaihuaSdk 的签名通信，安全可靠
- ✅ 跨平台（Android/iOS）

**缺点**
- ❌ 依赖服务端，离线不可用
- ❌ 无本地缓存机制
- ❌ UI 相对简洁，功能仍需完善
- ❌ 无数据驱逐机制

**文件位置**
- 数据模型：`clients/MobileApp.Maui/Services/MasterModels.cs`
- 服务层：`clients/MobileApp.Maui/Services/MasterService.cs`
- 页面：`clients/MobileApp.Maui/Pages/MasterListPage.razor`、`MasterChatPage.razor`、`MasterStagePage.razor`

---

## 四、优化建议

### 4.1 统一 API 契约 ✅ 已完成
- 所有平台通过 `TaskRunner.Contracts.Master` 共享 DTO
- 花圃已对齐 WebUI 的 API 调用方式

### 4.2 推荐改进项

#### 短期（已在花圃实现）
- [x] 添加流式对话支持
- [x] 添加免责声明弹窗
- [x] 统一 5 阶段修炼体系
- [x] 完善页面导航流程

#### 中期（建议实施）
- [ ] 为鸿蒙/安卓版添加流式对话（需 AI 客户端支持）
- [ ] 为鸿蒙/安卓版添加免责声明
- [ ] 引入 API 网关，统一 AI 调用入口
- [ ] 添加本地缓存层（花圃）

#### 长期（架构优化）
- [ ] 建立数据同步机制（本地 ↔ 服务端）
- [ ] 知识库联动推广到所有平台
- [ ] 实现跨平台工作记忆同步
- [ ] 统一 Prompt 管理，支持 A/B 测试

### 4.3 代码复用方案

```
TaskRunner.Contracts (共享 DTO)
    └── Master/ (MasterDto, ApprenticeProfileDto, StageSummaryDto)

BaihuaSdk (移动端 SDK)
    └── MasterService (IMasterService 接口)

Core.Shared (服务端共享)
    └── MasterEngine (核心业务逻辑)

TaskRunner.Family (API 服务)
    └── MasterController (REST API + SSE)
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

### 花圃吸收的优点
1. ✅ 行业-师父映射（来自鸿蒙/安卓）
2. ✅ 5 阶段修炼体系（来自所有平台）
3. ✅ 流式对话（来自 WebUI）
4. ✅ 免责声明（来自 WebUI）
5. ✅ 学徒画像管理（来自鸿蒙/安卓）

### 花圃暂未实现
1. ❌ 数据驱逐机制（待服务端支持）
2. ❌ 知识库联动（待架构规划）
3. ❌ 本地缓存（待实现）

### 下一步
1. 编译验证所有变更
2. 部署到设备进行真机测试
3. 根据测试结果迭代优化
