# 百花服务 K8s 部署指南

## 架构概览

```
┌─────────────────────────────────────────────────────────────┐
│                    K8s Cluster (baihua namespace)            │
│                                                               │
│  ┌──────────┐     ┌──────────┐     ┌──────────────────────┐ │
│  │ bh-nginx │────▶│ bh-webui │────▶│ bh-family            │ │
│  │ :30080   │     │ :5177    │     │ :8788 (OpenVINO+GPU) │ │
│  │ NodePort │     │ Blazor   │     │ .NET + Python        │ │
│  └──────────┘     └──────────┘     └──────┬───────────────┘ │
│                                            │                  │
│                   ┌──────────┐     ┌──────▼──────┐          │
│                   │ bh-ai    │     │ bh-vault    │          │
│                   │ :8791    │     │ :8790       │          │
│                   │ AI 代理  │     │ 知识库管理  │          │
│                   └──────────┘     └─────────────┘          │
│                                                               │
│  ┌───────────────────────────────────────────────────────┐  │
│  │ Intel GPU Device Plugin (DaemonSet, kube-system)      │  │
│  │ → 注册 intel.com/gpu 扩展资源                          │  │
│  │ → 自动挂载 /dev/dri/renderD128 到 Pod                  │  │
│  └───────────────────────────────────────────────────────┘  │
│                                                               │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐    │
│  │ data PVC │  │ logs PVC │  │ vaults   │  │ models   │    │
│  │ 10Gi     │  │ 5Gi      │  │ 50Gi     │  │ 50Gi     │    │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘    │
└─────────────────────────────────────────────────────────────┘
```

## 文件清单

| 文件 | 说明 |
|------|------|
| `00-namespace.yaml` | 命名空间 |
| `01-configmap.yaml` | 共享配置（非敏感） |
| `02-secret.yaml` | 敏感配置（密码、密钥） |
| `03-pvc.yaml` | 持久化存储（data/logs/vaults/models） |
| `10-intel-gpu-plugin.yaml` | Intel GPU Device Plugin DaemonSet |
| `20-vault.yaml` | bh-vault Deployment + Service |
| `21-ai.yaml` | bh-ai Deployment + Service |
| `22-family.yaml` | bh-family Deployment + Service（含 GPU） |
| `23-webui.yaml` | bh-webui Deployment + Service |
| `24-nginx-configmap.yaml` | Nginx 配置（K8s DNS 适配） |
| `25-nginx.yaml` | Nginx Deployment + NodePort Service |
| `deploy.sh` | 一键部署脚本 |
| `../docker/Dockerfile.family-openvino.prebuilt` | 含 OpenVINO 的 Family 镜像 |

## 前提条件

### 1. K8s 集群

需要原生 Linux K8s 集群（不能用 WSL2 内的 Docker Desktop）：

```bash
# 方式 A: kind（推荐，开发用）
kind create cluster --name baihua

# 方式 B: minikube
minikube start --driver=docker

# 方式 C: 生产集群（kubeadm / RKE / 云托管 K8s）
# 确保节点有 Intel GPU 驱动
```

### 2. 节点 GPU 驱动

```bash
# 检查节点是否有 /dev/dri
ls -la /dev/dri/
# 应看到: renderD128, card0 等

# 检查 Intel GPU 驱动
lspci | grep -i vga
# 应看到 Intel 显卡设备

# 安装 Intel GPU 运行时（Ubuntu/Debian）
sudo apt install -y intel-opencl-icd level-zero-dev libigdgmm12
```

### 3. .NET SDK + Docker

```bash
dotnet --version   # 10.0+
docker --version   # 24+
```

## 部署步骤

### 一键部署

```bash
cd k8s
chmod +x deploy.sh
./deploy.sh all
```

### 分步部署

#### 1. 构建镜像

```bash
./deploy.sh build
```

构建 4 个镜像：
- `bh-vault:latest` — 标准镜像
- `bh-ai:latest` — 标准镜像
- `bh-webui:latest` — 标准镜像
- `bh-family-openvino:latest` — **含 OpenVINO + Intel GPU 运行时**

#### 2. 加载镜像到集群

```bash
# kind
./deploy.sh load

# 或手动
kind load docker-image bh-vault:latest bh-ai:latest bh-webui:latest bh-family-openvino:latest
```

#### 3. 填写 Secret

编辑 `02-secret.yaml`，填入实际值：

```bash
# 生成管理员密码哈希
dotnet run --project services/Baihua.Family -- generate-hash <your-password>

# 生成移动端密钥
openssl rand -hex 32

# 生成加密密钥
openssl rand -base64 32
```

#### 4. 部署

```bash
./deploy.sh deploy
```

#### 5. 验证 GPU

```bash
./deploy.sh verify-gpu
```

预期输出：
```
OpenVINO 可用设备: ['CPU', 'GPU']
✅ Intel GPU 可用
```

## 验证部署

```bash
# 查看状态
./deploy.sh status

# 访问 WebUI
# http://<节点IP>:30080

# 查看日志
./deploy.sh logs bh-family 100
```

## Intel GPU 配置详解

### Device Plugin 工作原理

