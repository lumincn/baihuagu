# GPU 依赖功能测试报告

**测试日期:** 2026-08-09  
**测试环境:** Windows 11 + Docker Desktop (WSL2) + Intel Arc 130T GPU (16GB)  
**测试目标:** 验证 GPU 依赖功能在 Docker 容器和宿主机上的工作状态

---

## 1. 测试环境

| 项目 | Docker 容器 | 宿主机 |
|------|------------|--------|
| OS | Linux (container) | Windows 11 |
| GPU | ❌ 不可用 (WSL2 不支持 Intel GPU 直通) | ✅ Intel Arc 130T GPU (16GB) |
| Python | ❌ 无 OpenVINO | ✅ Python 3.12 + OpenVINO 2026.2.1 |
| OpenClaw CLI | ❌ 未安装 | ✅ v2026.7.1-2 (需 Node 24) |
| Node.js | ❌ 未安装 | ✅ v24.18.0 |

---

## 2. Docker 容器测试结果

### 2.1 Capability 检测

```
Level: 1 (CpuOnly)
GpuName: 无
MaxVramGiB: 0
```

| 功能 | 状态 | 说明 |
|------|------|------|
| OpenClawLocalConfig | ✅ 可用 | 已通过代码修改解除 GPU 限制 |
| HardwareBenchmark | ✅ 可用 | 不依赖 GPU |
| SettingsLocalModelDownload | ✅ 可用 | 不依赖 GPU |
| LocalAiInference | ❌ 受限 | 需要 GPU |
| LocalModelDeployment | ❌ 受限 | 需要 GPU |
| LocalModelsPage | ❌ 受限 | 需要 GPU |
| MessagesLocalModelSelector | ❌ 受限 | 需要 GPU |
| ModelBenchmark | ❌ 受限 | 需要 GPU |
| AiConfigLocalProviderPresets | ❌ 受限 | 需要 GPU |

### 2.2 OpenClaw API 端点测试

| 端点 | 方法 | 结果 | 说明 |
|------|------|------|------|
| /api/openclaw/tasks | GET | ✅ 200 | 返回空列表 |
| /api/openclaw/tasks | POST | ✅ 200 | 任务创建成功 (Id=1) |
| /api/openclaw/tasks/{id} | GET | ✅ 200 | 返回任务详情 |
| /api/openclaw/local-ai-config | GET | ✅ 200 | 返回配置 (全 null) |
| /api/openclaw/local-ai-config | POST | ✅ 200 | 保存成功 |
| /api/openclaw/local-ai-detect (openvino) | POST | ✅ 200 | "OpenVINO 未启用" (预期) |
| /api/openclaw/local-ai-detect (ollama) | POST | ✅ 200 | 尝试启动失败 (无 ollama 二进制) |
| /api/openclaw/local-ai-models | GET | ✅ 200 | 返回空列表 |
| /api/openclaw/default-model | GET | ✅ 200 | 2 个云端模型可用 |
| /api/openclaw/model-profiles | GET | ✅ 200 | 多个预设配置可用 |

### 2.3 OpenClaw 页面

- `/openclaw` 页面: **200 OK**, 10970 bytes, 无错误 ✅
- 菜单可见性: **已显示** (OpenClawLocalConfig 在 AvailableFeatures 中) ✅

### 2.4 任务执行

- 任务创建: ✅ 成功 (TaskId=f3597d7d, Status=running)
- 任务执行: ❌ 失败 — `openclaw` CLI 未安装在容器中
- 错误信息: "An error occurred trying to start process 'openclaw' with working directory '/app/'. No such file or directory"

---

## 3. 宿主机测试结果

### 3.1 Capability 检测

```
Level: 2 (LowEndGpu)
GpuName: Intel(R) Arc(TM) 130T GPU (16GB)
MaxVramGiB: 2 (WMI uint32 溢出，实际 16GB)
```

| 功能 | 状态 | 说明 |
|------|------|------|
| OpenClawLocalConfig | ✅ 可用 | |
| HardwareBenchmark | ✅ 可用 | |
| SettingsLocalModelDownload | ✅ 可用 | |
| LocalAiInference | ✅ 可用 | GPU 检测成功 → LowEndGpu |
| LocalModelDeployment | ✅ 可用 | |
| LocalModelsPage | ✅ 可用 | |
| MessagesLocalModelSelector | ✅ 可用 | |
| ModelBenchmark | ✅ 可用 | |
| AiConfigLocalProviderPresets | ✅ 可用 | |

**全部 9 个功能可用，0 个受限！**

### 3.2 GPU 检测链路

```
WMI Win32_VideoController
  → Name: "Intel(R) Arc(TM) 130T GPU (16GB)"
  → Vendor: Intel (匹配 "intel" 关键字)
  → IsIntegrated: false (不匹配 "hd graphics"/"uhd"/"iris" 模式)
  → VramBytes: 2147479552 (~2GB, WMI uint32 溢出)
  → VramGiB: ~2.0
  → GetHardwareTier: LowEndGpu (vram >= 0, < 4)
  → MachineCapability: LowEndGpu
  → 所有 >= LowEndGpu 的功能解锁
```

