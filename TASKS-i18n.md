# 国际化（i18n）任务计划

## 目标

为 Baihua 全项目添加中英文语言切换支持，默认中文。

## 方案

**标准 .NET 方式：`.resx` + `IStringLocalizer<T>`**

- 中文作为默认语言（`zh-CN`）
- 英文作为备选（`.resx` 默认文化，fallback）
- Blazor Server 原生支持，零额外依赖

## 工作量估算

当前有 ~366 个文件含中文，~96,000 个中文字符。分阶段进行：

---

### Phase 1：基础设施（1-2 次 session）

1. **新建 `services/Baihua.Web/Localization/` 目录**，包含：
   - `SharedResources.resx` — 英文默认
   - `SharedResources.zh-CN.resx` — 中文翻译
2. **在 `Program.cs` 配置本地化**
   ```csharp
   builder.Services.AddLocalization();
   // 设置默认文化为 zh-CN
   var supportedCultures = new[] { "zh-CN", "en" };
   ```

3. **创建 `_Imports.razor` 注入**：
   ```razor
   @using Microsoft.Extensions.Localization
   @inject IStringLocalizer<SharedResources> L
   ```

4. **语言切换组件**
   - 下拉框或按钮切换 `CultureInfo.CurrentCulture`
   - 存入 `LocalStorage`，重启保持

### Phase 2：Blazor UI 字符串提取（3-4 次 session）

每个 `.razor` 页面处理：

1. 扫描页面中的中文硬编码字符串（双引号内的中文）
2. 替换为 `@L["KeyName"]`
3. 在 `.resx` 中添加对应条目

**重点页面**（含中文最多的）：
| 文件 | 字符串数 | 优先级 |
|------|---------|--------|
| HardwareBenchmark.razor | 31 | ⭐⭐⭐ |
| MasterChat.razor | 16 | ⭐⭐⭐ |
| OpenClaw.razor | 9 | ⭐⭐ |
| MasterStage.razor | 7 | ⭐⭐ |
| LocalModels.razor | 7 | ⭐⭐ |
| KnowledgeGenerate.razor | 6 | ⭐⭐ |
| Search.razor | 6 | ⭐⭐ |

### Phase 3：服务层字符串提取（3-4 次 session）

重点文件：
- `Baihua.Core/DefaultPromptProvider.cs` — AI Prompt 模板（大量中文）
- `Baihua.Core/CapabilityService.cs` — 功能描述
- `Baihua.Core/AiMetricsService.cs` — 指标描述
- `Baihua.Core/Security/` — 安全相关提示
- `Baihua.Family/` 各 Service 中的日志/错误信息

### Phase 4：修复预存编码损坏（1 次 session）

同时处理以下文件的中文乱码问题：
- `Baihua.Contracts/Benchmark/BenchmarkPrompts.cs` — 模型描述
- `Baihua.Contracts/LocalModels/ModelDatabase.cs` — 模型列表
- `Baihua.Contracts/Metrics/ServiceMetrics.cs` — 指标中文描述
- `Baihua.Vault/` 多个文件的 `_logger.Log*` 中文日志

### Phase 5：注释中文化（可选）

代码中的中文注释（`// 中文注释`）不变——它们不是运行时字符串，不影响国际化。

## 技术细节

### .resx 文件结构
```xml
<!-- SharedResources.resx (English, default) -->
<data name="SearchPlaceholder" xml:space="preserve">
    <value>Search notes...</value>
</data>

<!-- SharedResources.zh-CN.resx (Chinese) -->
<data name="SearchPlaceholder" xml:space="preserve">
    <value>搜索笔记...</value>
</data>
```

### 在 Razor 中使用
```razor
@inject IStringLocalizer<SharedResources> L
<input placeholder="@L["SearchPlaceholder"]" />
<h3>@L["PageTitle_Search"]</h3>
```

### 语言切换
```csharp
// CultureService.cs
public async Task SetCultureAsync(string culture)
{
    var preferredCulture = new CultureInfo(culture);
    CultureInfo.DefaultThreadCurrentCulture = preferredCulture;
    CultureInfo.DefaultThreadCurrentUICulture = preferredCulture;
    await _jsRuntime.InvokeVoidAsync("blazorCulture.set", culture);
    NavigationManager.NavigateTo(NavigationManager.Uri, forceLoad: true);
}
```

## 注意事项

- 不修改 `.cs` 文件中的 XML 注释（`/// <summary>`），它们只是开发文档
- 不修改 `_logger.Log*` 中的日志字符串——日志文字在运营中固定，翻译反而增加复杂度
- `ErrorMessage` 类的消息保留中文，仅对用户可见的 UI 字符串做国际化
- 保持 `bh.ps1` 等脚本中的中文输出不变（仅内部开发工具）