1. `intel-gpu-plugin` DaemonSet 在每个节点运行
2. 扫描 `/dev/dri/renderD128` 等设备
3. 向 K8s 注册 `intel.com/gpu` 扩展资源
4. Pod 声明 `resources.limits.intel.com/gpu: 1` 时，自动挂载 GPU 设备

### bh-family Pod GPU 访问链路

```
Pod (bh-family)
├── /dev/dri/renderD128  ← Intel GPU Device Plugin 自动挂载
├── Python + openvino-genai
│   └── Core().available_devices → ['CPU', 'GPU']
├── openvino_llm_server.py  ← OpenAI 兼容推理服务
│   └── --device GPU  ← 使用 Intel GPU 推理
└── .NET bh-family.dll
    └── CapabilityService → 检测到 GPU → OpenClaw 菜单显示
```

### OpenVINO 推理服务

bh-family Pod 内通过 `openvino_llm_server.py` 提供 OpenAI 兼容 API：

- 模型路径：`/opt/baihua/models/<model-dir>`（PVC 挂载）
- 端口：8000（Pod 内部）
- 设备：GPU（环境变量 `OPENVINO_DEVICE=GPU`）
- API：`/v1/models`, `/v1/chat/completions`

在 WebUI 的 OpenClaw 页面配置 OpenVINO provider 时：
- BinaryPath 留空（自动探测）
- ModelPath 填 `/opt/baihua/models/Qwen2.5-VL-7B-Instruct-int4-ov`
- Device 选 `GPU`
- Port 填 `8000`

## 与 Docker Compose 对比

| 维度 | Docker Compose | K8s |
|------|---------------|-----|
| 网络 | host (Linux) / bridge (Windows) | Service DNS |
| GPU 访问 | `--gpus all` (仅 NVIDIA) | `intel.com/gpu` (Device Plugin) |
| OpenVINO | ❌ WSL2 不支持 Intel GPU | ✅ 原生 Linux |
| 持久化 | bind mount | PVC |
| 配置 | .env 文件 | ConfigMap + Secret |
| 反向代理 | nginx container (host net) | nginx Pod (NodePort) |
| 扩缩容 | 手动 | `kubectl scale` |
| 自愈 | restart: unless-stopped | K8s 自动重启 + 健康检查 |
| 滚动更新 | 手动重建 | `kubectl set image` + RollingUpdate |

## 故障排查

### Pod 无法调度（GPU 不足）

```bash
kubectl -n baihua describe pod -l app=bh-family
# 如果看到 "0/1 nodes are available: 1 Insufficient intel.com/gpu"
# 说明 Device Plugin 未注册 GPU，检查:
kubectl -n kube-system get pods -l app=intel-gpu-plugin
kubectl get nodes -o custom-columns=NAME:.metadata.name,GPU:.status.capacity.'intel\.com/gpu'
```

### OpenVINO 检测不到 GPU

```bash
# 进入 Pod 检查
kubectl -n baihua exec -it deployment/bh-family -- bash

# 检查设备节点
ls -la /dev/dri/
# 应看到 renderD128

# 检查 OpenCL
clinfo | head -20

# 检查 OpenVINO
python3 -c "from openvino.runtime import Core; print(Core().available_devices)"
```

### 模型文件缺失

```bash
# 检查模型目录
kubectl -n baihua exec deployment/bh-family -- ls -la /opt/baihua/models/

# 如果为空，需要将模型上传到节点的 /opt/baihua/models/
scp -r Qwen2.5-VL-7B-Instruct-int4-ov user@node:/opt/baihua/models/
```

### Nginx 502

```bash
# 检查后端服务
kubectl -n baihua exec deployment/bh-nginx -- nginx -t
kubectl -n baihua get svc
# 确保 bh-family 和 bh-webui Service 有 Endpoints
kubectl -n baihua get endpoints
```

## 生产环境建议

1. **StorageClass**：替换 hostPath 为网络存储（NFS / Ceph / 云盘）
2. **HPA**：对 bh-webui 配置 HorizontalPodAutoscaler
3. **Ingress**：替换 NodePort 为 Ingress + TLS 证书
4. **监控**：部署 DCGM exporter + Prometheus（Intel GPU 指标）
5. **日志**：Fluentd/Fluent Bit 收集到 OpenObserve
6. **镜像仓库**：使用 Harbor / ACR 推送镜像，避免 `imagePullPolicy: IfNotPresent`

## 附录：CapabilityService GPU 检测链路

```
K8s Pod (bh-family)
├── /dev/dri/renderD128 ← Device Plugin 挂载
├── HardwareInfoService.Gpu.cs
│   └── DetectGpu()
│       ├── lspci | grep -i vga → "Intel Corporation Arc 130T"
│       ├── 判断 IsIntegratedGpu → false（Arc 是独显）
│       └── GetHardwareTier → LowEndGpu 或 MidRangeGpu
├── CapabilityService.cs
│   └── GetCapability()
│       ├── cap >= LowEndGpu → true
│       ├── OpenClawLocalConfig → available ✅
│       ├── LocalModelDeployment → available ✅
│       └── LocalAiInference → available ✅
└── WebUI FamilyNavMenu.razor
    └── OpenClaw 菜单显示 ✅
```
