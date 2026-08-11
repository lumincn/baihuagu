# 百花 WebUI 审计发现 — C 组（管理与工具，14 页）

- 审计时间：2026-08-11 13:22–14:05（GMT+8）
- 环境：http://127.0.0.1:5177（Blazor Server），Edge headless 1440x900，zh-CN，cli-token 登录
- 方法：14 页全量巡检（截图 + console/pageerror/失败请求采集）→ 逐页安全交互 → 未登录行为验证 → 375x812 响应式抽查 → 源码佐证（只读）
- 严重度：P0=功能不可用/数据错误/崩溃；P1=明显 bug/流程断；P2=体验差/易用性；P3=视觉/文案细节
- 截图均在 `shots/`。涉及真实 AI 推理/删除/清理的按钮一律未点击（标注「未执行」）。

---

## /settings（AI 配置）

- [P3] 导航与页面标题不一致：侧栏叫「AI 设置」，页面 H1 为「🤖 AI 配置」
  - 位置：`/settings`，侧栏导航 vs `<h1>`
  - 现象：同一条目两种叫法
  - 期望：统一命名
  - 截图：C_settings.png

- [P3] 「删除 AI 提供商」确认弹窗用浏览器原生 confirm（样式突兀）
  - 位置：`/settings` 提供方列表「删除」按钮
  - 现象：实测弹 `确定要删除 'DeepSeek (官方)' 吗？`（原生 JS confirm，与全站自定义 modal 风格不一致；同页 LocalModels 用的是自定义弹窗）
  - 期望：统一为站内 modal
  - 复现：点任意提供方「删除」→ 出现原生 confirm
  - 截图：C_settings_after_del_cancel.png（弹窗已自动取消，数据未动）

- [P3] 添加/编辑提供方为整页切换编辑视图（无弹窗），保存前无「未保存离开」提示
  - 位置：`/settings` →「+ 添加AI提供商」/「编辑」
  - 现象：点编辑后整页替换为表单，直接点侧栏可无确认离开，已填内容丢失
  - 期望：离开前提示或弹窗化
  - 截图：C_settings_add_provider.png、C_settings_edit_provider.png

- 好的点：API Key 列表只显示掩码（sk-572...67c1）；编辑表单可回显掩码；预设下拉（Ollama/OpenAI API/硅基流动/自定义）+ 引导文案齐全。全局生成详细度（简洁/适中/详细）按钮组正常。

## /local-models（本地模型部署）

- [P1] OpenVINO「已下载模型」表的「删除」按钮**无任何确认直接删除模型文件**
  - 位置：`/local-models` →「🧿 OpenVINO」tab →「📦 已下载模型」→ 每行「删除」（约 7.8GB 的 Qwen2.5-14B 等）
  - 现象：源码 `@onclick='() => DeleteModelAsync(m.Path)'`（第 2142 行）直接调删除 API，无 confirm；而「📊 概览」tab 的下载模型删除有自定义确认弹窗（`showDeleteConfirm`）。同一危险操作两种待遇
  - 期望：补确认弹窗（含模型名/大小/不可恢复警告，复用现有 `LocalModels_DeleteConfirm*` 文案）
  - 复现：OpenVINO tab → 点「删除」
  - 截图：C_local-models_openvino_tab.png
  - ⚠️ 未执行真实删除

- [P2] 页面首屏「刷新中...」占位 10~20 秒且无任何进度/超时提示
  - 位置：`/local-models` 首次打开（概览/Ollama tab）
  - 现象：进入后概览内容被 `isLoading` 骨架屏「刷新中...」替换（实测 10s、20s 两次采样，10s 时仍在转；约 15~20s 后才显示硬件环境/模型推荐）。期间无 spinner 动画以外的任何说明，后台在并行扫描 Ollama/LM Studio/llama.cpp（未安装的端点超时拖慢整体）
  - 期望：分块加载（先显示已就绪的 OpenVINO/推荐内容）、给超时错误提示或「跳过不可达提供方」；至少显示「正在检测 Ollama…」这类具体文案
  - 截图：C_local-models_overview_20s.png（加载完成后）、C_local-models.png（加载中）
  - 附带：加载完成后 Ollama/LM Studio/llama.cpp 三个 tab 内容几乎相同（共享同一批 GGUF 模型，`共 2 个模型，总大小约 7.4 GB`），信息重复度高

