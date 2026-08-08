# OpenClaw 本地 AI 配置 - OpenVINO 集成
> 状态：✅ API 链路已打通（Contracts → Core 服务 → Family 接口 → WebUI 前端），RoundTrip 全字段验证通过。真实推理服务启动链路已按 llama.cpp 同构实现，待真实安装 OpenVINO 后做端到端验证。
> 本文档为功能设计、实现记录、问题与待完善项汇总。

---

## 1. 背景

百花 WebUI 的 OpenClaw 页面（`/openclaw`）已支持三类本地 AI Provider 的配置管理：

| Provider | 说明 | 独立配置文件 |
|----------|------|-------------|
| Ollama | 最常用的本地推理服务，自动检测端口 | 无（走 openclaw.json 主配置） |
| LM Studio | Mac/Win 主流桌面推理 GUI | 无（走 openclaw.json 主配置） |
| llama.cpp | 原生 C++ 推理，支持 CPU/GPU | `llama-config.json`（独立） |

本次新增 **OpenVINO**（Intel CPU/GPU/NPU 加速推理），架构与 llama.cpp 对齐：**独立 JSON 配置文件 + CLI `config set` 同步双写**。

---

## 2. 修改文件清单

共 7 个文件：

| 项目 | 文件 | 改动 |
|------|------|------|
| **Baihua.Contracts** | `OpenClaw/OpenClawDtos.cs` | 新增 `OpenClawOpenVinoConfigDto`；扩展 `OpenClawLocalAiConfigDto.OpenVino` 和 `SaveOpenClawLocalAiConfigRequest.OpenVino` |
| **Baihua.Family** | `Services/OpenClaw/OpenClawConfigService.cs` | 新增 OpenVINO 配置路径获取、JSON 解析、构建；修改 `SaveLocalAiConfigAsync` 保存逻辑 |
| **Baihua.Family** | `Services/OpenClaw/LocalAiConfigService.Scan.cs` | 新增 `ScanOpenVinoModelsAsync`，探测服务或从模型目录推导信息 |
| **Baihua.Family** | `Services/OpenClaw/LocalAiConfigService.Detect.cs` | 新增 `DetectAndStartOpenVinoAsync`，检测状态、组装命令启动 |
| **Baihua.Family** | `Services/OpenClaw/LocalAiConfigService.Sync.cs` | 新增 `SyncOpenVinoToOpenClawAsync`，同步到 openclaw.json providers |
| **Baihua.Family** | `Services/OpenClaw/OpenClawModelProfileService.cs` | `CollectAvailableModelsAsync` 中补齐 OpenVINO provider 模型收集（格式 `openvino/{modelId}`） |
| **Baihua.Web** | `Pages/OpenClaw.razor` + `Localization/SharedResources*.resx` | 前端新增 OpenVINO 卡片（开关、路径、端口、设备、上下文大小、ExtraArgs）；标题改为「本地 AI 配置（Ollama / LM Studio / llama.cpp / OpenVINO）」 |

---

## 3. 数据结构

### 3.1 `OpenClawOpenVinoConfigDto`（Contracts）

```csharp
public class OpenClawOpenVinoConfigDto
{
    public bool   Enabled      { get; set; }            // 是否启用
    public string BinaryPath   { get; set; } = "";      // openvino-genai-server 可执行路径
    public string ModelPath    { get; set; } = "";      // 模型目录（含 .xml/.bin/tokenizer）
    public string BaseUrl      { get; set; } = "http://localhost:8000";
    public int    Port         { get; set; } = 8000;
    public string Device       { get; set; } = "CPU";   // CPU / GPU / NPU / AUTO
    public int    ContextSize  { get; set; } = 4096;
    public string ApiType      { get; set; } = "openai-completions";
    public string ExtraArgs    { get; set; } = "";      // 透传到启动命令的附加参数
}
```

### 3.2 独立配置文件（落盘）

路径：`%USERPROFILE%\.openclaw\openvino-config.json`

```json
{
  "enabled": true,
  "binaryPath": "C:\\Users\\lumin\\scoop\\shims\\openvino-genai-server.bat",
  "modelPath": "C:\\Users\\lumin\\.openclaw\\models\\Qwen2.5-VL-7B-Instruct-int4-ov",
  "baseUrl": "http://localhost:8000",
  "port": 8000,
  "device": "NPU",
  "contextSize": 16384,
  "apiType": "openai-completions",
  "extraArgs": "--temperature 0.1"
}
```

---

## 4. 核心流程

### 4.1 保存（`POST /api/openclaw/local-ai`）

```
SaveOpenClawLocalAiConfigAsync
    │
    ├─ Ollama / LM Studio  ──► 直接 openclaw config set（无独立文件）
    │
    ├─ llama.cpp ──► ① 写 llama-config.json（必填）
    │                ② CLI config set（尽力而为，失败不影响返回）
    │
    └─ OpenVINO  ──► ① 写 openvino-config.json（必填）
                     ② CLI config set（尽力而为，失败不影响返回）
```

