# 百花服务 K8s 部署指南

## 架构概览

```
┌─────────────────────────────────────────────────────────────────┐
│                    K8s Cluster (baihua namespace)                │
│                                                                  │
│  ┌──────────┐     ┌──────────┐     ┌──────────────────────┐     │
│  │ bh-nginx │────▶│ bh-webui │────▶│ bh-family            │     │
│  │ :30080   │     │ :5177    │     │ :8788 (轻量, 无GPU)  │     │
│  │ NodePort │     │ Blazor   │     │ .NET only            │     │
│  └──────────┘     └──────────┘     └──────┬───────────────┘     │
│                                            │ HTTP                │
│                   ┌──────────┐     ┌──────▼──────────────┐      │
│                   │ bh-ai    │     │ bh-openvino         │      │
│                   │ :8791    │     │ :8000 LLM  :8801 VS │      │
│                   │ AI 代理  │     │ OpenVINO + Intel GPU│      │
│                   └──────────┘     └─────────────────────┘      │
│                                                                  │
│  ┌──────────┐     ┌──────────┐                                  │
│  │ bh-vault │     │  GPU     │ ← intel.com/gpu (Device Plugin)  │
│  │ :8790    │     │ 8Gi RAM  │                                  │
│  └──────────┘     └──────────┘                                  │
│                                                                  │
│  ┌──────────┐  ┌──────────┐  ┌──────────┐  ┌──────────┐        │
│  │ data PVC │  │ logs PVC │  │ vaults   │  │ models   │        │
│  │ 10Gi     │  │ 5Gi      │  │ 50Gi     │  │ 50Gi     │        │
│  └──────────┘  └──────────┘  └──────────┘  └──────────┘        │
│                                              ↑ ↑                 │
│                                    RW(OpenVINO) RO(Family scan) │
└─────────────────────────────────────────────────────────────────┘
```

## 文件清单

| 文件 | 说明 |
|------|------|
| `00-namespace.yaml` | 命名空间 |
| `01-configmap.yaml` | 共享配置（含 OpenVINO 服务 URL） |
| `02-secret.yaml` | 敏感配置（密码、密钥） |
| `03-pvc.yaml` | 持久化存储（data/logs/vaults/models） |
| `10-intel-gpu-plugin.yaml` | Intel GPU Device Plugin DaemonSet |
| `20-vault.yaml` | bh-vault Deployment + Service |
| `21-ai.yaml` | bh-ai Deployment + Service |
| **`22a-openvino.yaml`** | **bh-openvino Deployment + Service（GPU 推理）** |
| `22-family.yaml` | bh-family Deployment + Service（轻量, 无 GPU） |
| `23-webui.yaml` | bh-webui Deployment + Service |
| `24-nginx-configmap.yaml` | Nginx 配置（K8s DNS 适配） |
| `25-nginx.yaml` | Nginx Deployment + NodePort Service |
| `deploy.sh` | 一键部署脚本 |
| `./images/Dockerfile.openvino-server.prebuilt` | OpenVINO 独立推理容器 |
| `./images/Dockerfile.family.prebuilt` | Family 轻量容器（无 OpenVINO） |

## 架构设计：OpenVINO 独立容器

### 为什么拆分？

| 维度 | 之前（嵌入 Family） | 之后（独立容器） |
|------|---------------------|------------------|
| Family 镜像大小 | ~3GB（.NET + Python + OpenVINO） | ~800MB（.NET only） |
| GPU 资源 | 绑定在 Family Pod | 仅 OpenVINO Pod |
| 升级 OpenVINO | 需重建 Family 镜像 | 独立重建 OpenVINO 镜像 |
| 扩缩容 | Family + GPU 一起扩 | 可独立扩 OpenVINO |
| 代码改动 | — | 极小（HTTP 接口不变） |

### 通信方式

Family 服务**已经通过 HTTP** 调用 OpenVINO（`openvino_llm_server.py` 的 `/v1/chat/completions`）。
改造只是把 `localhost:8000` 换成 `http://bh-openvino:8000`（K8s Service DNS）。

- **本地模式**（Docker Compose / 开发环境）：Family 通过 `Process.Start` 拉起本地 Python 服务
- **远程模式**（K8s）：设置 `OPENVINO_LLM_URL=http://bh-openvino:8000`，Family 跳过进程启动，直接调用远程 API

### 模型文件存储

```
PVC: baihua-models-pvc (50Gi, hostPath: /opt/baihua/models)
├── bh-openvino Pod:  挂载 /models (RW) — 推理读取
└── bh-family Pod:    挂载 /opt/baihua/models (RO) — 模型扫描（UI 列表）
```

单节点用 RWO hostPath 即可；多节点需改为 NFS + RWX。