- [P3] 375px 视口有 2px 横向溢出（scrollWidth 377 > 375）
  - 位置：`/local-models` 模型表格
  - 期望：表格容器加 `overflow-x:auto` 或缩减内边距
  - 截图：C_resp375__local-models.png

- 好的点：概览 tab 的删除有自定义确认弹窗（含模型名/大小/「不可恢复」警告）；OpenVINO 托管服务异常有明确错误文案（「宿主机 8866 未运行？」）；下载任务区/目录说明齐全。

## /openclaw（OpenClaw 任务委派）

- [P3] 页面初始加载（任务列表+本地 AI 配置）耗时 10 秒以上，期间「刷新」按钮一直禁用且无加载提示
  - 位置：`/openclaw` 进入页面
  - 现象：实测 9s 时「刷新」仍 disabled（`disabled="@_isLoading"`，初始后台加载未完成）；约 12s+ 才可点。虽有每 5 秒自动刷新兜底，但用户首次进入看到「刷新」灰着会以为功能坏了
  - 期望：初始加载加 spinner/文案，或加载完再渲染按钮
  - 截图：C_openclaw.png

- 好的点：任务失败有「查看错误」弹窗（错误详情 + 复制 + 关闭，弹窗可关闭）；字符计数实时更新（输入 17 字符显示「17 字符」）；发送按钮随内容启用/禁用；5s 自动轮询。环境问题（openclaw 二进制缺失 `No such file or directory`）属于部署问题，非 UI 缺陷。

## /prompt-templates（提示词模板）

- [P3] 删除模板的确认弹窗同样为原生 JS confirm，且无「模板名/不可恢复」等上下文（与 LocalModels 自定义弹窗不一致）
  - 位置：`/prompt-templates` 模板列表「删除」
  - 现象：实测弹 `确定要删除模板"中医"吗？`（原生 confirm）
  - 期望：统一站内弹窗；「通用」内置模板无删除按钮（正确），其余模板删除前可补充「自定义模板删除后不可恢复」提示
  - 截图：C_prompt-templates_after_del_cancel.png

- 好的点：新建模板进入编辑视图；「默认分类」下拉、行业标签（编程语言/算法/系统设计…）预设齐全；编辑/删除按钮层级清晰。

## /model-benchmark（模型评测）

- [P3] 标题中英混杂 + 与导航命名不一致
  - 位置：`/model-benchmark` 浏览器标题「模型 benchmark」、H1「📊 模型 benchmark」；侧栏导航叫「模型评测」
  - 现象：同一功能两处叫法，且页面标题夹英文
  - 期望：统一「模型评测」；页面内「笔记大模型/编程大模型」tab 正常
  - 截图：C_model-benchmark.png

- [P3] 提供方/模型选择用「chips 模式」增强下拉：占位项「-- 请选择 --」也渲染为一个可点 chip
  - 位置：`/model-benchmark` 测试配置区（及 /code-agent、/image-recognition 同款）
  - 现象：chips 第一项是「-- 请选择 --」，点击等于清空选择；易误触，且视觉上像是一个真实选项
  - 期望：占位项渲染为灰色不可点占位文本而非 chip
  - 截图：C_model-benchmark_chip_selected.png

- 好的点：点「DeepSeek (官方)」chip 后模型级联正常（deepseek-v4-pro / deepseek-v4-flash 出现）；排行榜/历史记录有「暂无数据」空态；开始测试按钮存在（⚠️ 未执行，会触发真实评测）。

## /hardware-benchmark（硬件评测）

