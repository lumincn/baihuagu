# OpenVINO 性能对比测试报告：Windows 原生 vs WSL2

> 测试日期：2026-08-09
> 硬件：Intel Core Ultra 5 225H / Arc 130T 16GB iGPU / Intel AI Boost NPU
> 模型：Qwen2.5-VL-3B-Instruct-int4-ov (2.57GB) / Qwen2.5-VL-7B-Instruct-int4-ov (4.83GB)

---

## 1. 测试环境

| 维度 | Windows 原生 | WSL2 (Arch Linux) |
|------|-------------|-------------------|
| OS | Windows 11 | Arch Linux (WSL2 kernel) |
| Python | 3.12.10 | 3.14.6 |
| OpenVINO | 2026.2.1 (release) | 2026.3.0 (dev) |
| OpenVINO GenAI | 2026.2.1.0 | 2026.3.0.0 |
| CPU | Intel Core Ultra 5 225H | 同左（共享物理 CPU） |
| GPU | Intel Arc 130T 16GB | **不可见**（WSL2 不支持 Intel GPU 直通） |
| NPU | Intel AI Boost | **不可见** |

## 2. 测试方法

- 模型：openvino_genai VLMPipeline（VL 模型必须用 VLMPipeline）
- 解码：贪心（do_sample=False），max_new_tokens=128
- 每个测试用例运行 3 轮（3B）/ 2 轮（7B），取平均值
- 5 个测试用例：short_qa / medium_qa / long_generate / reasoning / code_gen
- 额外变量：WSL2 分别测试 /mnt/c（NTFS via 9p）和 ext4 原生文件系统

## 3. 吞吐量结果（tokens/s，越高越好）

### 3B 模型

| 测试用例 | Win CPU | Win GPU | Win NPU | WSL2 CPU (/mnt/c) | WSL2 CPU (native) |
|----------|---------|---------|---------|---------------------|---------------------|
| short_qa | 6.4 | 5.4 | 0.1 | 4.1 | 2.9 |
| medium_qa | **18.5** | 14.0 | 0.4 | 9.2 | 7.3 |
| long_generate | **13.4** | 10.9 | 0.3 | 5.3 | 5.3 |
| reasoning | **15.4** | 13.2 | 0.4 | 6.8 | 6.1 |
| code_gen | **14.9** | 12.2 | 0.3 | 7.0 | 5.8 |
| **平均** | **13.7** | **11.1** | **0.3** | **6.5** | **5.5** |

### 7B 模型

| 测试用例 | Win CPU | Win GPU | GPU vs CPU |
|----------|---------|---------|------------|
| short_qa | 2.6 | 3.5 | GPU +35% |
| medium_qa | 7.3 | **9.4** | GPU +29% |
| long_generate | 6.0 | **7.3** | GPU +22% |
| reasoning | 6.5 | **8.1** | GPU +25% |
| code_gen | 6.3 | **7.5** | GPU +19% |
| **平均** | **5.7** | **7.2** | **GPU +26%** |

### 关键发现：GPU/CPU 交叉点

| 模型大小 | CPU 吞吐 | GPU 吞吐 | 赢家 | 差距 |
|----------|---------|---------|------|------|
| 3B | 18.5 tps | 14.0 tps | CPU | CPU 快 32% |
| 7B | 7.3 tps | 9.4 tps | GPU | GPU 快 29% |

**3B 模型 CPU 更快**（GPU 初始化开销 > 计算收益），**7B 模型 GPU 反超**（计算密集度超过开销阈值）。交叉点大约在 5-6B 参数量。

## 4. 延迟结果（秒，越低越好）

### 3B 模型

| 指标 | Win CPU | Win GPU | WSL2 (/mnt/c) | WSL2 (native) | Win NPU |
|------|---------|---------|----------------|---------------|---------|
| 模型加载 | 5.93 | 15.92 | 4.37 | 4.15 | 46.11 |
| Warmup | 4.50 | **2.07** | 6.55 | 4.93 | 26.86 |
| 平均 TTFT | **0.37** | 0.51 | 0.64 | 0.79 | 10.05 |