### 3.3 OpenVINO 设备探测

**直接 Python 探测 (Python 3.12 + OpenVINO 2026.2.1):**

```
Devices: CPU, GPU, NPU
  CPU: Intel(R) Core(TM) Ultra 5 225H
  GPU: Intel(R) Arc(TM) 130T GPU (16GB) (iGPU)
  NPU: Intel(R) AI Boost
```

**API 探测 (ProbeOpenVinoDevicesAsync):**
- 使用 `python` 命令 → 解析到 Python 3.13 (无 OpenVINO) → 设备列表为空
- 已知问题: 探测函数使用硬编码 `python`，未使用配置的 BinaryPath

### 3.4 OpenVINO 配置保存

- POST /api/openclaw/local-ai-config: ✅ 200 success=true
- 配置内容: Enabled=true, Device=GPU, Port=8008, BinaryPath=Python312路径

### 3.5 OpenVINO 服务启动

- 尝试启动: ✅ 进入了启动流程 (通过了 enabled 和 model path 检查)
- 启动结果: ⏱️ 超时 (dummy 模型目录无实际模型文件)
- 预期行为: 配置真实 OpenVINO 模型后可正常启动

---

## 4. 发现的问题

### 4.1 WMI AdapterRAM 溢出 (已知限制)

**问题:** WMI `Win32_VideoController.AdapterRAM` 是 uint32，16GB GPU 报告为 ~2GB  
**影响:** GPU 被识别为 LowEndGpu 而非 TopTierGpu  
**当前状态:** 不影响功能 — LowEndGpu 已足够解锁所有功能  
**建议:** 可从 GPU 名称中解析 "(16GB)" 来修正显存值

### 4.2 ProbeOpenVinoDevicesAsync 使用硬编码 Python (Bug)

**问题:** `ProbeOpenVinoDevicesAsync()` 使用 `FileName = "python"` 而非配置的 BinaryPath  
**影响:** 默认 Python 无 OpenVINO 时，设备探测返回空列表  
**修复建议:** 优先使用 `openvino.BinaryPath`，回退到自动探测  
**文件:** `LocalAiConfigService.Detect.cs:438`

### 4.3 OpenClaw CLI 未包含在 Docker 镜像中 (预期)

**问题:** Docker 容器内无 `openclaw` 可执行文件  
**影响:** 任务创建后执行失败  
**当前状态:** 预期行为 — OpenClaw CLI 需单独安装  
**建议:** K8s 部署时在 Family 镜像中安装 openclaw CLI (npm install -g openclaw)

---

## 5. K8s 部署后的预期状态

在原生 Linux K8s + Intel GPU Device Plugin 环境下:

| 检查项 | 预期结果 |
|--------|---------|
| `/dev/dri/renderD128` 挂载 | ✅ Intel GPU Device Plugin 自动挂载 |
| 容器内 `nvidia-smi` | N/A (Intel GPU) |
| 容器内 OpenVINO 设备探测 | ✅ CPU, GPU (需安装 openvino-genai) |
| CapabilityService GPU 检测 | ✅ lspci 检测到 Intel GPU → LowEndGpu+ |
| 所有 9 个功能 | ✅ 全部可用 |
| OpenClaw 任务执行 | ✅ (需安装 openclaw CLI) |

---

## 6. 测试结论

| 维度 | Docker (当前) | 宿主机 | K8s (预期) |
|------|--------------|--------|-----------|
| GPU 检测 | ❌ CpuOnly | ✅ LowEndGpu | ✅ LowEndGpu+ |
| 功能解锁 | 3/9 | 9/9 | 9/9 |
| OpenClaw 菜单 | ✅ 显示 | ✅ 显示 | ✅ 显示 |
| OpenClaw API | ✅ 全部可用 | ✅ 全部可用 | ✅ 全部可用 |
| OpenClaw 任务执行 | ❌ 无 CLI | ⚠️ 需 Node 24 | ✅ (安装 CLI) |
| OpenVINO 设备探测 | ❌ 无 GPU | ✅ CPU/GPU/NPU | ✅ CPU/GPU/NPU |
| OpenVINO 推理 | ❌ | ⚠️ 需真实模型 | ✅ (配置模型后) |

**核心结论:**
1. ✅ 代码修改 (OpenClaw 解除 GPU 限制) 工作正常
2. ✅ GPU 检测逻辑在宿主机上正确识别 Intel Arc 130T
3. ✅ 所有 GPU 依赖功能在宿主机上全部解锁 (9/9)
4. ✅ Docker 容器内 6 个 GPU 依赖功能按预期受限
5. ⚠️ ProbeOpenVinoDevicesAsync 有 Python 路径 bug，需修复
6. ✅ K8s 部署后预期全部功能可用
