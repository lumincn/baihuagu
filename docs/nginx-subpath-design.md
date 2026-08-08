# Nginx 统一 80 端口 & 子路径路由 — 设计与实施计划
> 状态：🔧 设计已评审（2026-08-08），**第二轮代码核对完成（2026-08-08）**，待实施
> 日期：2026-08-08（首次评审）／2026-08-08（代码核对修订）
> 目标：用户在浏览器输入 `http://<server-ip>`（默认 80 端口，无端口号）即可打开管理面板；花记移动端仍用同域名/IP 配对，无需端口号或任何改动；子路径前缀/Nginx 监听端口支持配置，适配多样部署环境。
> **重要**：本版已逐项对照代码核实（docker/nginx.conf、Baihua.Family/Web Program.cs、Baihua.Core OneHop、BaihuaSdk、Kotlin/ArkTS SDK），修正了首版中与实际代码不符的假设（见 §12「与代码事实的核对」）。

### 评审决策（2026-08-08）

| 决策项 | 结论 |
|--------|------|
| 容器命名 | 统一改为 `baihua-*`（从 `yj-family-*` 渐进改名，Nginx 先行） |
| 监听端口 | 默认 **80**（被占用时可 `.env` 改 `BAIHUA_NGINX_PORT`） |
| Nginx 启用方式 | 默认 `docker compose up` 就拉起，不放 profile |
| PathBase 与花记 SDK | **不拼前缀**。花记 SDK 中的 `/mg/*` 等路径永远相对根，配对二维码只写 `scheme://host[:port]`，SDK 自己拼路径 |
| Nginx 日志目录 | 放 `$BAIHUA_HOME/logs/nginx`（与 .NET 日志同根，`bh.ps1 logs nginx` 统一查看） |
| **OneHop 端口**（第二轮修订） | **保持动态推导，不要硬编码**。服务端 `OneHopService.GetAvailablePort()` 默认 8792、从 QR 地址取 `uri.Port + 1`；ArkTS 端 `QRCodeService.ets:87-88` 的 `httpPort + 1` 与之镜像。改为固定 8789 会破坏配对（详见 §11.3.4 修订）<br>✅ **第三轮定稿（2026-08-08）**：项目未上线，**不做兼容**——OneHop 组件整体删除（含命名），`/mg/onehop/register-device` 改名 `/mg/register-device`，TCP 监听删除。本节端口推导**作废**，详见 [ONEHOP_SIMPLIFICATION_PLAN.md](https://github.com/luminsw/project-manager/blob/master/docs/ONEHOP_SIMPLIFICATION_PLAN.md) |

---

## 1. 现状与问题

### 1.1 当前入口（无 Nginx 时）

用户/设备必须记住 **4 个端口号**，体验不友好：

| 服务 | 端口 | 说明 |
|------|------|------|
| Baihua.Web (WebUI) | **5177** | Blazor Server 管理面板，浏览器打开 `http://ip:5177` |
| Baihua.Family | **8788** | 花记移动端发现/配对/同步入口，`http://ip:8788`；**移动端唯一授权网关** |
| Baihua.AI | **8791** | 模型/聊天接口。**有公开路径** `/api/ai/chat/completion|stream`（AI Program.cs 公开白名单），但移动端**不应直连**——必须经 Family 的 `/api/ai/chat` HMAC 授权代理 |
| Baihua.Vault | **8790** | 知识库同步/搜索接口。有 `/vault/*`、`/api/search/*` 等端点，**不应直连**——必须经 Family 的授权转发 |

`bh.ps1 dashboard` 打开的是 `http://127.0.0.1:5177`，手动访问时漏打端口号会打不开。

### 1.2 现有 Nginx（docker/nginx/nginx.conf）的问题

1. **监听 8080 而非 80**：用户仍需打端口号，达不到"直接 `http://localhost`"的目标
2. **安全漏洞**：`/vault/`、`/api/search/`、`/api/vaults/`、`/api/settings/vault-root` 直接 proxy_pass 到 Vault（`:8790`），**绕过了 Family 的设备授权中间件**（HMAC 验证 + X-Device-Id 查授权表 + Bearer Token 注入）——局域网任意设备都可以未授权下载知识库文件、调用搜索接口
3. **配置静态不可改**：nginx.conf 是写死路径的静态文件，无法通过 `.env` 调整监听端口、WebUI 路径前缀等
4. **WebSocket 配置不完整**：Blazor Server 的根路径 `/` 配了 Upgrade，但 OpenClaw WebSocket 推送、AI 流式响应（SSE）未针对性调 `proxy_read_timeout` / `proxy_buffering`
5. **无静态缓存**：Blazor 生成的 `_framework/*.js/.wasm`、css 静态资源没有 `expires` / `Cache-Control`，首屏慢

### 1.3 Family 现有转发架构（不能动的核心逻辑）

Family (`:8788`) 在 Program.cs 中有**三段用自定义中间件实现的“安全代理”**，这是移动端的授权边界，**Nginx 不能替代**。已核对真实路径（Program.cs）：

```
花记 App (局域网)
    │  HMAC 签名请求头 + X-Device-Id
    ▼
Family :8788
    ├─ 签名验证中间件（HMAC，全局）
    ├─ 访问控制中间件（公开路径白名单 + loopback 检查）
    │
    ├─ 转发① Vault（Program.cs ~L600）：
    │     路径：/mg/manifest /mg/file /mg/file_chunk /mg/cards /mg/vaults
    │           /mg/auth/config /mg/verify-token /mg/note-count
    │           /api/sync/  /vault/manifest /vault/file /vault/file_chunk
    │           /mobile-vaults/push
    │     → 验证 HMAC → X-Device-Id 查已授权设备表 → 附加 Authorization: Bearer <token>
    │     → 转发到 Vault :8790（FamilySyncAuthorizationStrategy 校验 Bearer）
    │
    └─ 转发② AI（Program.cs ~L685）：
         路径：/api/ai/chat（前缀匹配，含 /api/ai/chat/stream SSE）
         → 同样的 HMAC + 设备授权 → 转发到 AI :8791

WebSocket（Program.cs）：
    /ws/devices        → 移动端设备推送 WebSocket（非 /ws/push！）
    /hubs/task-progress → Family SignalR（任务进度）
    /hubs/devices      → Family SignalR（设备状态）
    /hubs/status       → WebUI Blazor SignalR（:5177，注意与 Family 的 /hubs/* 区分）
```

**结论**：所有来自移动端的流量（`/mg/*`、`/onehop/*`、`/api/ai/chat`、`/ws/devices`）**必须先到 Family**，不能让 Nginx 直接分发到 Vault/AI。Family 作为移动端的“统一授权网关”角色保持不变。

---

## 2. 设计目标与非目标

### 2.1 目标（Must Have）

| # | 目标 | 验证方法 |
|---|------|---------|
| M1 | 浏览器打开 `http://<server-ip>`（默认 80）直接进入 WebUI 管理面板 | 手动输入 URL 直达登录页，无端口号 |
| M2 | 花记移动端（鸿蒙/安卓/花圃）配对/同步**协议零改动** | 把原来的 `http://ip:8788` 改成 `http://ip` 配对手册全流程通过。**注意**：SDK 的 `normalizeBaseUrl` 默认端口需从 8788 改为 80（§11），属 SDK 一行改动，不涉及配对协议/路径 |
| M3 | 修复现有 Nginx 配置的未授权访问漏洞（Vault/AI 直连） | 用 curl 直接打 `http://ip/mg/manifest` 能通过 Family 正常鉴权；`http://ip/vault/xxx` 不绕过 Family |
| M4 | 子路径前缀/监听端口可通过 `.env` 配置（无需改代码和 nginx.conf） | 修改 `.env` → `docker compose up -d` → 新路径生效 |
| M5 | Blazor Server（SignalR）、移动端 WebSocket、AI SSE 流式响应 100% 可用 | 打开 OpenClaw 聊天不中断、流式字符正常、配对/同步 WebSocket 不丢消息 |
| M6 | 与现有 docker-compose（host 网络、mDNS 服务发现、systemd）兼容 | 保留 `network_mode: host`，不影响现有 `bh.ps1 start` / systemd 部署方式 |

### 2.2 非目标（Nice to Have 或不在本期）

- ❌ 引入 HTTPS/TLS 终止（家庭局域网场景价值低，自签证书反而让移动端配对麻烦，留后续二期）
- ❌ 负载均衡/多实例（家庭版单实例足够）
- ❌ 用 Docker bridge 网络替代 host 网络（mDNS 广播需要 host，bridge 下无法发 224.0.0.251:5353）
- ❌ 改花记 App 的 API 路径前缀（保持 `/mg/*` 等写死路径不变）

---

## 3. 总体架构

```
                    ┌──────────────────────────────────────────────────┐
   浏览器 /          │               Docker Host (NUC/旧笔记本)          │
   花记 App          │                                                    │
   (局域网)          │   listen 80 (可配置 BAIHUA_NGINX_PORT)            │
       │            │                                                    │
       │            │  ┌──────────────────────────────────────────────┐  │
       │            │  │              Nginx Container                 │  │
       │            │  │  - envsubst 渲染模板化 conf                   │  │
       │            │  │  - 静态资源缓存、WebSocket/SSE 超时调优        │  │
       │            │  └──────────────┬───────────────────────────────┘  │
       │            │                 │  所有 upstream 走 127.0.0.1      │
       │            │    ┌────────────┴───────────────┐                  │
       │            │    │                            │                  │
       │            │    ▼                            ▼                  │
       │            │  根路径 /                 管理/移动端 API           │
       │            │  (WebUI)                 /mg/* /onehop/* /pair    │
       │            │  :5177                   /ws/devices /api/* /hubs/* │
       │            │                           :8788 (Family)          │
       │            │                            │                       │
       │            │                            │  授权代理（代码内）    │
       │            │                            ├───────────► Vault 8790│
       │            │                            └───────────► AI    8791│
       └────────────►                                            ◄───────┘
                    │                                            (loopback 白名单/
                    │                                             Bearer Token 验证)
                    └──────────────────────────────────────────────────┘
```

**安全分层**：
- **Layer 1 Nginx**：连接管理、静态缓存、头部转发、超时控制——**不做业务授权**
- **Layer 2 Family (:8788)**：移动端 HMAC + 设备授权 + Bearer Token 注入——**这是唯一的移动端授权边界**
- **Layer 3 Vault/AI (:8790/:8791)**：仅接受 loopback IP 或带有效 Bearer Token 的请求（现状已满足，无需改动）

---

## 4. 子路径规划

### 4.1 默认路径映射（开箱即用，无需配置）

> 以下路径已对照代码核实（2026-08-08）：BaihuaSdk、Family Program.cs、Web Program.cs。

| 子路径前缀 | 上游服务 | 说明 | 可配？ |
|-----------|---------|------|-------|
| **`/`** | **WebUI :5177** | Blazor Server 根路径（用户需求） | ✅ 可加前缀（默认无前缀） |
| `/mg/*` | Family :8788 | 花记 SDK 写死前缀：manifest/file/cards/vaults/auth/config/pair/code/register-device/devices/push-pending | ❌ **不可改**（移动端硬约束） |
| `/mg/register-device` | Family :8788 | 扫码配对注册（原 `/mg/onehop/register-device` 改名，OneHop 精简后） | ❌ 不可改 |
| `/pair`、`/pair/code`、`/pair/code/refresh` | Family :8788 | 配对页面/配对码（PairController 同时注册 `/pair`、`/vault/pair`、`/mg/pair` 三份路由） | ❌ 不可改 |
| `/vault/pair`、`/vault/pair/code` | Family :8788 | 配对码别名（同一 PairController） | ❌ 不可改 |
| `/ws/devices` | Family :8788 | 移动端设备推送 WebSocket（Upgrade）——**注意不是 `/ws/push`** | ❌ 不可改 |
| `/api/ai/chat/*` | Family :8788 | AI 对话 + SSE 流式（Family HMAC 授权后转发 :8791） | ⚠️ 仅管理侧前缀可配 |
| `/api/*` | Family :8788 | 管理侧 API（Family 再分发到 Vault/AI） | ⚠️ 可改前缀仅限管理侧部分 |
| `/hubs/status` | WebUI :5177 | Blazor SignalR 状态 hub（**最长匹配优先于 Family 的 /hubs/**） | ❌ 不可改 |
| `/hubs/task-progress`、`/hubs/devices` | Family :8788 | Family 侧 SignalR（任务/设备实时推送） | ❌ 不可改 |
| `/_framework/*`、`/css/*`、`/img/*` | WebUI :5177（缓存） | Blazor 静态资源，加 `Cache-Control: public, max-age=31536000, immutable` | ❌ 不可改 |

### 4.2 路径前缀可配置项（部署环境多样性）

通过 `.env` 文件控制，**只对"不影响花记移动端 SDK 兼容性"的部分开放**：

| 变量名 | 默认值 | 说明 | 示例（群晖 DSM 已有反代时） |
|--------|--------|------|---------------------------|
| `BAIHUA_NGINX_PORT` | `80` | Nginx 对外监听端口 | `8080`（运营商封 80 时） |
| `BAIHUA_WEBUI_PREFIX` | `""`（空=根路径） | WebUI 管理面板的路径前缀，用于挂在已有反代下面 | `/baihua` → 访问 `http://ip:8080/baihua/` |
| `BAIHUA_WEBUI_STATIC_CACHE` | `true` | 是否对 Blazor `_framework` 静态资源加 1 年强缓存 | `false`（开发调试时） |
| `BAIHUA_NGINX_CLIENT_MAX_BODY_SIZE` | `100M` | 上传文件大小限制（知识库附件上传、头像） | `500M` |

**注意**：当 `BAIHUA_WEBUI_PREFIX` 非空时：
- WebUI **已支持 PathBase**（`services/Baihua.Web/Program.cs` ~L368：读配置 `BasePath`，非 `/` 时 `app.UsePathBase(basePath)`），compose 把 `BAIHUA_WEBUI_PREFIX` 映射到 `BasePath` 配置（环境变量 `BasePath` 或 appsettings）即可，**无需改代码**（首版文档误以为“当前未处理”，已核对）
- Nginx `proxy_pass http://127.0.0.1:5177/` 时用**带尾部斜杠**的写法，自动剥掉前缀再转发（避免 Blazor 收到重复前缀）

### 4.3 花记移动端兼容性（零改动）

**迁移前后 URL 对比**：用户只需把配对二维码或手动输入的地址从 `http://192.168.3.x:8788` 改为 `http://192.168.3.x`（或 `http://192.168.3.x:8080`），移动端代码不动。

| 迁移前（直连端口） | 迁移后（走 Nginx） |
|-------------------|-------------------|
| `http://ip:8788/mg/manifest` | `http://ip/mg/manifest` |
| `http://ip:8788/mg/vaults` | `http://ip/mg/vaults` |
| `http://ip:8788/mg/onehop/register-device` | `http://ip/mg/register-device` |
| `http://ip:8788/pair` | `http://ip/pair` |
| `ws://ip:8788/ws/devices` | `ws://ip/ws/devices` |

---

## 5. Nginx 配置设计（模板化）

### 5.1 渲染方式（支持 envsubst）

把现有的 `docker/nginx/nginx.conf` 改为 **`docker/nginx/default.conf.template`**，容器启动时：

```dockerfile
# 官方 nginx:1.27-alpine 自带 /docker-entrypoint.d/ 目录，
# 任何 .sh 放这里会自动执行；envsubst 也已预装。
# 只需要把 template 挂载到 /etc/nginx/templates/default.conf.template，
# entrypoint 会自动用 envsubst 渲染到 /etc/nginx/conf.d/default.conf
```

compose 中的挂载方式：
```yaml
volumes:
  - ./nginx/default.conf.template:/etc/nginx/templates/default.conf.template:ro
environment:
  - BAIHUA_NGINX_PORT=${BAIHUA_NGINX_PORT:-80}
  - BAIHUA_WEBUI_PREFIX=${BAIHUA_WEBUI_PREFIX:-}
  - BAIHUA_NGINX_CLIENT_MAX_BODY_SIZE=${BAIHUA_NGINX_CLIENT_MAX_BODY_SIZE:-100M}
```

### 5.2 关键 location 片段（草案）

```nginx
server {
    listen ${BAIHUA_NGINX_PORT};
    server_name _;
    client_max_body_size ${BAIHUA_NGINX_CLIENT_MAX_BODY_SIZE};

    # ==============================================================
    # 安全头部（家庭版不强制 HTTPS，但防点击劫持/MIME 嗅探）
    # ==============================================================
    add_header X-Frame-Options "SAMEORIGIN" always;
    add_header X-Content-Type-Options "nosniff" always;
    add_header Referrer-Policy "strict-origin-when-cross-origin" always;

    # ==============================================================
    # 1. Blazor 静态资源（带 hash 的文件名 → 1 年强缓存）
    #    优先级最高，提前命中可减轻 Kestrel 压力
    #    BAIHUA_WEBUI_STATIC_CACHE=false 时（开发调试）不加强缓存头
    # ==============================================================
    location ^~ ${BAIHUA_WEBUI_PREFIX}/_framework/ {
        proxy_pass http://127.0.0.1:5177${BAIHUA_WEBUI_PREFIX}/_framework/;
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        #if ($BAIHUA_WEBUI_STATIC_CACHE = "true")  ← envsubst 不支持条件，见下方说明
        expires 1y;
        add_header Cache-Control "public, immutable";
    }
    location ^~ ${BAIHUA_WEBUI_PREFIX}/css/ {
        proxy_pass http://127.0.0.1:5177${BAIHUA_WEBUI_PREFIX}/css/;
        expires 7d;
        add_header Cache-Control "public";
        # ... 头部同上
    }

    # ⚠️ envsubst 只做变量替换，不支持条件语句。
    # 实现 BAIHUA_WEBUI_STATIC_CACHE 开关的两种方式（择一）：
    #   A. 生成两个模板（default.conf.template / default-nocache.conf.template），
    #      容器 entrypoint 脚本按变量值选择渲染哪一个；
    #   B. 用 nginx 的 map 指令：map $arg_nocache $cc { default "public, max-age=31536000, immutable"; }，
    #      按请求参数控制（不推荐，纯静态场景用 A 更直观）。

    # ==============================================================
    # 2. Blazor SignalR Hub（WebSocket Upgrade + 长超时）
    # ==============================================================
    location ${BAIHUA_WEBUI_PREFIX}/hubs/status {
        proxy_pass http://127.0.0.1:5177${BAIHUA_WEBUI_PREFIX}/hubs/status;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400;
        proxy_buffering off;
    }

    # ==============================================================
    # 3. 移动端 API（Family 统一授权入口）
    #    关键：/mg/* /onehop/* /ws/devices /api/ai/chat 等必须先到 Family，
    #    绝对不能直连 Vault/AI（否则绕过授权）
    # ==============================================================
    location /mg/ {
        proxy_pass http://127.0.0.1:8788/mg/;
        include /etc/nginx/family-proxy-headers.conf;
        proxy_read_timeout 600;   # 大文件同步 10 分钟
    }
    # 配对码/配对页（PairController 同时注册 /pair /vault/pair /mg/pair）
    location = /pair {
        proxy_pass http://127.0.0.1:8788/pair;
        include /etc/nginx/family-proxy-headers.conf;
    }
    location = /pair/code {
        proxy_pass http://127.0.0.1:8788/pair/code;
        include /etc/nginx/family-proxy-headers.conf;
    }
    location = /vault/pair {
        proxy_pass http://127.0.0.1:8788/vault/pair;
        include /etc/nginx/family-proxy-headers.conf;
    }
    # 移动端设备推送 WebSocket（SDK 实际路径，不是 /ws/push）
    location /ws/devices {
        proxy_pass http://127.0.0.1:8788/ws/devices;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        include /etc/nginx/family-proxy-headers.conf;
        proxy_read_timeout 86400;
        proxy_buffering off;
    }
    # 管理侧 API + /api/ai/chat（Family 会再分发到 AI；SSE 必须关缓冲）
    location /api/ {
        proxy_pass http://127.0.0.1:8788/api/;
        include /etc/nginx/family-proxy-headers.conf;
        proxy_read_timeout 3600;  # AI 长对话/SSE
        proxy_buffering off;      # SSE 流式响应必须关缓冲
    }
    # Family SignalR hubs（task-progress/devices；/hubs/status 已在上方精确定位到 WebUI）
    location /hubs/ {
        proxy_pass http://127.0.0.1:8788/hubs/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
        include /etc/nginx/family-proxy-headers.conf;
        proxy_read_timeout 86400;
        proxy_buffering off;
    }

    # ==============================================================
    # 4. WebUI 根路径（放最后，最短匹配优先级最低）
    #    注意：proxy_pass 尾部 / 会自动剥掉 BAIHUA_WEBUI_PREFIX
    # ==============================================================
    location ${BAIHUA_WEBUI_PREFIX}/ {
        proxy_pass http://127.0.0.1:5177/;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection $http_connection;   # Blazor 推荐写法
        proxy_set_header Host $host;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_read_timeout 86400;
        proxy_buffering off;
        proxy_cache_bypass $http_upgrade;
    }
}
```

`family-proxy-headers.conf` 公共 include：
```nginx
proxy_http_version 1.1;
proxy_set_header Host $host;
proxy_set_header X-Real-IP $remote_addr;
proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
proxy_set_header X-Forwarded-Proto $scheme;
```

---

## 6. 服务端改造（PathBase + ForwardedHeaders）

### 6.1 WebUI 支持可配置 PathBase（已存在，仅需接线）

**已核实（2026-08-08）**：`Baihua.Web/Program.cs` ~L368 已支持 PathBase：

```csharp
// services/Baihua.Web/Program.cs（现状，无需新增代码）
var basePath = builder.Configuration.GetValue<string>("BasePath") ?? "/";
if (basePath != "/")
{
    app.UsePathBase(basePath);   // 自动剥离前缀、生成正确链接
}
```

`appsettings.json` 已有 `"BasePath": "/"`。**接线方式**：compose 的 webui 服务注入环境变量 `BasePath=${BAIHUA_WEBUI_PREFIX}`（或启动参数 `--BasePath`），其余无需改代码。

> 首版文档写“当前未处理”，与代码不符，已更正。

### 6.2 Family/AI/Vault ForwardedHeaders 信任链

**已核实（2026-08-08）**：三个服务**均已配置** `ForwardedHeaders` 且仅信任 loopback：
- Family：`Program.cs` ~L410（`KnownProxies.Clear()` + 只加 `IPAddress.Loopback` / `IPv6Loopback`）
- AI：`Program.cs` ~L203；Vault：`Program.cs` ~L233（同款配置）

因为 Nginx 容器用 `network_mode: host`，它和 .NET 服务共享 lo 接口，上游 IP 对 .NET 来说就是 127.0.0.1，**现有配置不用改**，信任链天然正确。

⚠️ **host 网络下 X-Forwarded-For 的注意点**：由于 Nginx 与 .NET 同机，`$proxy_add_x_forwarded_for` 会把真实客户端 IP 追加进 XFF 头，而 ForwardedHeadersMiddleware 因 KnownProxies 只有 loopback 会信任它。**这是设计意图**（Family 访问控制中间件据此判断非 loopback 请求）。若未来改用 bridge 网络，必须同步更新 KnownProxies 为 Nginx 容器 IP，否则 Family 会把所有请求当 loopback 放行（安全回归）。

### 6.3 WebUI 内部 API BaseUrl 配置策略

WebUI 当前 `appsettings.json`：
```json
"TaskRunnerApi":      { "BaseUrl": "http://127.0.0.1:8788/" },
"TaskRunnerAiApi":    { "BaseUrl": "http://127.0.0.1:8791/" },
"TaskRunnerVaultApi": { "BaseUrl": "http://127.0.0.1:8790/" }
```

**建议继续保持直连端口，不经过 Nginx**。理由：
1. WebUI 和其他服务都在 host 网络，127.0.0.1 打内网端口最快（无额外 Nginx 转发开销）
2. Vault/AI 对 loopback 有白名单策略（无需 Bearer Token），这是内部服务互信的基础，走 Nginx 反而会变成外部 IP 或需要重新授权
3. 不改变现有安全模型——内部互信不变，只有外部流量通过 Nginx

---

## 7. Docker Compose 改造

### 7.1 Nginx 服务（现有 service 重写）

```yaml
  nginx:
    image: nginx:1.27-alpine
    container_name: baihua-nginx   # 从 yj-family-* 改成 baihua-*，统一命名
    restart: unless-stopped
    network_mode: host             # 保持 host 网络：能直连 127.0.0.1 四个服务
    # ports:                       # 用 host 网络时 ports: 无效（直接占宿主机端口）
    volumes:
      # ↓ 关键：模板文件（含 ${BAIHUA_*} 变量）
      - ./nginx/default.conf.template:/etc/nginx/templates/default.conf.template:ro
      # ↓ 公共 include（静态，无变量）
      - ./nginx/family-proxy-headers.conf:/etc/nginx/family-proxy-headers.conf:ro
      # ↓ 对外访问日志（排查配对/同步问题时有用），与 .NET 日志同根
      - ${BAIHUA_HOME:-/opt/baihua}/logs/nginx:/var/log/nginx
    environment:
      # 传给 entrypoint envsubst 的变量，必须白名单列出
      - BAIHUA_NGINX_PORT=${BAIHUA_NGINX_PORT:-80}
      - BAIHUA_WEBUI_PREFIX=${BAIHUA_WEBUI_PREFIX:-}
      - BAIHUA_WEBUI_STATIC_CACHE=${BAIHUA_WEBUI_STATIC_CACHE:-true}
      - BAIHUA_NGINX_CLIENT_MAX_BODY_SIZE=${BAIHUA_NGINX_CLIENT_MAX_BODY_SIZE:-100M}
      - NGINX_ENVSUBST_FILTER=^BAIHUA_   # 官方 entrypoint 只替换 BAIHUA_* 前缀
    depends_on:
      taskrunner:
        condition: service_healthy
      taskrunner-ai:
        condition: service_healthy
      taskrunner-vault:
        condition: service_healthy
      webui:
        condition: service_healthy
    healthcheck:
      # 注意：/health 是上游 .NET 服务的端点，不是 nginx 自己的；
      # 这里探测 nginx 自身是否响应（根路径由 WebUI location 处理，200/404 都说明 nginx 活着）。
      # 若想聚合探活四个上游，用 Phase 3 的 /health 聚合 location（见 §9 Phase 3 任务 4）。
      test:
        - "CMD-SHELL"
        - "curl -fs http://127.0.0.1:${BAIHUA_NGINX_PORT:-80}/ || curl -fs -o /dev/null http://127.0.0.1:${BAIHUA_NGINX_PORT:-80}/ || exit 1"
      interval: 30s
      timeout: 5s
      retries: 3
      start_period: 10s
```

### 7.2 .env.example 补充

在 `docker/.env.example` 中追加：
```dotenv
# ────────────────────────────────────────────────
# Nginx 统一入口（可选，默认启用并监听 80）
# ────────────────────────────────────────────────

# Nginx 对外监听端口。
# 家庭宽带常用场景：
#   80     → 默认，浏览器输入 http://<ip> 即可
#   8080   → 运营商封 80 时用，浏览器输入 http://<ip>:8080
#   其他   → 配合上游反代（群晖/NPM）时用
BAIHUA_NGINX_PORT=80

# WebUI 路径前缀（空=挂根路径，推荐）。
# 如果你已经有其他网站占用 80 端口（如群晖 Web Station），
# 需要让上游反代把 /baihua/* 转到百花 Nginx 的某个端口，
# 这里填 /baihua，WebUI 内部的链接/静态资源自动适配。
# 花记移动端的 /mg/* 等前缀不受此变量影响（固定不变）。
BAIHUA_WEBUI_PREFIX=

# Blazor 静态资源强缓存开关（默认 true，开发调试可关）
BAIHUA_WEBUI_STATIC_CACHE=true

# 上传文件最大体积（影响知识库附件、头像上传等）
BAIHUA_NGINX_CLIENT_MAX_BODY_SIZE=100M
```

---

## 8. 安全风险 & 应对

| # | 风险 | 影响 | 应对 |
|---|------|------|------|
| R1 | 重写 Nginx 时再次引入 Vault/AI 直连（如 `/vault/*` → 8790） | **未授权下载所有知识库文件** | ① 模板中禁止写指向 8790/8791 的 location；② 文档中明确“除 Family 外，无人有权直连 Vault/AI 对外接口”；③ 新增 CI/脚本型用例：`curl -I http://ngx/vault/file/xxx`（应 404，因为该路径不再暴露）；`curl -I http://ngx/mg/file/xxx` 应经 Family 处理（无签名时依赖是否配置 `MobileAuth:SharedSecret`，配置后应 401） |
| R2 | `X-Forwarded-For` 伪造，绕过 Family 的 loopback 白名单（管理员 UI） | 非本机 IP 访问管理面板 | 现有 Family Program.cs 的 ForwardedHeadersOptions 已**仅信任 loopback**，Nginx 在 host 网络，请求来源 IP 对 Family 来说就是 127.0.0.1 → 转发后 X-Forwarded-For 的第一个真实客户端 IP 正确，不会信任客户端直送的头部 |
| R3 | 端口 80 被家庭路由器的 Web 管理面板占用 | Nginx 启动失败（Address already in use） | `BAIHUA_NGINX_PORT` 默认 80，用户可在 `.env` 改成 8000/8080 任意端口，文档给常见场景指引 |
| R4 | `BAIHUA_WEBUI_PREFIX` 非空时 WebUI 链接失效 | 静态 404、SignalR 连不上 | ① WebUI 的 `BasePath` 配置注入 + 已实现的 `UsePathBase` 严格测试；② Nginx `proxy_pass` 尾部 `/` 的前缀剥离在集成测试中单独覆盖；③ Blazor `<base href>` 渲染结果验证 |
| R5 | 容器崩溃时 Nginx 没起来，WebUI 也“失联” | 用户不知道还能 `http://ip:5177` 直达 | 文档首页醒目写入“直达端口号兜底地址”；Family 侧 `/api/onehop/status` 的直达地址能力迁移到 `/health` 或设备管理 API，让配对失败时移动端能做 fallback |

---

## 9. 实施计划（分 4 个 Phase，每阶段可独立交付/回滚）

### Phase 1：Nginx 静态配置 + 默认路径 + 安全修复 + 移动端 SDK 改造（核心价值交付）
**目标**：用户输入 `http://ip` 直接打开 WebUI；花记手动输入裸 IP 自动走 80 端口；修掉直连 Vault 漏洞。

| 任务 | 产出 | 预计工作量 |
|------|------|-----------|
| 1. 新增 `default.conf.template` + `family-proxy-headers.conf` | Nginx 配置模板（含 §5.2 所有 location） | 1h |
| 2. 重写 docker-compose.yml 的 nginx service（§7.1） + 补 `.env.example`（§7.2） | compose + env 模板 | 0.5h |
| 3. 移除现有 nginx.conf 中直连 Vault 的危险 location（/vault/ /api/search/ 等） | 配置安全审计通过 | 0.2h |
| 4. BaihuaSdk C# normalizeBaseUrl 默认端口 8788→80（§11.3.1） | HttpTransport.cs 改动 | 0.1h |
| 5. 花圃 Huapu 占位符更新（§11.3.1） | PairingContent.razor 改动 | 0.05h |
| 6. Kotlin baihua-sdk normalizeBaseUrl 改动（§11.3.1） | HttpTransport.kt 改动 | 0.1h |
| 7. Kotlin App PushWebSocketService 统一复用 SDK（§11.4） | 消除重复代码 | 0.1h |
| 8. ArkTS baihua_sdk normalizeBaseUrl + DEFAULT_SERVER_PORT 改动（§11.3.1） | HttpTransport.ets + AppConstants.ets | 0.1h |
| 9. ArkTS 默认端口常量更新（§11.3.1/11.3.4） | AppConstants.ets DEFAULT_SERVER_PORT 8788→80；**QRCodeService 的 httpPort+1 推导逻辑保持不变** | 0.1h |
| 10. 服务端 QR 码生成地址改为 Nginx 对外地址（§11.3.3） | ServerAddressService（GetQrCodeAddresses + 新增 PublicBaseUrl 配置） | 0.2h |
| 11. 手动冒烟：80 端口 → WebUI、移动端走 Nginx 配对同步 | 冒烟清单全 PASS | 0.5h |
| **验证用例** | | |
| - `curl http://ip/` → WebUI HTML（200） | | |
| - `curl http://ip/mg/vaults` → 视配置而定：配置了 `MobileAuth:SharedSecret` 时无签名 → 401；未配置时走 Family 公开白名单 → 200（返回 Vault 数据）。**两种情况下请求都必须经 Family 而非直连 Vault**（验证点：Family 日志出现转发记录） | | |
| - `curl http://ip/vault/file/xxx` → 404（直连路径已删除） | | |
| - 花圃手动输入 `192.168.x.x`（裸 IP）→ 补成 `http://192.168.x.x` → 走 Nginx 80 → 配对成功 | | |
| - 花记 Kotlin/ArkTS 同上验证 | | |
| - 扫码配对（新旧 QR 码各一份）→ 全部 PASS | | |
| - OpenClaw 流式对话 2 分钟不中断 | | |

### Phase 2：服务端 PathBase + WebUI 前缀可配（部署多样性）
**目标**：用户已有反代时，百花 Nginx 挂在 `/baihua/` 下也能工作。

| 任务 | 产出 |
|------|------|
| 1. 验证 WebUI `BasePath` 接线：compose 注入 `BasePath=${BAIHUA_WEBUI_PREFIX}` 环境变量（Program.cs 已支持 UsePathBase，无需改代码） | 支持前缀环境变量 |
| 2. 可选：Baihua.Family/AI/Vault 确认 ForwardedHeaders（已配置，仅信任 loopback） | 一致的头部转发 |
| 3. compose 中 webui 注入 `BasePath=${BAIHUA_WEBUI_PREFIX}` | 与 Nginx 前缀联动 |
| 4. 集成测试：`BAIHUA_WEBUI_PREFIX=/baihua` 时所有静态资源 200，SignalR 连上，登录页链接不 404 | 前缀冒烟 PASS |

### Phase 3：静态缓存 + 超时优化 + 访问日志
**目标**：性能 + 可观测性打磨。

| 任务 | 产出 |
|------|------|
| 1. 条件化注入静态缓存策略（`BAIHUA_WEBUI_STATIC_CACHE=false` 时不加 expires 头） | 开发/生产双模式 |
| 2. AI SSE 流式：`proxy_buffering off;` + `proxy_read_timeout 1h;` 在 `/api/ai/chat` location 精细化 | 长对话不中断 |
| 3. Nginx access/error log → `$BAIHUA_HOME/logs/nginx`，和现有 .NET 日志放同一根，`bh.ps1 logs nginx` 支持 | 与 `bh.ps1` 体验统一 |
| 4. 新增 `/health` 聚合 location：同时检查 Family/Vault/AI/WebUI 四个 `/health`，任一不健康返回 503（否则 200） | 上游负载均衡可探活 |

### Phase 4：文档 + bh.ps1 支持
**目标**：用户可一键拉起/停止 Nginx，无学习成本。

| 任务 | 产出 |
|------|------|
| 1. 在 `bh.ps1` 中新增：`bh.ps1 nginx [start|stop|restart|reload|logs]`（包装 `docker compose -f docker/docker-compose.yml ... nginx`） | 与现有 dev/status 风格统一 |
| 2. `docker/start.sh` / `stop.sh` 纳入 nginx 生命周期（可选，保留 profile） | 脚本行为一致 |
| 3. AGENTS.md：Nginx 端口 80 与子路径配置写入"部署"章节；移动端配对说明改为"推荐输入 http://ip 即可，无需加 :8788" | 用户文档更新 |
| 4. systemd 单元：新增 `baihua-nginx.service`（Requires= 四个 .NET 服务） | 裸机 systemd 用户可用 |

---

## 10. 风险与回滚

### 10.1 回滚策略（任何阶段不满意即可回滚）

因为 Nginx 是**可选入口层**（非破坏性），回滚非常简单：

```bash
# 1. 停 Nginx 容器
cd docker && docker compose stop nginx

# 2. 用户继续使用旧端口访问即可
#    WebUI:  http://ip:5177
#    移动端: http://ip:8788
```

所有 .NET 服务、Family 转发中间件**零改动**；花记 SDK 的默认端口改动（8788→80）在回滚后需一并还原（否则裸 IP 输入会连到 80 端口而非 8788），停 Nginx 即可回到当前状态。

### 10.2 灰度验证

- **先家庭内部使用 1 周**：所有家庭成员的手机/平板走 Nginx，配对/同步/聊天 100% 通过再推广文档
- **OpenClaw 长对话验证**：2 小时以上流式对话观察超时/断开情况
- **大文件同步验证**：1GB 以上知识库全量同步观察 Nginx `proxy_read_timeout`、`client_max_body_size` 是否足够
- **PathBase 验证**：切换到带前缀模式，至少验证 3 种浏览器 + 1 个移动浏览器

---

## 11. 花记移动端 SDK 改造（三端统一）

### 11.1 问题：默认端口从 8788 变为 80

当前花记移动端 SDK 的 `normalizeBaseUrl` 函数在用户输入裸 IP（无 `http://` 前缀、无端口号）时，自动补 `:8788`。Nginx 上线后默认端口变为 80，用户在配对页面输入 `192.168.1.5` 时，SDK 会补成 `http://192.168.1.5:8788` 而非 `http://192.168.1.5`（80 端口），导致连接失败。

**影响范围**：仅"手动输入"配对方式。**扫码配对不受影响**（QR 码中 URL 是完整地址，不经过 `normalizeBaseUrl`）。

### 11.2 需要修改的文件清单

三端 `normalizeBaseUrl` 逻辑完全对齐，需同步修改（行号已核对 2026-08-08）：

| 平台 | 文件 | 行号 | 当前代码 |
|------|------|------|---------|
| **C# (BaihuaSdk)** | [HttpTransport.cs](file:///c:/Users/lumin/src/baihuagu/libs/BaihuaSdk/src/Transport/HttpTransport.cs#L241) | 241 | `return $"http://{trimmed}:8788";` |
| **Kotlin (baihua-sdk)** | `baihua-sdk/.../transport/HttpTransport.kt` | 247-249 | `val portSuffix = if (hasPort) "" else ":8788"` |
| **ArkTS (baihua_sdk)** | `baihua_sdk/.../transport/HttpTransport.ets` | 238-239 | `` `http://${trimmed}${hasPort ? '' : ':8788'}` `` |

ArkTS entry 模块的常量：
| 平台 | 文件 | 行号 | 当前代码 |
|------|------|------|---------|
| **ArkTS (entry)** | `entry/.../utils/AppConstants.ets` | 19 | `export const DEFAULT_SERVER_PORT: number = 8788;` |
| **ArkTS (entry)** | `entry/.../utils/PathValidator.ets` | 19 | `` `http://${trimmed}:${DEFAULT_SERVER_PORT}` ``（**首版遗漏，已补充**） |

花圃 Huapu UI 占位符：
| 平台 | 文件 | 行号 | 当前代码 |
|------|------|------|---------|
| **C# (Huapu)** | [PairingContent.razor](file:///c:/Users/lumin/src/baihuagu/clients/Huapu/Pages/PairingContent.razor#L30) | 30 | `placeholder="例如: http://192.168.1.100:8788"` |

### 11.3 改造方案

**核心思路**：将默认端口从硬编码 `8788` 改为 `80`（HTTP 默认端口），当端口为 80 时不拼端口号（浏览器/HTTP 客户端默认行为）。

#### 11.3.1 normalizeBaseUrl 逻辑变更

**变更前**（三端一致）：
```
输入: "192.168.1.5"          → 输出: "http://192.168.1.5:8788"
输入: "192.168.1.5:8080"     → 输出: "http://192.168.1.5:8080"
输入: "http://192.168.1.5"   → 输出: "http://192.168.1.5"  (已有协议，不补端口)
```

**变更后**：
```
输入: "192.168.1.5"          → 输出: "http://192.168.1.5"        (默认 80，不拼端口号)
输入: "192.168.1.5:8080"    → 输出: "http://192.168.1.5:8080"  (用户显式指定端口)
输入: "http://192.168.1.5"  → 输出: "http://192.168.1.5"       (已有协议，不补端口)
```

**C# BaihuaSdk 改动**（[HttpTransport.cs:235-242](file:///c:/Users/lumin/src/baihuagu/libs/BaihuaSdk/src/Transport/HttpTransport.cs#L235-L242)）：
```csharp
// 改前
return $"http://{trimmed}:8788";

// 改后（默认 80 端口不拼端口号，与浏览器行为一致）
return $"http://{trimmed}";
```

**Kotlin baihua-sdk 改动**（HttpTransport.kt:247-249）：
```kotlin
// 改前
val portSuffix = if (hasPort) "" else ":8788"
"http://$trimmed$portSuffix".trimEnd('/')

// 改后
"http://$trimmed".trimEnd('/')
```

**ArkTS baihua_sdk 改动**（HttpTransport.ets:238-239）：
```typescript
// 改前
return `http://${trimmed}${hasPort ? '' : ':8788'}`.replace(/\/$/, '');

// 改后
return `http://${trimmed}`.replace(/\/$/, '');
```

**ArkTS entry 常量改动**（AppConstants.ets:19）：
```typescript
// 改前
export const DEFAULT_SERVER_PORT: number = 8788;

// 改后
export const DEFAULT_SERVER_PORT: number = 80;
```

同时需检查 ArkTS 中引用 `DEFAULT_SERVER_PORT` 的 5 处代码（已核对 2026-08-08）：
- `EnhancedPairingService.ets:184-186` — `extractPortFromUrl` 兜底返回值，需改为 80
- `QRCodeService.ets:87` — QR 码解析端口兜底，需改为 80（注意：其 `httpPort + 1` 的 OneHop 推导逻辑**保持不变**，见 §11.3.4）
- `ServerManager.ets:241,263` — `isServerAdded` / `addServerFromOneHop` 兜底拼接，需改为 80
- `PathValidator.ets:19` — 手动输入校验补端口，需改为 80（**首版遗漏，已补充**）

**花圃 Huapu 占位符改动**（[PairingContent.razor:30](file:///c:/Users/lumin/src/baihuagu/clients/Huapu/Pages/PairingContent.razor#L30)）：
```razor
<!-- 改前 -->
<input @bind="_manualUrl" placeholder="例如: http://192.168.1.100:8788" />

<!-- 改后 -->
<input @bind="_manualUrl" placeholder="例如: http://192.168.1.100" />
```

#### 11.3.2 向后兼容性

| 场景 | 变更前行为 | 变更后行为 | 兼容？ |
|------|-----------|-----------|--------|
| 已配对设备（存储的 URL 含 :8788） | 连 8788 端口 | 连 8788 端口（存储的 URL 不变，不经过 normalizeBaseUrl） | ✅ 不影响 |
| 扫码配对（QR 码含完整 URL） | 直接用 QR 中的 URL | 直接用 QR 中的 URL | ✅ 不影响 |
| 手动输入裸 IP | 补 :8788 | 补 http://（不拼端口，默认 80） | ⚠️ **行为变化** |
| 手动输入 `ip:8788` | 补 http://，保留 :8788 | 补 http://，保留 :8788 | ✅ 不影响 |
| 手动输入 `http://ip:8788` | 原样返回 | 原样返回 | ✅ 不影响 |

**唯一的行为变化**：用户手动输入裸 IP 时，从连 8788 变为连 80。这正是 Nginx 上线后的目标行为。

**已配对设备无需重新配对**：存储在 `ServerConfig` 中的 URL（如 `http://192.168.1.5:8788`）不会被二次 normalize，直接用于 HTTP 请求。用户如果想走 Nginx，需要手动删除旧服务器重新配对（或在服务器管理中修改地址）。

#### 11.3.3 配对二维码内容变更

百花服务端生成配对二维码时，`baseUrl` 字段应从 `http://ip:8788` 改为 `http://ip`（无端口号，默认 80）。

**已核实（2026-08-08）**：QR 地址来源是 `Baihua.Core/ServerAddressService.GetQrCodeAddresses()`：
- 配置了 `Domain`（广域网 HTTPS）→ 返回 `https://{domain}`
- 否则局域网 → 返回 `http://{localIp}:{GetHttpPort()}`，其中 `GetHttpPort()` 从 `Kestrel:Endpoints:Http:Url` 配置读取，兜底 8788
- 兜底分支硬编码 `http://{localIp}:8788`（L332）

调用链：`PairingController.Pairing.cs` → `GetQrCodeAddresses()` → `PairingService.GenerateQRCodeContent(baseUrl: url)`（PairingDtos 中 `baseUrl` 字段）。

**改造方案**：在 `ServerAddressService` 增加配置项 `Baihua:PublicBaseUrl`（或 `ServerAddress:PublicBaseUrl`）：
- 非空 → 直接用（如 `http://192.168.1.5` 或 `https://mydomain.com`）
- 为空 → 保持现状自动探测（兼容未启用 Nginx 的部署）

```csharp
// ServerAddressService.GetQrCodeAddresses() 改造示意
var publicBase = _configuration["Baihua:PublicBaseUrl"];
if (!string.IsNullOrWhiteSpace(publicBase))
    return (publicBase.TrimEnd('/'), hostName);
// ...原有 Domain / 局域网自动探测逻辑
```

**注意**：OneHop 端口从 QR 地址推导（`uri.Port + 1`），所以 `PublicBaseUrl` 里的端口会直接决定 OneHop 端口——配置时务必让该 URL 的端口与 Nginx 监听端口一致（默认 80 → OneHop 81）。

#### 11.3.4 OneHop TCP 注册端口（ArkTS 特有）——**保持动态推导，勿硬编码**

ArkTS 的 `QRCodeService.ets:87-89` 用 `httpPort + 1` 推导 OneHop TCP 注册端口：
```typescript
const httpPort = portMatch ? parseInt(portMatch[1]) : DEFAULT_SERVER_PORT;  // 默认 8788
const oneHopPort = httpPort + 1;  // 8789
ServerRegistrationHelper.sendOneHopRegistration(serverIp, oneHopPort);
```

**服务端对应逻辑（已核实 `Baihua.Core/OneHopService.cs` L283-300）**：
```csharp
private int GetAvailablePort()
{
    int oneHopPort = 8792;   // 默认兜底
    var (httpUrl, _) = _serverAddressService.GetQrCodeAddresses();
    if (!string.IsNullOrEmpty(httpUrl) && Uri.TryCreate(httpUrl, UriKind.Absolute, out var uri))
        oneHopPort = uri.Port + 1;   // ← 从 QR 码地址端口推导，与 ArkTS 端镜像
    ...
}
```

**关键结论（修订首版错误）**：
1. 服务端 OneHop 端口**不是固定 8789**——它从 `GetQrCodeAddresses()` 的 QR 地址端口推导（`uri.Port + 1`），QR 地址端口来自 `GetHttpPort()`（默认 8788，可被 Kestrel 配置覆盖）
2. **ArkTS 端的 `httpPort + 1` 与服务端是同一套推导逻辑，天然一致**。当 Nginx 上线、QR 地址变为 `http://ip`（80）时，服务端 OneHop = 81，ArkTS 端从 QR 解析 httpPort=80 → oneHopPort=81，**仍然一致**，无需修改
3. 首版建议“改为固定 8789”是**错误方案**：QR 地址端口一旦变化（80/8080/自定义），固定 8789 会让 ArkTS 端 OneHop 注册打到错误端口，配对失败

**唯一需要同步的**：`DEFAULT_SERVER_PORT` 从 8788 改为 80 后，ArkTS 端在“QR 码无端口”场景下推导 oneHopPort = 81——与服务端（QR 地址无端口 → uri.Port=80 → OneHop=81）仍一致。**两端同时改即可，无需固定端口。**

**附带发现**：`OneHopManager.cs` L308/317 有 `ExtractPortFromUrl` 兜底返回 8788，属服务端内部逻辑（URL 解析失败时），与移动端无直接关系，但改端口时建议一并从配置读取。

> **✅ 第三轮定稿（2026-08-08）**：本节基于“OneHop TCP 通道继续存在”的前提，**已作废**。项目未上线、不做兼容，OneHop 组件整体删除：TCP 监听删除、`/mg/onehop/register-device` 改名 `/mg/register-device`、OneHopService/OneHopManager/OneHopController/三端 contract 全部删除（见 [ONEHOP_SIMPLIFICATION_PLAN.md](https://github.com/luminsw/project-manager/blob/master/docs/ONEHOP_SIMPLIFICATION_PLAN.md)）。对 Nginx 方案影响：**无**——Nginx 只代理 HTTP（`/mg/*`、`/api/*`、`/ws/devices`），改名后路径表更新为 `/mg/register-device` 即可，且少一个 TCP 端口更简洁

### 11.4 Kotlin App 层 PushWebSocketService 不一致修复

Kotlin App 层 `app/.../sync/PushWebSocketService.kt:78` 有一个**私有** `normalizeBaseUrl`，与 SDK 的行为不一致——它只补 scheme 不补端口：

```kotlin
// App 层私有实现（不补端口）
private fun normalizeBaseUrl(url: String): String {
    var base = url.trimEnd('/')
    if (!base.startsWith("http://") && !base.startsWith("https://")) {
        base = "http://$base"
    }
    return base
}
```

**巧合**：这个“不一致”在 Nginx 默认 80 端口场景下反而变成了正确行为（不补端口 = 默认 80）。建议**统一改为复用 SDK 的 `HttpTransport.normalizeBaseUrl`**，消除重复代码和潜在分歧（注意：SDK 版改成默认 80 后，两者行为完全一致，此时是否复用仅是代码整洁问题，不再是行为问题）。

### 11.5 移动端改造验证用例

| 用例 | 验证步骤 | 期望结果 |
|------|---------|---------|
| 手动输入裸 IP | 输入 `192.168.1.5` | 补成 `http://192.168.1.5`，连接 Nginx 80 端口，配对成功 |
| 手动输入带端口 | 输入 `192.168.1.5:8788` | 补成 `http://192.168.1.5:8788`，直连 Family，配对成功（兼容旧模式） |
| 扫码配对（新 QR 码） | 扫描 baseUrl=`http://192.168.1.5` 的 QR 码 | 直接用 QR 中的 URL，配对成功 |
| 扫码配对（旧 QR 码） | 扫描 baseUrl=`http://192.168.1.5:8788` 的 QR 码 | 直接用 QR 中的 URL，配对成功（向后兼容） |
| 已配对设备（旧地址） | 之前配对的 URL 是 `http://ip:8788` | 继续直连 8788，正常工作（不经过 normalizeBaseUrl） |
| WebSocket 推送 | 配对后收到推送消息 | `ws://ip/ws/devices` 正常连接 |
| AI 对话流式 | OpenClaw 流式聊天 | SSE（`/api/ai/chat/stream`）通过 Nginx 正常传输，不中断 |
| 知识库同步 | 全量同步知识库 | 大文件通过 Nginx 下载完成，无超时 |

---

## 12. 与代码事实的核对记录（2026-08-08 第二轮）

首版文档基于对架构的推演，本版逐项对照代码核实，以下差异已修正：

| # | 首版假设 | 代码事实 | 影响与修正 |
|---|---------|---------|-----------|
| C1 | 移动端 WebSocket 路径 `/ws/push` | SDK `PushWebSocketService.cs` 实际用 **`/ws/devices`**；轮询兜底 `/mg/devices/push-pending` | §4.1/§4.3/§5.2/§11.5 全部修正为 `/ws/devices`。**若按首版配置，推送 WebSocket 全部 404** |
| C2 | `/api/discovery` 是移动端发现端点 | 不存在此路由；实际是 **`/api/onehop/*`**（status/devices/discovery/start/stop），SDK 也不调用它 | 从路径表移除；移动端发现靠 mDNS + OneHop，HTTP 入口是 `/api/onehop/status` |
| C3 | WebUI 未实现 PathBase，“需新增 UsePathBase 代码” | `Baihua.Web/Program.cs` ~L368 **已有** `UsePathBase`（读配置 `BasePath`，默认 `/`），`appsettings.json` 已有 `"BasePath": "/"` | §6.1 改为“仅需接线”：compose 注入 `BasePath` 环境变量即可，无需改代码 |
| C4 | OneHop 服务端端口固定 8789 | `OneHopService.GetAvailablePort()`：默认 **8792**，且从 QR 地址 `uri.Port + 1` 推导；`OneHopManager` 兜底 8788 | §11.3.4 全面重写：**保持动态推导，勿硬编码**。首版“固定 8789”会破坏配对 |
| C5 | Family hub 笼统 `/hubs/*` | 实际：`/hubs/task-progress`、`/hubs/devices`（Family）；`/hubs/status`（WebUI） | §4.1 精确列出；Nginx 最长前缀匹配天然正确处理，但文档需准确 |
| C6 | `/pair` 仅一个入口 | `PairController` 同时注册 `/pair`、`/vault/pair`、`/mg/pair`（POST）+ `/pair/code`、`/pair/code/refresh`（GET/POST） | §4.1/§5.2 补充 `/pair/code`、`/vault/pair` 精确 location |
| C7 | AI 不直接对外 | AI 服务自身有公开 `/api/ai/chat/completion|stream`（Program.cs 公开白名单） | 措辞修正：移动端**必须**经 Family 代理（HMAC），但 AI 服务确实暴露端点——Nginx 配置仍不应直连 AI |
| C8 | 三端 normalizeBaseUrl 行号 | C# 241 ✅；Kotlin 247-249（非 248）；ArkTS 238-239（非 239） | §11.2 行号修正 |
| C9 | ArkTS 引用 DEFAULT_SERVER_PORT 4 处 | 实际 **5 处**：EnhancedPairingService 184-186、QRCodeService 87、ServerManager 241/263、**PathValidator 19（首版漏）** | §11.2 补充 PathValidator.ets |
| C10 | Nginx healthcheck 探 `/health` | `/health` 是上游 .NET 端点（Family/AI/Vault 有，nginx 自身无） | §7.1 healthcheck 改为探测 nginx 自身根路径；上游聚合探活归 Phase 3 |
| C11 | Family/AI/Vault ForwardedHeaders 需补 | 三个服务**均已配置**（仅信任 loopback） | §6.2 改为“现有配置不用改”+ bridge 网络切换警示 |
| C12 | 验证用例假设 `/mg/vaults` 无签名必 401 | HMAC 签名中间件**条件启用**（Program.cs L84：仅配置 `MobileAuth:SharedSecret` 时验证；L456-540：未配置时跳过）。无签名时的防线是访问控制中间件的公开白名单（/mg/vaults 在列） | Phase 1 验证用例改为“视配置而定”，强调验证点是“经 Family 而非直连 Vault” |
| C13 | 首版/二版假设 OneHop TCP 通道继续存在（§11.3.4 端口推导） | 移动端已删自动发现；扫码注册已由 HTTP（`/mg/onehop/register-device`）完整承担；TCP 只做重叠的上线通知（失败无害）；三端 OneHop contract 为死代码/仅旧传输使用 | **第三轮定稿：项目未上线不做兼容，OneHop 组件与命名整体删除**（TCP 监听、OneHopController/Manager/Service/DTO/Adapter、`/onehop/*` 路径改名），详见 ONEHOP_SIMPLIFICATION_PLAN.md；§11.3.4 作废 |

### 核对过的代码位置（证据）

- `services/Baihua.Family/Program.cs`：L408-418（ForwardedHeaders）、L520-580（访问控制白名单）、L589-670（Vault 转发）、L680-740（AI 转发）、L842-849（WebSocket + hubs 映射）
- `services/Baihua.Web/Program.cs`：L368（UsePathBase）、L406（MapHub status）
- `services/Baihua.AI/Program.cs`：L203-209（ForwardedHeaders）、L225（HealthChecks）、L241（公开白名单）
- `services/Baihua.Vault/Program.cs`：L233-239（ForwardedHeaders）
- `services/Baihua.Core/OneHopService.cs`：L283-300（GetAvailablePort 动态端口）
- `services/Baihua.Core/OneHopManager.cs`：L308/317（ExtractPortFromUrl 兜底 8788）
- `services/Baihua.Core/ServerAddressService.cs`：L300-334（GetQrCodeAddresses）、L339-349（GetHttpPort）
- `libs/BaihuaSdk/src/Transport/HttpTransport.cs`：L241（normalizeBaseUrl）
- `libs/BaihuaSdk/src/Push/PushWebSocketService.cs`：L68/219（`/ws/devices`）
- `libs/BaihuaSdk/src/Push/PushPollingServiceImpl.cs`：L42（`/mg/devices/push-pending`）
- `services/Baihua.Family/Controllers/Core/PairController.cs`：L35-81（三份配对路由）
- `services/Baihua.Family/Controllers/Core/PairingController.Pairing.cs`：L11（GetQrCodeAddresses 调用链）
- Kotlin `baihua-sdk/.../HttpTransport.kt`：L247-249；`app/.../PushWebSocketService.kt`：L78
- ArkTS `baihua_sdk/.../HttpTransport.ets`：L238-239；`entry/.../AppConstants.ets`：L19；`PathValidator.ets`：L19；`QRCodeService.ets`：L87-89；`ServerManager.ets`：L241/263

---

## 13. 修订日志

| 版本 | 日期 | 内容 |
|------|------|------|
| v1 | 2026-08-08 | 首次设计评审（状态 ✅） |
| v2 | 2026-08-08 | 代码核对修订：修正 `/ws/devices`、`/api/onehop/*`、UsePathBase 已有、OneHop 动态端口（保留推导勿硬编码）、hub 精确路径、PathValidator 遗漏、healthcheck、ForwardedHeaders 已配；补充 §12 核对记录与证据位置 |
| v3 | 2026-08-08 | OneHop 精简定稿：项目未上线不做兼容，OneHop 组件与命名整体删除（TCP 监听、OneHopController/Manager/Service、`/mg/onehop/` 路径改名 `/mg/register-device`），详见 ONEHOP_SIMPLIFICATION_PLAN.md；对 Nginx 方案无影响 |