- [P3] CPU 名称显示「Unknown X64 CPU」
  - 位置：`/hardware-benchmark`「📋 本机配置」CPU 行
  - 现象：Windows 上报 CPU 名称为 Unknown 时原样展示，用户看到一串英文占位
  - 期望：识别到 Unknown 时降级为「未知 CPU（x64）」或取处理器家族名
  - 截图：C_hardware-benchmark_refreshed.png

- 好的点：本机配置（核心数/内存/显卡/系统/磁盘）、算力对比表（按 Llama3-8B Q4 估速排序）、性能等级说明、适用场景建议结构完整；「刷新硬件信息」可用（实测点击正常）。

## /image-recognition（图片识别）

- [P2] 「启动服务」后无进度/端口/停止入口（交互链路不完整）
  - 位置：`/image-recognition` 顶部「⚠️ 本地视觉服务未运行 ▶️ 启动服务」
  - 现象：页面只提供「启动」按钮；无「启动中...」状态、无日志/端口信息、无「停止服务」按钮；启动是重操作（拉起 OpenVINO 服务进程），用户误点后只能去别处关
  - 期望：启动中态 + 启动后显示服务地址/日志 + 停止按钮
  - 截图：C_image-recognition_full.png
  - ⚠️ 启动按钮未执行（重操作）

- 好的点：文件格式/大小限制说明（png/jpg/jpeg/webp/bmp，最大 20MB）；3B/7B 模型切换（chips）实测正常；「提问（可选）」有默认提示词占位。

## /ai-drawing（AI 绘图）

- [P2] 历史记录缩略图加载失败（ORB 拦截，裂图）
  - 位置：`/ai-drawing` →「🕘 历史记录」列表（实测 1 条 `a cute panda eating bamboo...` 08-05）
  - 现象：请求 `GET http://127.0.0.1:8788/api/comfy/file?filename=baihua_art_00001_.png&subfolder=` 报 `net::ERR_BLOCKED_BY_ORB`（跨端口 5177→8788，响应被浏览器按 opaque response blocking 拦截），缩略图无法显示
  - 期望：ComfyUI 文件端点返回合法图片类型/CORS 头，或改为后端代理图片；至少给「图片加载失败」占位
  - 复现：打开 /ai-drawing 即有失败请求（见审计 issues.failedRequests）
  - 截图：C_ai-drawing_redetect.png（历史区可见裂图占位）

- 好的点：ComfyUI 未运行有醒目警告 + 「重新检测」按钮（实测可用，检测后仍提示未运行，反馈一致）；风格/尺寸/步数选择齐全；「风格会追加到提示词末尾」有说明。

## /stock-advisor（股票 AI 建议）

- 未发现明显缺陷。进入页面自动加载上次分析结果（缓存，10 只推荐股票含评分/理由表格）体验好；「强制刷新」明确标注；行业下拉 60+ 项走可搜索下拉（搜索框实测存在）；持仓评估区、AI 原始输出折叠区结构清晰。
- ⚠️ 「开始分析」未执行（会拉行情+AI 分析）。
- 截图：C_stock-advisor_full.png、C_resp375__stock-advisor.png（375px 无溢出 ✓）

## /qr-tool（二维码工具）

- [P1] 首次进入页面自动生成的二维码全部失败（空白二维码框）
  - 位置：`/qr-tool` 顶部「🏠 服务器配对码」「🤖 主 AI API Key」
  - 现象：进入页面即报 2 条 console error：`Compact QRCode generation failed: TypeError: a.appendChild is not a function`（qrcode.min.js 构造器内）。根因：Blazor Server ElementReference 竞态——`OnAfterRenderAsync` 里取数据后 `await Task.Yield()` 立即调 JS，但二维码容器在 `@if (_serverQRCode != null)` 条件块里尚未渲染，JS 收到的是未解析的引用对象（`{__internalId}`）而非 DOM 元素 → 库构造器 `a.appendChild` 崩溃 → 用户看到 180x180 空白框
  - 期望：JS 调用前确保容器已渲染（先 `StateHasChanged` + 等渲染完成，或容器常驻渲染、或改传 id 用 `getElementById`）
  - 复现：直接打开 /qr-tool 即可见（console 有报错、二维码区空白）；点「🔄 刷新」后恢复正常（元素已存在）——说明是时序问题
  - 截图：C_qr-tool.png（空白）、C_qr-tool_after_refresh.png（刷新后正常）

