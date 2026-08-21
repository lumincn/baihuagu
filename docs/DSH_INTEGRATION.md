# 百花 × DeepSeek Harness 集成（DSH 交互面）

> 架构定位：**百花 = 能力提供方**（算力池/本机模型/知识库/家庭数据），**DSH = 编排与交互面**。
> 百花 Web 内 AI 消费型功能（AI 对话 / 编程 Agent / 图片识别 / AI 绘图）已下线菜单入口，
> 统一走 DSH 智能体（`/dsh` 页 = DSH 控制台）。百花核心业务（知识库/家庭/任务/移动端）保留。

## 部署拓扑

```
┌─────────────── 宿主机（如 192.168.3.13，Linux/k3s） ───────────────┐
│  DSH web（手动启动，127.0.0.1:3080）                                │
│   ├─ baihua-dsh-plugin      桥接（agent 会话/事件流 + bh 运维 + 百花数据工具）│
│   ├─ baihua-local-ai-dsh-plugin  LLM provider（本机 OVMS + 算力池网关）   │
│   └─ lanListen 0.0.0.0:3081  仅 /dsh-bridge/* 的局域网桥（token 鉴权）    │
│                                                                      │
│  k3s：family/ai/vault/webui/openvino/postgres（ClusterIP 直连免签名）   │
└───────────────────────────────────────────────────────────────────┘
        ▲ LAN :3081（token）                    ▲ 百花 Web(k8s) 经 DshApi__BaseUrl
```

## 1. 启动 DSH（手动，不常驻）

DSH 迭代期不稳定，**保持手动启动**：

```bash
# Node ≥ 22.19；首次会下载/使用 npx 缓存
npx @deepseek-ai/dsh web            # 监听 127.0.0.1:3080
# 关终端即停；需要后台时再自行选择 nohup/systemd（不推荐常驻）
```

> 不要用 `--host 0.0.0.0`——DSH 故意禁止（会把远程代码执行暴露到网络）。
> 局域网访问走插件的 `lanListen`（只暴露桥接口）。

## 2. 安装插件（每台跑 DSH 的机器一次）

两个插件仓库在 `/home/lumin/src/mdyj/`（org `luminsw`，public）：

```bash
dsh plugin --profile web add /home/lumin/src/mdyj/baihua-dsh-plugin
dsh plugin --profile web add /home/lumin/src/mdyj/baihua-local-ai-dsh-plugin
# 或从 GitHub：dsh plugin --profile web add github:luminsw/baihua-dsh-plugin
```

## 3. 插件配置（~/.dsh/profiles/web/cordis.patch.yml）

```yaml
- id: dsh-baihua-bridge
  name: baihua-dsh-plugin
  config:
    token: '<共享密钥>'                                    # 必须（对外暴露时）
    lanListen: '0.0.0.0:3081'                             # 局域网桥（仅 /dsh-bridge/*）
    bhCommand: '/home/lumin/src/mdyj/baihuagu/tools/bh/bh.sh'  # 运维 CLI（本机路径）
    vaultUrl: 'http://10.43.242.109:8790'                 # k8s Vault ClusterIP
    familyUrl: 'http://10.43.159.101:8788'                # k8s Family ClusterIP
    comfyUrl: 'http://127.0.0.1:8188'                     # ComfyUI（本机）

- id: dsh-baihua-local-ai
  name: baihua-local-ai-dsh-plugin
  config:
    token: '<共享密钥>'
    poolUrl: 'http://192.168.3.13/mg/pool/v1'             # 算力池网关（全网路由+failover）
    # poolToken: '...'                                    # 网关配置了 BAIHUA_AI_EXTERNAL_TOKEN 时填写
```

> k8s ClusterIP 可用 `kubectl get svc -n baihua bh-vault bh-family -o jsonpath='{.items[*].spec.clusterIP}'` 查询；
> 服务重建后可能变化，需同步更新。

## 4. 百花 Web（k8s）对接

ConfigMap `baihua-config`（`baihua` 命名空间）注入：

```yaml
DshApi__BaseUrl: http://192.168.3.13:3081   # 宿主机 lanListen 地址
DshApi__Token: <与插件相同的 token>
```

改后 `kubectl rollout restart deploy bh-webui -n baihua`。`/dsh` 页显示"DSH 在线"即通。