**关键修复（2026-08-08）**：对于有独立配置文件的 provider（llama.cpp、OpenVINO），**只要 JSON 落盘成功即返回 HTTP 200**；CLI `openclaw config set` 因 Node 版本不足等原因失败时，仅记录日志，不再误报 HTTP 400。

### 4.2 读取（`GET /api/openclaw/local-ai`）

从三份来源读取后组装为 `OpenClawLocalAiConfigDto`：
1. `openclaw.json` 主配置 → Ollama/LM Studio
2. `llama-config.json` → llama.cpp
3. `openvino-config.json` → OpenVINO（不存在则返回全部默认值 + `Enabled=false`）

### 4.3 模型扫描（`LocalAiConfigService.Scan.cs`）

`ScanOpenVinoModelsAsync` 按以下优先级：
1. 若 `BaseUrl` 可访问 → 调 `/v1/models` 获取实时模型列表
2. 否则若 `ModelPath` 目录存在 → 从目录名推导（如 `Qwen2.5-VL-7B-Instruct-int4-ov` → `qwen2.5-vl-7b-instruct-int4-ov`）
3. 都不可用 → 空列表

### 4.4 检测与启动（`LocalAiConfigService.Detect.cs`）

`DetectAndStartOpenVinoAsync`：
1. 探测端口健康（`GET {BaseUrl}/health` 或 `/v1/models`）
2. 未启动则构造命令行：`{BinaryPath} --model {ModelPath} --port {Port} --device {Device} --ctx-size {ContextSize} {ExtraArgs}`
3. 启动后等待就绪，返回 `DetectionResult { Running, Endpoint, Models, Error }`

### 4.5 同步到 OpenClaw 可用模型

`SyncOpenVinoToOpenClawAsync` 调用 `openclaw config set models.providers.openvino <json>`，将扫描到的模型写入 `openclaw.json`，于是 `OpenClawModelProfileService` 收集后即可在前端默认模型下拉框中以 `openvino/{modelId}` 格式出现。

---

## 5. 前端 UI

`OpenClaw.razor` 中在 llama.cpp 卡片下方新增 **OpenVINO 卡片**，结构与 llama.cpp 卡片完全对称：

| 控件 | 绑定字段 | 说明 |
|------|---------|------|
| 开关 | `_openVinoConfig.Enabled` | 启用/禁用 |
| 状态徽标 | 自动计算 | 绿色「已检测到服务」/ 黄色「未检测到」/ 红色「启动失败」 |
| BinaryPath 输入框 | `_openVinoConfig.BinaryPath` | openvino-genai-server.exe 或 .bat 路径 |
| ModelPath 输入框 + 浏览 | `_openVinoConfig.ModelPath` | 模型目录（选到包含 openvino_tokenizer.bin 的那一级） |
| Port 数字框 | `_openVinoConfig.Port` | 默认 8000 |
| Device 下拉 | `_openVinoConfig.Device` | CPU / GPU / NPU / AUTO |
| ContextSize 数字框 | `_openVinoConfig.ContextSize` | 默认 4096，常见取值 8192 / 16384 / 32768 |
| ExtraArgs 文本框 | `_openVinoConfig.ExtraArgs` | 附加启动参数，如 `--temperature 0.1 --top-p 0.9` |
| 「检测」按钮 | 调 `detect-openvino` API | 探测当前服务状态 |
| 「同步模型」按钮 | 调 `sync-models/openvino` API | 扫描并写入 openclaw.json |

标题已改为：**本地 AI 配置（Ollama / LM Studio / llama.cpp / OpenVINO）**。

---

## 6. 遇到的问题与修复

| # | 问题 | 现象 | 根因 | 修复 |
|---|------|------|------|------|
| 1 | **Save 返回 HTTP 400 误报** | 明明前端输入合法，保存却返回失败；但查数据库/JSON 发现数据已落盘 | `OpenClawConfigService.SaveLocalAiConfigAsync` 把 `openclaw config set` CLI 调用失败当作致命错误，而 Node 版本低或 CLI 未安装时会失败 | 改为：对有独立 JSON 的 provider（llama.cpp、OpenVINO），**JSON 写成功即返回 true**；CLI 仅 `await RunXxxAsync(...).ContinueWith(_ => { })` 静默吞异常记日志，不影响 HTTP 状态码 |
| 2 | **默认模型下拉框点不开** | 首次进入时无本地模型，AvailableModels 为空数组，`<select>` 无选项 → 视觉上像「点不开」 | `OpenClawModelProfileService.CollectAvailableModelsAsync` 最初只收集本地 provider（Ollama/LM/llama），漏了云端（硅基流动/DeepSeek/智谱/OpenAI）和新增的 OpenVINO | ① 补云端 provider 解析；② 新增 OpenVINO 分支从 `openvino-config.json` + 模型目录推导；③ 前端加空状态引导「请先在 AI 配置中添加模型」 |
| 3 | **Playwright 访问被重定向到登录** | Playwright 打开 `/openclaw`，结果跳到 `/login`，后续 selector 全失败 | 管理面板基于 OS 权限授权，无 cookie 时被 auth middleware 拦截 | 在打开页面前先调用 `http://127.0.0.1:5177/api/auth/cli-token` 获取一次性 token，拼到 URL `?cli-token=xxx` 后自动登录 |
| 4 | **暗色模式下部分卡片/区块亮色硬编码** | 切暗色后，默认模型卡片、本地 AI 卡片仍是亮白底，对比度刺眼 | Blazor 组件里直接写了 `class="bg-light"` / `bg-white`，没有走 data-theme 变量 | 在组件 `<style>` 中加 `[data-bs-theme="dark"] .mb-4.p-3.border.rounded.bg-light { background-color: rgba(255,255,255,0.05) !important; }` 等覆盖，使用全局暗色调色板 `#232a3b / #2d3548 / #3d4558 / #c0c8d0` |