- [P1] 「通用二维码」首次点「生成二维码」必失败，需点第二次才成功
  - 位置：`/qr-tool` →「📝 通用二维码」卡片
  - 现象：输入内容点「生成二维码」→ 结果区显示红色「生成二维码失败」（同一竞态：`_showQR=true` 后结果容器才渲染，JS 调用时仍拿不到元素）；再点一次（容器已存在）才成功
  - 期望：同上；且失败时应自动重试一次而不是直接显示失败
  - 复现：输入文本 → 点「生成二维码」（第一次）→ 见失败提示
  - 截图：C_qr-tool_general.png

- 好的点：服务器配对码含名称内联编辑（✏️，校验「名称不能为空」）、ServerId/地址展示、自动授权开关（失败回滚）；API Key 二维码卡片折叠交互正常。

## /code-agent（编程 Agent）

- [P2] 切换到「OpenVINO (本地)」后，模型默认仍选中云端模型 `deepseek-v4-flash`，无「本地/云端」区分提示
  - 位置：`/code-agent`「AI 提供方/模型」选择区
  - 现象：提供方 chips 点「OpenVINO (本地)」→ 模型 chips 出现 `提供方默认模型 / deepseek-v4-flash / qwen2.5-7b-instruct-int4-ov`，且选中态落在 `deepseek-v4-flash`（云端 DeepSeek 模型被配置进了本地提供方的模型列表，选中态跟随切换前的 `_model` 值残留）。用户以为在用本地模型，实际可能请求云端付费 API
  - 期望：模型列表按提供方过滤（本地提供方只列本地模型）；切换提供方时重置模型为「提供方默认模型」；给本地/云端模型加徽标或分组
  - 复现：进入 /code-agent → 点 chips「OpenVINO (本地)」→ 观察模型选中项
  - 截图：C_code-agent_ov_selected.png

- 好的点：语言/技术栈下拉、生成中流式输出（spinner + 停止按钮）、复制/下载输出、文件名校验完整。⚠️ 「生成代码」未执行（真实推理）。

## /log-errors（错误日志）

- [P1] 整页本地化资源键缺失，标题/按钮/状态/确认弹窗全部显示原始键名（页面基本不可用）
  - 位置：`/log-errors` 全页
  - 现象：浏览器标题 `LogErrors_PageTitle`；H4 `LogErrors_Title`；四个按钮 `LogErrors_RefreshLocal / LogErrors_OpenObserveErrors / LogErrors_ClearLocal / LogErrors_ClearOpenObserve`；状态行 `LogErrors_StatusLocal`、`LogErrors_CurrentSourceLogErrors_SourceLocalFiles`（两个键无空格拼接）；点「清理本地日志」的确认弹窗内容也是原始键 `LogErrors_ConfirmClearLocal`。根因：`LogErrors_*` 键在 `SharedResources.resx` 与 `SharedResources.zh-CN.resx`（1387 个键）中均不存在，IStringLocalizer 回退返回键名
  - 期望：补充全部 `LogErrors_*` 中英文资源键；建议加一条「页面资源键缺失」的自动化检查
  - 复现：打开 /log-errors 即见
  - 截图：C_log-errors.png、C_log-errors_after_clear_cancel.png（弹窗键名）
  - ⚠️ 清理日志未执行（弹窗已自动取消）

- 好的点：日志条目本身渲染完整（时间/服务徽标/级别徽标/消息，截断合理）；清空操作用 confirm 兜底（键补上即可）。

## /log-settings（日志配置）

- [P3] 密码框占位符是整句说明（`通过环境变量 OPENOBSERVE_ROOT_PASSWORD 配置`），输入时占位被圆点覆盖、半截显示
  - 位置：`/log-settings`「密码」输入框（type=password）
  - 现象：占位文本过长，聚焦输入后无任何说明，用户不知该填什么
  - 期望：占位符简短（如「留空则用环境变量」），完整说明放 label 或下方 small 提示
  - 截图：C_log-settings_full.png

