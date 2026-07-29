# 国际化（i18n）任务计划

## 目标

为 Baihua 全项目添加中英文语言切换支持，默认中文。

## 方案

**标准 .NET 方式：`.resx` + `IStringLocalizer<T>`**

- 中文作为默认语言（`zh-CN`）
- 英文作为备选（`.resx` 默认，fallback）
- Blazor Server 原生支持，零额外依赖

## 工作量估算

- ~366 个文件含中文，~96,000 个中文字符
- 分 5 个 Phase，约 10-15 次 session

---

## ✅ Phase 1：基础设施（已完成）

提交 `ff7c985`

- `Localization/SharedResources.resx` — 英文默认
- `Localization/SharedResources.zh-CN.resx` — 中文
- `Localization/SharedResources.cs` — 标记类
- `Services/CultureService.cs` — 语言管理（读取/切换/localStorage 持久化）
- `Program.cs` — `AddLocalization()` + `UseRequestLocalization()`，默认 `zh-CN`
- `FamilyNavMenu.razor` — 语言切换按钮（中文 / English）
- 修复了 `FamilyNavMenu.razor` 的预存编码损坏

**KeyNames 已定义：**
| Key | 中文 | English |
|-----|------|---------|
| LanguageName | 中文 | English |
| Language | 语言 | Language |
| Nav_Home | 首页 | Home |
| Nav_Search | 搜索 | Search |
| Nav_Tasks | 任务 | Tasks |
| Nav_Cards | 卡片 | Cards |
| Nav_Vaults | 知识库 | Vaults |
| Nav_Settings | 设置 | Settings |
| Nav_Login | 登录 | Login |
| Nav_Dashboard | 仪表板 | Dashboard |
| Nav_Messages | 消息 | Messages |
| Search_Placeholder | 搜索笔记... | Search notes... |
| Common_Save | 保存 | Save |
| Common_Cancel | 取消 | Cancel |
| Common_Delete | 删除 | Delete |
| Common_Confirm | 确认 | Confirm |
| Common_Loading | 加载中... | Loading... |
| Common_Error | 发生错误 | An error occurred |
| Common_Retry | 重试 | Retry |
| Common_Close | 关闭 | Close |

---

## 📋 Phase 2：Blazor UI 字符串提取（~4 session）

### 命名规则
```
<ComponentName>_<Description>
例: MasterChat_SendButton, Login_UsernameLabel
```

### 页面优先级（从高到低）

#### 🔴 高优先级（含中文字符串最多的页面）
| 文件 | 字符串数 | 内容类型 |
|------|---------|---------|
| HardwareBenchmark.razor | ~31 | 硬件检测标签、按钮、状态文字 |
| MasterChat.razor | ~16 | 聊天界面按钮、提示、Toast |
| MasterStage.razor | ~7 | 阶段标题、操作按钮 |
| OpenClaw.razor | ~9 | 配置标签、状态文字 |
| LocalModels.razor | ~7 | 模型列表、按钮文字 |

#### 🟡 中优先级
| 文件 | 字符串数 | 内容类型 |
|------|---------|---------|
| Search.razor | ~6 | 搜索提示、筛选文字 |
| KnowledgeGenerate.razor | ~6 | 生成按钮、提示文字 |
| Achievements.razor | ~5 | 成就名称、描述 |
| Settings.razor | ~4 | 设置项标签 |
| NoteDetail.razor | ~4 | 按钮文字 |
| Messages.razor | ~3 | 提示文字 |
| PromptTemplates.razor | ~3 | 模板名称 |

#### 🟢 低优先级
| 文件 | 字符串数 | 内容类型 |
|------|---------|---------|
| Tasks.razor | ~2 | 按钮文字 |
| ModelBenchmark.razor | ~2 | 标签文字 |
| DailyCard.razor | ~1 | 按钮文字 |
| Dashboard.razor | ~1 | 页面标题 |
| FamilyHome.razor | ~1 | 提示文字 |
| LogSettings.razor | ~1 | 标签文字 |
| Login.razor | ~1 | 提示文字 |

### 操作步骤（每页）
1. 打开 `.razor` 文件
2. 扫描所有中文硬编码字符串（`"中文"`）
3. 将字符串移到两个 `.resx` 文件中
4. 替换为 `@L["KeyName"]`
5. 编译确认

### 示例
```razor
@* 改前 *@
<button @onclick="Search">搜索</button>
<span>加载中...</span>

@* 改后 *@
<button @onclick="Search">@L["Search_Button"]</button>
<span>@L["Common_Loading"]</span>
```

---

## 📋 Phase 3：服务层/Shared 组件字符串提取（~3 session）

