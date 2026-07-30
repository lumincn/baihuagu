# 花圃 ↔ 百花 通信流程优化方案

> 基于对 C# SDK、Kotlin SDK、ArkTS SDK 三端完整分析，提出统一优化方案。

## 一、三端现状对比

| 特性 | C# (BaihuaSdk) | Kotlin (Android) | ArkTS (HarmonyOS) |
|------|---------------|-------------------|---------------------|
| 授权等待方式 | WebSocket ↔ 轮询双模式 | WebSocket ↔ 轮询双模式 | WebSocket ↔ 轮询双模式 |
| 轮询间隔 | 3s | 3s | 3s |
| 轮询端点 | `POST /mg/onehop/register-device` | 同 | 同 |
| WebSocket 实现 | `PushWebSocketService` | 同 | `WebSocketPushService` |
| WebSocket 重连 | 最多 10 次，5s 起递增 | 同 | 同 |
| 轻量密钥查询 | ✅ `GET /mg/auth/config` | ❌ | ❌ |
| 密钥恢复策略 | AuthConfig > SecureStore > registerDevice | SecureStore > registerDevice | 同 Kotlin |
| mDNS 发现 | ❌ 客户端无 | 未知 | ❌ |

## 二、已完成的优化（C# 侧）

### ✅ 2.1 去掉轮询，纯 WebSocket

`AuthorizationWatcher.cs` 已移除所有轮询逻辑。授权等待流程简化为：

```
registerDevice (立即检查)
    ↓ 未授权
WebSocket 连接 + 监听 Authorized 推送
    ↓
收到推送 → 再次 registerDevice → 获取 sharedSecret
    ↓ 超时（默认 2 分钟）
返回失败
```

**依赖**：`PushWebSocketService` 自带重连机制（最多 10 次），无需额外兜底。

### ✅ 2.2 实现 `/mg/auth/config` 轻量密钥查询

详见 `services/Baihua.Family/Controllers/Core/AuthController.cs`。

### ✅ 2.3 SyncContent 密钥恢复优先用 `GetAuthConfigAsync`

`SyncContent.razor` 启动时密钥恢复顺序：
1. 内存 `Signer` → 2. `SecureStore` → 3. `GetAuthConfigAsync`（轻量） → 4. `registerDevice`（完整注册）

## 三、待同步优化的部分

### 3.1 Kotlin SDK：实现 `getAuthConfigAsync`

**当前状态**：Kotlin SDK 已有 `AuthConfigRequest`/`AuthConfigResponse` 数据类，但 `getAuthConfigAsync()` 抛出 `NotSupportedException`。

**方案**：参照 C# SDK 的 `PairingServiceImpl.GetAuthConfigAsync()` 实现，通过 `HttpTransport` 发送 `POST /mg/auth/config`，返回 `{ success, sharedSecret, message }`。

**文件**：
- `kotlin/baihua-sdk/src/main/kotlin/com/baihua/sdk/pairing/PairingServiceImpl.kt`
- `kotlin/baihua-sdk/src/main/kotlin/com/baihua/sdk/contract/Pairing.kt`（补充 `success` 字段）

### 3.2 ArkTS SDK：实现 `getAuthConfigAsync`

**当前状态**：ArkTS SDK 无此接口。

**方案**：
1. 在 `IPairingService.ets` 中新增方法声明
2. 在 `PairingServiceImpl.ets` 中实现
3. 在 `Pairing.ets` 中补充 `success` 字段到 `AuthConfigResponse`

**文件**：
- `baihua_sdk/src/main/ets/pairing/IPairingService.ets`
- `baihua_sdk/src/main/ets/pairing/PairingServiceImpl.ets`
- `baihua_sdk/src/main/ets/contract/Pairing.ets`

### 3.3 三端统一去掉轮询

| 平台 | 需要修改的文件 | 操作 |
|------|--------------|------|
| **C#** | `AuthorizationWatcher.cs` | ✅ 已完成 |
| **Kotlin** | `AuthorizationWatcher.kt` 或相应等待授权逻辑 | 移除轮询，纯 WebSocket + 超时 |
| **ArkTS** | `AuthorizationWatcher.ets` | 移除 `waitForAuthorizationViaPolling()`，WebSocket 超时后直接失败 |

## 四、统一后的标准流程

```
┌──────────────────────────────────────────────────────┐
│              三端统一通信流程                            │
├──────────────────────────────────────────────────────┤
│                                                      │
│  1. 发现                                             │
│     ├─ QR 扫码 / 手动输入 URL                          │
│     └─ ParseQrCode() → GetServerAddresses()           │
│                                                      │
│  2. 注册 + 密钥获取（首次）                             │
│     ├─ POST /mg/onehop/register-device                │
│     │   { deviceId, deviceName, deviceType }          │
│     ├─ 已授权 → 返回 { authorized:true, sharedSecret } │
│     └─ 未授权 → 返回 { authorized:false, requestId }   │
│                                                      │
│  3. 授权等待（纯 WebSocket，无轮询）                    │
│     ├─ ws://server/ws/devices?deviceName=xxx          │
│     ├─ 监听 { action: "authorized" } 推送              │
│     ├─ 推送到达 → POST /mg/onehop/register-device     │
│     │             获取 sharedSecret                   │
│     ├─ WebSocket 断开 → PushService 自动重连（最多10次） │
│     └─ 超时 2 分钟 → 返回失败，提示用户检查 WebUI        │
│                                                      │
│  4. 密钥恢复（后续启动）                                │
│     ├─ 内存 Signer.HasServerSecret()                  │
│     ├─ SecureStore 持久化恢复                          │
│     └─ POST /mg/auth/config（轻量查询，已授权设备）      │
│                                                      │
│  5. 知识库操作（需 HMAC 签名）                          │
│     ├─ GET /mg/vaults         （知识库列表）           │
│     ├─ GET /mg/manifest        （同步清单）            │
│     └─ GET /mg/file            （下载文件）            │
│                                                      │
│  6. 推送通知                                          │
│     └─ WebSocket { action: "SyncRequest", ... }       │
│                                                      │
└──────────────────────────────────────────────────────┘
```

## 五、进一步优化建议（低优先级）

### 5.1 客户端 mDNS 发现

服务端已有 `MDnsService` 宣告 `_baihua._tcp` 服务。三端客户端均可添加 mDNS 监听来自动发现局域网内的百花服务器。

### 5.2 SDK 层统一密钥持久化

当前三端 SDK 的 `RequestSigner` 都是纯内存存储，各自在 UI 层手动调用 `SecureStore`（或 Android `SharedPreferences`/鸿蒙 `Preferences`）。建议 SDK 提供 `RequestSigner.PersistAsync()` 统一接口。

### 5.3 同步断点续传

当前 `SyncVaultAsync` 逐个下载文件，无断点续传。可引入 cursor/mtime 机制跳过已同步文件。

## 六、迁移步骤

| 步骤 | 内容 | 平台 |
|------|------|------|
| 1 | C# AuthorizationWatcher 去掉轮询 | ✅ 已完成 |
| 2 | C# `/mg/auth/config` 端点 + SDK | ✅ 已完成 |
| 3 | Kotlin SDK 实现 `getAuthConfigAsync` | 待实施 |
| 4 | ArkTS SDK 实现 `getAuthConfigAsync` | 待实施 |
| 5 | Kotlin 去掉授权轮询 | 待实施 |
| 6 | ArkTS 去掉授权轮询 | 待实施 |
| 7 | 三端联调验证 | 待实施 |