- 好的点：用户名/密码/Web UI 地址/浏览器访问地址四字段齐全；「打开 OpenObserve」外链 + Docker 部署命令说明完整；默认值来自环境变量有兜底。

## /login（授权登录页）— C 组未登录行为验证

- [通过] 未登录访问受保护页正确跳转 `/login?returnUrl=%2Fsettings`、`/login?returnUrl=%2Flocal-models`（returnUrl 编码正确）
  - 截图：C_unauth__settings.png、C_unauth__local-models.png

- [P3] 登录页仅提供「本机命令行一键授权」一条路径，未提及移动端扫码配对入口
  - 位置：`/login` 全页
  - 现象：页面只有 `./bh dashboard` 命令 + 「授权基于操作系统用户权限」提示；远程/手机访问者没有其它入口（系统其实有 /qr-tool 配对码 + 自动授权功能）
  - 期望：加「移动端扫码配对」入口或说明（引用配对码功能）；Windows 下命令提示可写 `bh dashboard`（`./bh` 前缀在 cmd 中无效）
  - 截图：C_login_unauth.png

- 好的点：文案友好（「需要授权访问」「💡 提示」），无账号/密码体系故无找回需求，安全模型（OS 用户权限）说明清晰。

## 全站一致性（C 组涉及）

- [P3] 侧栏导航空格风格不一：「AI数字助理」（无空格）vs「AI 对话/AI 绘图/AI 设置」；「股票AI建议」vs 页面标题「股票 AI 建议」
  - 位置：左侧导航栏
  - 期望：统一 `AI xxx` 风格
  - 截图：任一 C_*.png 侧栏可见

- [P3] 危险操作确认方式三套并存：原生 confirm（Settings 删提供方、PromptTemplates 删模板、LogErrors 清日志）、自定义 modal（LocalModels 概览删模型）、无确认（LocalModels OpenVINO 删模型）——建议统一为自定义 modal 并给 LocalModels OpenVINO 补上

---

## 本组最值得修的 Top 5

1. **[P1] /log-errors 整页原始本地化键**（标题/按钮/状态/确认弹窗全是 `LogErrors_*` 键名）— 页面形同不可用，需补 resx 键；顺手加资源键缺失检查。截图：C_log-errors.png
2. **[P1] /qr-tool 二维码生成时序竞态** — 首次进入两个自动二维码空白、通用二维码首次生成必失败（ElementReference 未渲染即调 JS），需在 JS 调用前保证容器已渲染或失败自动重试。截图：C_qr-tool.png / C_qr-tool_general.png
3. **[P1] /local-models OpenVINO「已下载模型」删除无确认** — 一键删 7.8GB 模型文件无任何确认，与概览 tab 有确认弹窗不一致，必须补确认（建议统一 modal）。截图：C_local-models_openvino_tab.png
4. **[P2] /code-agent 本地提供方默认选中云端模型 deepseek-v4-flash** — 用户以为用本地模型实际可能走云端付费 API，需按提供方过滤模型列表 + 切换时重置 + 本地/云端徽标。截图：C_code-agent_ov_selected.png
5. **[P2] /local-models 首屏「刷新中...」10~20 秒无进度提示** — 骨架屏长时间无解释，后台扫描不可达提供方拖慢整体；应分块渲染 + 具体文案 + 超时跳过。截图：C_local-models_overview_20s.png

---

### 审计覆盖统计
- 页面：14/14（均有截图，每页 ≥1 张，共 50+ 张）
- 发现总数：21 条（P1×4、P2×4、P3×13，另含多条「通过/好点」备注）
- 交互执行情况：真实 AI 推理（生成代码/开始测试/股票分析/识别图片）、删除模型、清理日志等重操作一律未执行，相关确认弹窗均自动取消；仅执行安全交互（打开弹窗、切换 tab/开关、表单输入、刷新按钮、级联选择等）