### 7B 模型

| 指标 | Win CPU | Win GPU |
|------|---------|---------|
| 模型加载 | 5.56 | 19.53 |
| Warmup | 15.40 | **2.07** |
| 平均 TTFT | 1.02 | **0.49** |

**GPU 的 TTFT 优势随模型增大而放大**：3B 时 GPU TTFT 比 CPU 高 38%（0.51 vs 0.37），7B 时 GPU TTFT 比 CPU 低 52%（0.49 vs 1.02）。GPU 擅长处理大模型的 prefill 阶段。

## 5. Windows vs WSL2 CPU 对比（3B 模型）

| 指标 | Win CPU | WSL2 (/mnt/c) | WSL2 (native) | Win 优势 |
|------|---------|----------------|---------------|---------|
| 平均吞吐 | 13.7 tps | 6.5 tps | 5.5 tps | **2.1x ~ 2.5x** |
| 平均 TTFT | 0.37s | 0.64s | 0.79s | **1.7x ~ 2.1x** |
| 模型加载 | 5.93s | 4.37s | 4.15s | WSL2 快 26-30%* |
| Warmup | 4.50s | 6.55s | 4.93s | Win 快 9-31% |

*WSL2 模型加载更快可能因为 Linux ext4 文件读取效率更高，但推理性能差距说明瓶颈不在 I/O 而在计算层。

### WSL2 性能损耗原因分析

1. **WSL2 VM 开销**：WSL2 运行在轻量级 Hyper-V VM 中，CPU 指令需经过虚拟化层
2. **内存映射差异**：WSL2 的内存分配策略与 Windows 原生不同，可能影响 OpenVINO 的内存池
3. **OpenVINO 版本差异**：Windows 用 2026.2.1 (release)，WSL2 用 2026.3.0 (dev)，dev 版本可能未完全优化
4. **CPU 调度差异**：WSL2 内核的 CPU 调度器与 Windows 原生不同，可能影响线程亲和性

## 6. NPU 测试结论

Intel AI Boost NPU 完全不适合 LLM 推理：
- 吞吐量 0.3-0.4 tokens/s（比 CPU 慢 40-50 倍）
- 模型加载 46s（比 CPU 慢 8 倍）
- TTFT 10s（比 CPU 慢 27 倍）
- 单次生成 160s（比 CPU 慢 20-40 倍）

NPU 设计用于小型模型（<1B）和特定算子，不适用于 3B+ 参数的 Transformer 模型。

## 7. 结论与建议

### 性能排名（3B 模型，综合吞吐+延迟）

| 排名 | 方案 | 平均吞吐 | 适用场景 |
|------|------|---------|---------|
| 1 | Windows CPU | 13.7 tps | 小模型（<5B）开发/生产 |
| 2 | Windows GPU | 11.1 tps | 大模型（7B+）开发/生产 |
| 3 | WSL2 CPU | 6.5 tps | 仅 Linux 专用工具链需要时 |
| 4 | Windows NPU | 0.3 tps | **不可用**于 LLM |

### 对百花服务的建议

1. **OpenVINO-GenAI-server 应运行在 Windows 原生**，不在 WSL2 中
2. **3B 及以下模型用 CPU**，7B 及以上用 GPU
3. **设备选择策略**：CapabilityService 可根据模型大小自动推荐 CPU/GPU
4. **NPU 设备**：在 UI 中标注「不适合 LLM 推理」或直接隐藏
5. **WSL2 仅用于 Linux 专用工具链**（如 K8s 部署测试），不用于推理
6. **已有 K8s 清单中的 bh-openvino Pod** 应部署在裸金属 Linux 上以获得 GPU 加速

### 模型文件存储

- Windows：`C:\Users\lumin\.openclaw\models\`（当前已有 3B + 7B 模型）
- WSL2：`~/models/`（ext4 原生，避免 /mnt/c 的 9p 开销）
- K8s：PVC `baihua-models-pvc`（50Gi，hostPath 或 NFS）