## 5. 桥接接口一览（/dsh-bridge/*，除 /status 外均需 token）

| 端点 | 说明 |
|---|---|
| `GET /status` | 健康检查（不鉴权） |
| `GET /sessions` · `POST /chat` · `GET /sessions/{id}/history` · `WS /stream` | agent 会话驱动（百花 /dsh 页用） |
| `GET /bh/status` · `POST /bh/action` · `GET /bh/ops[/{id}]` · `GET /bh/logs` | 百花服务运维（启停/编译/更新/日志） |
| `GET /bh/status-ui` | **只读状态**（仅 127.0.0.1、免鉴权，供 DSH 设置页卡片拉取；LAN 不暴露） |
| DSH 工具 | `bh_*`（运维，含 `bh_build_restart` 编译并重启）、`baihua_vault_*`（知识库）、`baihua_budget_summary`、`baihua_tasks_list`、`baihua_draw`（ComfyUI） |

## 6. 运维界面

百花 → DSH 智能体（`/dsh`）→ 右上「🧰 运维」：服务状态表 + 启停/重启/编译并重启/编译/更新/部署/日志（打开后每 10s 自动刷新）。
底层是 `bh status --json` / `bh start|stop|restart <svc>`（已并入 `tools/bh/linux/k8s/bh.sh`）。

**DSH 设置页卡片**：DSH Web UI → 设置 → 插件 →「百花服务状态」卡片，只读展示百花各服务状态并自动刷新（`baihua-dsh-plugin` 的浏览器侧客户端模块，数据源 `/dsh-bridge/bh/status-ui`）。

## 6.5 百花能力 MCP server（标准对外通道）

独立仓库 [`luminsw/baihua-mcp-server`](https://github.com/luminsw/baihua-mcp-server)
（`@modelcontextprotocol/sdk`，stdio），把百花只读能力按标准 MCP 暴露给**任意** MCP
客户端（不只是 DSH）：

- 工具：`baihua_vault_search` / `baihua_vault_list` / `baihua_vault_read_note` / `baihua_budget_summary` / `baihua_tasks_list`
- 连接目标经环境变量：`BAIHUA_VAULT_URL`（默认 127.0.0.1:8790）、`BAIHUA_FAMILY_URL`（默认 127.0.0.1:8788）
- 本机检出：`/home/lumin/src/mdyj/baihua-mcp-server`（npm install 后可直接跑）

DSH 接入（profile patch，需先 `dsh plugin --profile web add @deepseek-ai/dsh-mcp-client`）：

```yaml
- insert:                      # 新建行必须用 insert 包裹（裸 - id: 只能覆盖已有行）
    - id: mcp-baihua
      name: '@deepseek-ai/dsh-mcp-client'
      config:
        serverName: baihua
        transport: stdio
        command: node
        args: ['/home/lumin/src/mdyj/baihua-mcp-server/src/index.js']
        env:
          BAIHUA_VAULT_URL: 'http://<vault-clusterip>:8790'
          BAIHUA_FAMILY_URL: 'http://<family-clusterip>:8788'
```

DSH 里工具名带 `mcp__baihua__` 前缀（如 `mcp__baihua__baihua_vault_search`）。
其他 MCP 客户端（Claude Desktop / Cursor 等）直接以 stdio 方式指向
`baihua-mcp-server/src/index.js` 即可。

## 7. 已下线页面

AI 对话（/messages）、编程 Agent（/code-agent）、图片识别（/image-recognition）、AI 绘图（/ai-drawing）
——菜单已隐藏，路由保留（实现与数据未删，稳定后再删；MAF 实现代码按决定**保留**）。
AI 实验室场景首页改指 `/dsh`。

## 8. 插件更新后的重启

插件是 `pnpm link` 安装，改完代码**重启 DSH 即生效**：

```bash
pkill -f "dsh web"; npx @deepseek-ai/dsh web
```

> 注意：当前 DSH 由本机手动启动（不常驻）。重启会重新加载插件源码与 profile patch
> （含 mcp-baihua 等 insert 行）。

百花侧改动（Web 页面/后端）走 `bh build <svc> && bh restart <svc>`（或 /dsh 页「🧰 运维」）。