## 前提条件

依赖分两类：**bh 命令能自动安装的**（缺失时 build 自动下载安装）与**需手动安装的**（系统级/交互式，见下文各节）：

| 依赖 | 用途 | 自动安装 | 触发 |
|------|------|---------|------|
| nerdctl | k8s 镜像构建（直连 containerd） | ✅ `bh.sh build` 自动装（GitHub release → /usr/local/bin） | build 时 |
| buildkit（buildkitd+buildctl） | nerdctl build 的守护进程 + 客户端 | ✅ 同上，一次下载装两个 | build 时 |
| .NET SDK 10 | native 部署/构建（linux-native） | ✅ `bh.sh build` 自动装（dotnet-install.sh → ~/.dotnet） | build 时 |
| .NET SDK 10 | native 部署/构建（win-native/win-docker） | ✅ winget 自动装 | build 时 |
| k3s | K8s 运行时 | ❌ 需 root + 网络，手动装 | 见 1 |
| Docker Desktop | win-docker 部署 | ❌ GUI 交互安装 | 见 4 |
| Intel GPU 驱动 | openvino GPU 推理 | ❌ 系统级 | 见 2 |

> 注意：buildkitd 自动安装后**不会自动启动**（无 systemd 环境）。首次 build 会提示启动命令。

### 1. K8s 集群

推荐 k3s（单节点、轻量、自带 containerd，不依赖 docker）：

```bash
# 方式 A: k3s（推荐）
curl -sfL https://get.k3s.io | sh -
```

> 不推荐 kind：kind 依赖 docker 且 GPU 不可用。
> 生产集群（kubeadm / RKE / 云托管 K8s）同样用 containerd/CRI，无需 docker。

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

### 3. 构建依赖（自动安装）

`bh.sh build` 会自动下载安装缺失的 nerdctl 与 buildkit（buildkitd 守护进程 + buildctl 客户端，官方 GitHub release → /usr/local/bin，需 root/sudo）。
dotnet（native 部署用）由 `bh-linux-native.sh build` 自动装到 ~/.dotnet。无需手动装：

```bash
# 验证（已安装时）
nerdctl --version      # 2.3.5+
buildkitd --version    # 0.32.x
buildctl --version     # 0.32.x（nerdctl build 需要）
dotnet --version       # 10.0+
```

> buildkitd 是 nerdctl build 的后端守护进程，安装后需运行（无 systemd 环境手动启动）：
> ```bash
> nohup buildkitd -config /etc/buildkit/buildkitd.toml > /tmp/buildkitd.log 2>&1 &
> ```

> 镜像构建用 `nerdctl -a /run/k3s/containerd/containerd.sock build`，构建完直接进入 k3s 的 containerd，
> 无需 docker，也无需 load/import。日常入口：`../tools/bh/linux/k8s/bh.sh`（build/up/status/logs）。

### 4. Docker Desktop（仅 Windows docker 部署用）

`bh-win-docker.ps1` 需要 Docker Desktop。需 GUI 交互安装（无法自动完成）：

```powershell
winget install --id Docker.DockerDesktop
# 或手动下载: https://www.docker.com/products/docker-desktop/
# 安装后启动 Docker Desktop，等待引擎就绪（docker info 可用）
```

> Linux k8s 部署**不需要** docker（nerdctl 直连 containerd）。Windows 纯 native 部署（`bh-win-native.ps1`）也不需要。

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

构建 5 个镜像：
- `bh-vault:latest` — 标准镜像
- `bh-ai:latest` — 标准镜像
- `bh-webui:latest` — 标准镜像
- `bh-family:latest` — **轻量镜像（无 OpenVINO）**
- `bh-openvino:latest` — **OpenVINO 推理服务 + Intel GPU 运行时**

#### 2. 加载镜像到集群

```bash
# kind
./deploy.sh load

# k3s（本地节点，镜像已在 docker 中）
# k3s 自动从 containerd 拉取本地镜像，无需额外操作
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

#### 5. 下载模型

```bash
# 在节点上创建模型目录
sudo mkdir -p /opt/baihua/models

# 下载 OpenVINO 模型（示例: Qwen2.5-VL-7B）
# 使用 HuggingFace + optimum-cli 转换
pip install optimum[openvino]
optimum-cli export openvino --model Qwen/Qwen2.5-VL-7B-Instruct --task image-text-to-text --weight-format int4 /opt/baihua/models/Qwen2.5-VL-7B-Instruct-int4-ov

# 或直接下载预转换模型
# https://huggingface.co/OpenVINO/qwen2-vl-7b-instruct-int4-ov
```

#### 6. 验证 GPU + OpenVINO

```bash
./deploy.sh verify-gpu
```

预期输出：
```
1. Device Plugin 已部署
2. 节点有 1 个 Intel GPU
3. OpenVINO 可用设备: ['CPU', 'GPU']
   Intel GPU 可用
