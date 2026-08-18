# 一服务一数据库 · 阶段1 交接与排障文档

> 用途：把"阶段1 推理切 shim"的**当前部署状态**、**未完成改动**、**构建问题**完整记录下来，
> 供其它工具/开发者继续排查与收尾。配套目标文档：`docs/ONE_SERVICE_ONE_DB.md`（阶段划分与设计）。

## 1. 一、一句话现状

代码已全部完成并提交推送（`77d80e4`），**除一个"shim 返回 tool_calls arguments 格式"的小修复外，
其余改动已成功部署并验证通过**。目前唯一卡住的是：**bh-ai 新镜像构建命令反复被工具中断，
未能把最后一个小修复部署上去**。系统当前运行正常（测速/聊天/tools 均已验证）。

## 2. 二、已完成且已部署验证的部分（勿重复排查）

| 改动 | 状态 | 验证结果 |
|------|------|----------|
| Family 推理切 shim（`AiClient__UseShim=true`，仅 Family 设） | ✅ 已部署 | 测速 `Family→shim→deepseek` = 22.9 tok/s |
| shim 工具透传（ParseTools/ParseMessages/tool_calls 返回） | ✅ 已部署 | 带 tools 请求返回 `tool_calls` + `finish_reason=tool_calls` |
| AI 服务不缓存响应（NoOpDistributedCache + useCache:false） | ✅ 已部署 | AI pod 稳定（修复了 OOM） |
| bh-ai 内存限制 1Gi→2Gi（k8s/21-ai.yaml，已 apply） | ✅ 已部署 | — |
| 本地模型、拜师/任务回归 | ⏳ 未做 | 见第 6 节 |

当前运行中的 pod（均为 75m 前 rollout，稳定）：
- `bh-ai-f5b75589b-sd2w6`（镜像 367bb87f0448）
- `bh-family-59db5f657d-84994`

## 3. 三、唯一未部署的改动（本次目标）

**文件**：`services/Baihua.AI/Controllers/OpenAiCompatController.cs`
**方法**：`NonStreamResponseAsync`
**改动**：shim 返回的 `tool_calls[].function.arguments` 由**对象**改为 **JSON 字符串**（OpenAI 协议要求字符串）：

```csharp
// 已改（工作区代码）：
var argsJson = functionCall.Arguments is { Count: > 0 }
    ? JsonSerializer.Serialize(functionCall.Arguments)
    : "";
function = new { name = functionCall.Name, arguments = argsJson }
```

**影响面**：仅 Family 侧"工具调用"链路（OpenAI SDK 解析 tool_calls 时，arguments 为对象可能抛异常）。
普通聊天、测速、流式均不受影响。**该改动已在 git 工作区并已随 `77d80e4` 提交，只是镜像没重建。**

## 4. 四、构建问题记录（为什么卡住）

### 现象
- 运行中的 bh-ai 镜像时间戳为 **3 days ago**（`sudo nerdctl images | grep bh-ai` → `367bb87f0448 3 days ago`），
  说明含 arguments 修复的构建从未成功完成。
- 多次 `nerdctl build` 调用（前台/后台）均被**工具层中断**（返回 "tool call aborted"，build 未执行完）。
- **不是构建本身的问题**：此前同样命令每次约 45 秒成功（详见下）。

### 构建环境与铁律（重要）
```bash
# 必须标准 nerdctl build（不要 -o type=image），且一次只构建一个镜像（并发会污染共享 NuGet 缓存）
cd /home/lumin/src/mdyj/baihuagu
sudo nerdctl -a /run/k3s/containerd/containerd.sock -n k8s.io build --no-cache -t bh-ai:latest -f k8s/images/Dockerfile.ai .
# 成功标志：输出含 "unpacking to docker.io/library/bh-ai:latest ... done"
```

### 历史 OOM 记录（已修复，供参考）
- bh-ai 曾因 `AddDistributedMemoryCache`（无限内存缓存）在转发负载下 OOM（exit 139），
  已通过 `NoOpDistributedCache` + shim 转发 `useCache:false` + 2Gi 内存修复（已部署）。

## 5. 五、恢复/收尾步骤（给接手工具）

1. **构建**：执行第 4 节的 nerdctl build 命令（串行，等完成标志）。
2. **部署**：`sudo k3s kubectl rollout restart deployment/bh-ai -n baihua`，
   然后 `sudo k3s kubectl rollout status deployment/bh-ai -n baihua --timeout=180s`。
3. **验证**（共 4 项）：
   ```bash
   # a. 测速（Family→shim→deepseek）
   curl -s -m 90 -X POST http://192.168.3.13/mg/benchmark/run -H 'Content-Type: application/json' \
     -d '{"modelName":"deepseek-v4-flash"}'
   # b. 聊天（pool 网关非流式）
   curl -s -m 90 -X POST http://192.168.3.13/mg/pool/v1/chat/completions -H 'Content-Type: application/json' \
     -d '{"model":"deepseek-v4-flash","messages":[{"role":"user","content":"你好"}]}'
   # c. shim 工具调用（期望 tool_calls 的 arguments 为 JSON 字符串）
   sudo k3s kubectl exec -n baihua deploy/bh-family -- sh -c \
     'curl -s -m 60 -X POST http://bh-ai:8791/mg/ai/v1/chat/completions -H "Content-Type: application/json" \
      -d "{\"model\":\"deepseek-v4-flash\",\"messages\":[{\"role\":\"user\",\"content\":\"查北京天气，用工具\"}],\"tools\":[{\"type\":\"function\",\"function\":{\"name\":\"get_weather\",\"description\":\"查询天气\",\"parameters\":{\"type\":\"object\",\"properties\":{\"city\":{\"type\":\"string\"}},\"required\":[\"city\"]}}}]}"'
   # d. AI 服务稳定：sudo k3s kubectl get pods -n baihua | grep bh-ai（无频繁 Restart）
   ```
4. **继续阶段2/3**（见 docs/ONE_SERVICE_ONE_DB.md）：Family 删 AIDbContext、
   ChatMemory/Comfy/Benchmark/Backup 改道。

## 6. 六、阶段1 剩余回归项（部署完成后做）

- [ ] 流式聊天（Family 聊天页）
- [ ] 工具调用端到端（拜师/任务/OpenClaw，会走到 Function Calling）
- [ ] 本地模型经 shim 转发（OpenVINO：模型名 `qwen2-5-vl-7b-instruct-int4-ov`，
      确认 AI 服务能路由到 `http://bh-openvino:8000/v1`）
- [ ] 算力池对端互调（寻芳居 .9 需 `bh update` 后回归 peer 注册/选用）

## 7. 七、回滚开关（万一出问题）

`Family/Program.cs` 中 `builder.Configuration["AiClient__UseShim"] = "true";` 改为 `"false"` 即恢复
Family 直连模型（旧行为）。AI 服务无需改动。