### 范围
| 文件 | 优先级 | 说明 |
|------|--------|------|
| `Shared/FamilyNavMenu.razor` | ⭐⭐⭐ | 导航菜单所有中文标签 |
| `Shared/NavMenu.razor` | ⭐⭐ | 旧版导航标签 |
| `Shared/MainLayout.razor` | ⭐⭐ | 布局中的中文文字 |
| `Shared/LoginDisplay.razor` | ⭐ | 登录显示文字 |
| `Components/Pages/*.razor` | ⭐⭐ | 组件页面中的文字 |

### 操作步骤
同 Phase 2，步骤一致。

---

## 📋 Phase 4：服务层（.cs 文件）字符串提取（~3 session）

### 范围

#### `.cs` 文件中的 UI 相关提示文字
| 文件 | 说明 | 处理方式 |
|------|------|---------|
| `Baihua.Core/CapabilityService.cs` | 功能描述字符串 | 提取到 resx |
| `Baihua.Core/AiMetricsService.cs` | 指标中文描述 | 提取到 resx |
| `Baihua.Family/Services/*.cs` | 各 Service 中的中文提示 | 提取到 resx |

#### `ErrorMessage` / 返回给前端的消息
| 文件 | 说明 | 处理方式 |
|------|------|---------|
| `Baihua.Family/Controllers/*.cs` | API 返回的错误消息 | 提取到 resx，按需翻译 |

### 注意
- `_logger.Log*` 中的日志文字**不翻译**——运营日志应固定语言
- `/// <summary>` XML 注释**不处理**——开发文档语言
- `bh.ps1` 等脚本的中文输出**不处理**——仅内部工具

### 操作方式
```csharp
// 改前
return BadRequest(new { error = "任务不存在" });

// 改后（注入 IStringLocalizer 或使用静态资源类）
return BadRequest(new { error = L["Error_TaskNotFound"] });
```

---

## 📋 Phase 5：修复预存编码损坏（~2 session）

### 损坏文件列表
| 文件 | 内容 | 损坏程度 |
|------|------|---------|
| `Baihua.Contracts/Benchmark/BenchmarkPrompts.cs` | 模型描述字符串 | 严重（~500 行损坏） |
| `Baihua.Contracts/LocalModels/ModelDatabase.cs` | 模型列表数据 | 中等 |
| `Baihua.Contracts/Metrics/ServiceMetrics.cs` | 指标名称 | 轻微 |
| `Baihua.Vault/Controllers/*.cs` | _logger.Log 中文 | 多个文件 |
| `Baihua.Vault/Program.cs` | 日志/打印文字 | 中等 |
| `Baihua.Web/Shared/FamilyNavMenu.razor` | 导航标签 | ⚡已修复 |

### 修复策略
1. 从 `.resx` 读取正确的中文翻译
2. 替换 `.cs` 文件中的损坏字符串为 resx 引用
3. 无对应 resx 条目的，根据上下文重写为正确中文
4. 日志文字直接替换为英文（不翻译）

### 预判工作量
- BenchmarkPrompts.cs — 模型描述是数据不是 UI，放入单独的数据类或 JSON 文件
- ModelDatabase.cs — 模型列表数据量大，考虑移出到 JSON 配置文件
- Vault 日志 — 约 20 处，逐个替换

---

## 🧪 验收标准

- [ ] 所有 `.razor` 页面无硬编码中文 UI 字符串
- [ ] 语言切换后整个 UI 切换语言，无需手动刷新
- [ ] 语言选择在 localStorage 中持久化，重启后保留
- [ ] `bh-webui` 编译 0 错误
- [ ] Vault / Contracts 的编码损坏全部修复
- [ ] 各 Phase 完成后运行 e2e 测试

---

## 📐 架构说明

```
services/Baihua.Web/
├── Localization/
│   ├── SharedResources.cs          # IStringLocalizer<T> 标记类
│   ├── SharedResources.resx        # 英文（默认文化）
│   └── SharedResources.zh-CN.resx  # 中文翻译
└── Services/
    └── CultureService.cs           # 语言管理服务

使用方式（Razor）：
@inject IStringLocalizer<SharedResources> L
<h3>@L["PageTitle_Search"]</h3>

使用方式（C# 代码）：
builder.Services.AddSingleton<Baihua.Web.Services.CultureService>();
// 注入 IStringLocalizer<SharedResources> 到需要翻译的服务
```

## ⚠️ 注意事项

- KeyName 使用 `PascalCase_SnakeCase` 格式：`Component_Description`
- 保持全项目 KeyName 一致性，命名前先在 `.resx` 中搜索是否已有相同 Key
- 每次一个小页面提交，方便 review
- 不要一次性改多个页面，避免冲突