4. 模型: Qwen2.5-VL-7B-Instruct-int4-ov, 设备: GPU, VL: True
5. Family → OpenVINO 连通性: OK
```

## 验证部署

```bash
# 查看状态
./deploy.sh status

# 访问 WebUI
# http://<节点IP>:30080

# 查看日志
./deploy.sh logs bh-family 100
./deploy.sh logs bh-openvino 100
```

## Intel GPU 配置详解

### Device Plugin 工作原理

1. `intel-gpu-plugin` DaemonSet 在每个节点运行
2. 扫描 `/dev/dri/renderD128` 等设备
3. 向 K8s 注册 `intel.com/gpu` 扩展资源
4. Pod 声明 `resources.limits.intel.com/gpu: 1` 时，自动挂载 GPU 设备

### bh-openvino Pod GPU 访问链路

```
Pod (bh-openvino)
├── /dev/dri/renderD128  ← Intel GPU Device Plugin 自动挂载
├── Python + openvino-genai
│   └── Core().available_devices → ['CPU', 'GPU', 'NPU']
├── openvino_llm_server.py  ← OpenAI 兼容推理服务 (:8000)
│   └── --device GPU  ← 使用 Intel GPU 推理
├── vision_server.py        ← 视觉推理服务 (:8801)
│   └── Qwen2.5-VL 3B/7B
└── /models/ ← PVC 挂载（模型文件）
```

### bh-family Pod（无 GPU）

```
Pod (bh-family)
├── .NET bh-family.dll
│   ├── OPENVINO_LLM_URL=http://bh-openvino:8000 (ConfigMap)
│   ├── DetectAndStartOpenVinoAsync()
│   │   └── 检测到远程 URL → 跳过本地 Python 启动
│   ├── ProbeOpenVinoDevicesAsync()
│   │   └── 调用 http://bh-openvino:8000/health 获取设备
│   └── ScanOpenVinoModelsAsync()
│       └── 扫描 /opt/baihua/models/ (RO PVC) → UI 模型列表
└── /opt/baihua/models/ ← PVC 只读挂载（模型扫描）
```

## 与 Docker Compose 对比

| 维度 | Docker Compose | K8s |
|------|---------------|-----|
| 网络 | host (Linux) / bridge (Windows) | Service DNS |
| GPU 访问 | `--gpus all` (仅 NVIDIA) | `intel.com/gpu` (Device Plugin) |
| OpenVINO | ❌ WSL2 不支持 Intel GPU | ✅ 原生 Linux |
| OpenVINO 架构 | 嵌入 Family 容器 | 独立 bh-openvino 容器 |
| 持久化 | bind mount | PVC |
| 配置 | .env 文件 | ConfigMap + Secret |
| 反向代理 | nginx container (host net) | nginx Pod (NodePort) |
| 扩缩容 | 手动 | `kubectl scale` |
| 自愈 | restart: unless-stopped | K8s 自动重启 + 健康检查 |
| 滚动更新 | 手动重建 | `kubectl set image` + RollingUpdate |

## 故障排查

### Pod 无法调度（GPU 不足）

```bash
kubectl -n baihua describe pod -l app=bh-openvino
# 如果看到 "0/1 nodes are available: 1 Insufficient intel.com/gpu"
# 说明 Device Plugin 未注册 GPU，检查:
kubectl -n kube-system get pods -l app=intel-gpu-plugin
kubectl get nodes -o custom-columns=NAME:.metadata.name,GPU:.status.capacity.'intel\.com/gpu'
```

### OpenVINO 检测不到 GPU

```bash
# 进入 OpenVINO Pod 检查
kubectl -n baihua exec -it deployment/bh-openvino -- bash

# 检查设备节点
ls -la /dev/dri/
# 应看到 renderD128

# 检查 OpenCL
clinfo | head -20

# 检查 OpenVINO
python3 -c "from openvino.runtime import Core; print(Core().available_devices)"
```

### Family 无法连接 OpenVINO

```bash
# 检查 Service
kubectl -n baihua get svc bh-openvino
# 应有 Endpoints

# 从 Family Pod 测试连通性
kubectl -n baihua exec deployment/bh-family -- curl -s http://bh-openvino:8000/health
```

### 模型文件缺失

```bash
# 检查 OpenVINO Pod 的模型目录
kubectl -n baihua exec deployment/bh-openvino -- ls -la /models/

# 检查 Family Pod 的模型目录（只读）
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
7. **OpenVINO 扩缩容**：多 GPU 节点时可 `kubectl scale deployment/bh-openvino --replicas=N`