---

## 7. 待完善项

| 优先级 | 项 | 说明 |
|--------|----|------|
| **P0** | **真实 OpenVINO 环境端到端验证** | 目前仅验证了 API 链路（保存/读取/RoundTrip/模型收集）。需在装了 `openvino-genai-server` + 真实 OV 模型目录的机器上，验证「检测 → 自动启动 → 健康检查 → 同步模型 → OpenClaw 实际发推理请求」全链路 |
| **P1** | 设备 Device 的真实枚举 | 目前下拉框是静态 4 项（CPU/GPU/NPU/AUTO），但 Intel 不同代 NPU 名称可能有差异（如 `NPU` / `AUTO:GPU,NPU,CPU`），建议改为健康检查成功后从服务端探测可用设备列表回填 |
| **P1** | 模型目录下多模型识别 | 真实 OV 模型目录可能包含子模型（如 `qwen2.5-7b/` 和 `qwen2.5-7b-chat/` 在同一个父目录），当前按目录名仅推导 1 个，需要扫描子目录里的 `openvino_tokenizer.bin` 或 `config.json` 来枚举 |
| **P2** | 下载管理集成 | 与 Ollama 的 `LocalAI.DownloadDirectory` 对齐，为 OV 模型提供「下载 → 校验 hash → 自动放入 ModelPath」的一体化流程，而不是让用户手动填路径 |
| **P2** | `openclaw config set` 版本检测前置 | 之前踩过 Node 版本不对导致 CLI 失败的坑，建议在调用 CLI 前先 `node --version` 和 `openclaw --version` 检查，低于最低版本时在 UI 上给出明确提示而不是静默失败 |
| **P2** | Windows NPU 权限/驱动检测 | 在启用 NPU 前先检测 Intel NPU 驱动是否安装（可查 `devcon` / WMI `Win32_PnPEntity` 中的 `Intel(R) AI Boost`），未安装时给出链接和指引 |
| **P3** | 启动命令预览 | 在「检测」按钮旁加「预览启动命令」，让用户看到即将执行的完整命令行，方便排查问题时复制到 cmd 手动跑 |
| **P3** | 模型大小/精度徽标 | 扫描模型时从 `config.json` 读取 `num_parameters` / `torch_dtype`（或从 OV 配置读取精度），在模型卡片上显示「7B INT4」之类的徽标，与 Ollama 保持一致 |

---

## 8. API 快速参考

| 方法 | 路径 | 说明 |
|------|------|------|
| GET  | `/api/openclaw/default-model` | 获取默认模型（CurrentModel + AvailableModels） |
| POST | `/api/openclaw/default-model` | 保存默认模型（写入 `agents.defaults.model.primary`） |
| GET  | `/api/openclaw/local-ai` | 获取本地 AI 配置（含 Ollama/LM/llama/OpenVINO 四个 DTO） |
| POST | `/api/openclaw/local-ai` | 保存本地 AI 配置（按 §4.1 规则双写） |
| POST | `/api/openclaw/local-ai/detect-openvino` | 检测 OpenVINO 服务状态，未启动则尝试启动 |
| POST | `/api/openclaw/local-ai/sync-models/openvino` | 扫描模型并同步到 openclaw.json providers |
| GET  | `/api/auth/cli-token` | 获取一次性 CLI 登录 token（调试/自动化用） |

---

## 9. 验证记录（2026-08-08）

**RoundTrip 全字段验证（PASS）**：
- POST 任意合法 OpenVINO 配置 → HTTP 200
- GET 读取 → 9 个字段（Enabled/BinaryPath/ModelPath/BaseUrl/Port/Device/ContextSize/ApiType/ExtraArgs）与写入值逐字节一致
- 可用模型列表：包含 `openvino/qwen25-7b-int4-ov`
- CurrentModel 不受影响：保持 `deepseek/deepseek-v4-flash`

**环境恢复验证（PASS）**：
- 删除测试假文件（openvino-config.json、假 bat、空模型目录）后，CurrentModel 仍为 flash，AvailableModels = 2 个（flash + pro），无测试残留
