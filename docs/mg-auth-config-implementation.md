# `/mg/auth/config` 接口实现需求
> 状态：✅ 已实现（AuthController.cs 提供 /mg/auth/config；Kotlin/ArkTS SDK 均已实现 getAuthConfigAsync）
> 本文档为需求与实现记录。

## 1. 背景

### 1.1 现状

密钥获取流程目前只有 `/mg/onehop/register-device` 一个端点，它**身兼两职**：

| 职责 | 说明 |
|------|------|
| **注册** | 首次设备发现时创建设备记录、待授权请求 |
| **查询密钥** | 已授权设备返回 `sharedSecret` |

这在首次注册时是合理的，但对于**已授权设备的后续请求**，`registerDevice` 做了太多不必要的事情：

- 每次都会查授权表、检查设备状态、可能更新设备信息
- 语义上，"注册"表达的是首次接入，不适合作为日常密钥获取的入口

### 1.2 存在的问题

**安卓端**之前直接 `POST /mg/auth/config`（`fetchSecretForServer`），但该端点**在所有服务端均未实现**，各端 SDK 均抛出 `NotSupportedException`：

| 平台 | 项目位置 | 状态 |
|------|----------|------|
| **百花**（家庭版服务端） | `baihua/services/Baihua.Family/` | ❌ 不存在 |

| **花圃**（MAUI 移动端） | `baihua/clients/MobileApp.Maui/` | ❌ 不存在，未引用 |
| 安卓 SDK (Kotlin) | `baihua/libs/BaihuaSdk/` 或 `kotlin/baihua-sdk/` | `throw NotSupportedException("[后端未实现 /mg/auth/config]")` |
| 鸿蒙 SDK (ArkTS) | `arkts/baihua_sdk/` | ❌ 不存在，未引用 |

当前安卓端已改为通过 `registerDevice` 获取密钥，但语义不合适，且每次获取都走了完整注册流程。

### 1.3 目标

实现 `/mg/auth/config` 作为**已授权设备轻量查询配置的端点**，并在各端正确使用。

---

## 2. 设计意图

### 2.1 两个端点的职责划分

| | `/mg/onehop/register-device` | `/mg/auth/config` |
|---|---|---|
| **定位** | 设备注册 | **授权后配置查询** |
| **使用场景** | 首次发现、扫码添加、重连 | 已授权设备获取/刷新密钥 |
| **是否要求已授权** | 否（三种状态都处理） | **是**（未授权返回 401） |
| **副作用** | 创建设备记录、配对请求、更新 IP | **无副作用**（只读） |
| **返回信息** | sharedSecret + accessToken + 设备信息 | **仅 sharedSecret**（轻量） |
| **调用频率** | 低（仅首次/重连时） | 中（每次启动、密钥丢失时） |

### 2.2 流程图

```
首次启动 / 扫码 / 发现
    │
    ▼
POST /mg/onehop/register-device     ← 全量注册
    │
    ├─ 已授权 → sharedSecret + accessToken
    │              │
    │              ▼
    │         存储密钥，后续用 /mg/auth/config 获取
    │
    └─ 未授权 → authorized: false
                   │
                   ▼
              用户在 WebUI 批准设备
                   │
                   ▼
              POST /mg/auth/config    ← 轻量查询密钥
                   │
                   ▼
              { sharedSecret: "sec-xxx" }
```

---

## 3. API 契约

### 3.1 端点

```
POST /mg/auth/config
```

### 3.2 请求

```json
{
  "deviceId": "android_xxxxx",
  "deviceName": "MTN-AN80"
}
```

| 字段 | 类型 | 必填 | 说明 |
|------|------|------|------|
| `deviceId` | string | 是 | 设备唯一标识（Android ID / HarmonyOS DeviceId） |
| `deviceName` | string | 否 | 设备显示名，为空时服务端使用默认值 |

### 3.3 响应

**成功 (200)**：

```json
{
  "success": true,
  "sharedSecret": "sec-a1b2c3d4e5f6",
  "message": "ok"
}
```

| 字段 | 类型 | 说明 |
|------|------|------|
| `success` | boolean | `true` |
| `sharedSecret` | string | 服务端当前的共享密钥 |
| `message` | string | 本地化消息 |

**未授权 (401)**：

```json
{
  "success": false,
  "error": "设备未授权",
  "message": "请先在管理后台的「设备管理」中授权该设备。"
}
```

**DeviceId 为空 (400)**：

```json
{
  "success": false,
  "error": "deviceId 不能为空"
}
```

**服务端错误 (500)**：

```json
{
  "success": false,
  "error": "获取配置失败",
  "message": "异常详情"
}
```

### 3.4 数据模型

#### Kotlin SDK (`baihua-sdk/.../contract/Pairing.kt`)

已定义，无需修改：

```kotlin
data class AuthConfigRequest(
    @SerializedName("deviceId") val deviceId: String = "",
    @SerializedName("deviceName") val deviceName: String? = null
)

data class AuthConfigResponse(
    @SerializedName("sharedSecret") val sharedSecret: String? = null,
    @SerializedName("message") val message: String? = null
)
```

⚠️ **需要补充 `success` 字段**：

```kotlin
data class AuthConfigResponse(
    val success: Boolean = false,
    @SerializedName("sharedSecret") val sharedSecret: String? = null,
    @SerializedName("message") val message: String? = null
)
```

#### ArkTS SDK (`baihua_sdk/src/main/ets/contract/Pairing.ets`)

需要新增：

```typescript
export interface AuthConfigRequest {
    deviceId: string;
    deviceName?: string;
}

export interface AuthConfigResponse {
    success: boolean;
    sharedSecret?: string;
    message?: string;
}
```

#### C# 服务端 (`Baihua.Core` / MobileContract)

需要定义响应 DTO：

```csharp
public class AuthConfigRequest
{
    public string DeviceId { get; set; } = string.Empty;
    public string? DeviceName { get; set; }
}

public class AuthConfigResponse
{
    public bool Success { get; set; }
    public string? SharedSecret { get; set; }
    public string? Message { get; set; }
}
```

---

## 4. 实现计划

### 4.1 百花后端 (Baihua.Family)

**文件**: `services/Baihua.Family/Controllers/Common/AuthController.cs`（新建）

```csharp
using Microsoft.AspNetCore.Mvc;
using Baihua.Core.Security;

[ApiController]
[Route("mg/auth")]
public class AuthController : ControllerBase
{
    private readonly IDeviceService _deviceService;
    private readonly RequestSignatureService _signatureService;
    private readonly ILocalizationService _loc;

    [HttpPost("config")]
    public ActionResult GetAuthConfig([FromBody] AuthConfigRequest request)
    {
        if (string.IsNullOrEmpty(request?.DeviceId))
            return BadRequest(new AuthConfigResponse
            {
                Success = false,
                Message = _loc["AuthConfig_DeviceIdRequired"]
            });

        var authorizedDevice = _deviceService.GetAuthorizedDeviceById(request.DeviceId);
        if (authorizedDevice == null)
            return Unauthorized(new AuthConfigResponse
            {
                Success = false,
                Message = _loc["AuthConfig_DeviceNotAuthorized"]
            });

        return Ok(new AuthConfigResponse
        {
            Success = true,
            SharedSecret = _signatureService.GetSharedSecret(),
            Message = _loc["AuthConfig_Success"]
        });
    }
}
```

**路由注册**: 确保 `/mg/auth/config` 在 `publicPaths` 中（无需 HMAC 签名），且不在 `mobileApiPaths` 中。

**授权要求**: 通过 `deviceId` 校验设备是否已授权（逻辑与 `register-device` 的已授权分支一致，但不创建任何记录）。

### 4.2 安卓 SDK (Kotlin)

**文件**: `baihua-sdk/.../pairing/PairingServiceImpl.kt`

将存根替换为真实实现：

```kotlin
override suspend fun getAuthConfigAsync(request: AuthConfigRequest): AuthConfigResponse {
    val transport = requireTransport()
    return transport.postJsonAsync("/mg/auth/config", request).data
        ?: throw IllegalStateException("Get auth config returned empty response")
}
```

### 4.3 安卓端 App (Kotlin)

**文件**: `app/.../ui/MainViewModel.kt`

将 `fetchVaultsForServer` 中的密钥获取从 `registerDevice` 改为 `getAuthConfigAsync`：

```kotlin
// 已有密钥但失效时（401），先尝试轻量查询配置
if (!RequestSigner.hasServerSecret(server.httpUrl)) {
    // 首次：用 SDK 的 getAuthConfigAsync 获取密钥
    val pairingService = PairingServiceImpl(httpClient, RequestSigner, _deviceId, deviceName)
    pairingService.initialize(server.httpUrl)
    val config = pairingService.getAuthConfigAsync(AuthConfigRequest(deviceId = _deviceId))
    if (config.success && !config.sharedSecret.isNullOrEmpty()) {
        RequestSigner.setServerSecret(server.httpUrl, config.sharedSecret)
        RequestSigner.saveToContext(appContext)
    } else {
        // 回退到 registerDevice（可能是未授权设备）
        deviceRegistration.registerDevice(server.httpUrl)
    }
}
```

**说明**：
- 首次获取密钥：先试 `getAuthConfigAsync`（轻量），失败则回退 `registerDevice`（完整注册）
- 密钥失效时（401 重试逻辑中）：优先 `getAuthConfigAsync`，失败回退 `registerDevice`
- `fetchSecretForServer` 方法可以删除（已完全被替代）

### 4.4 鸿蒙 SDK (ArkTS)

**文件**: `baihua_sdk/src/main/ets/pairing/IPairingService.ets`

新增接口方法：

```typescript
export interface IPairingService {
    // ... 已有方法
    getAuthConfigAsync(request: AuthConfigRequest): Promise<AuthConfigResponse>;
}
```

**文件**: `baihua_sdk/src/main/ets/pairing/PairingServiceImpl.ets`

实现：

```typescript
async getAuthConfigAsync(request: AuthConfigRequest): Promise<AuthConfigResponse> {
    const transport = this.requireTransport();
    const response = await transport.postJsonAsync<AuthConfigResponse>('/mg/auth/config', request);
    if (!response.isSuccess || !response.data) {
        throw new Error(`获取配置失败 (HTTP ${response.statusCode}): ${response.errorMessage}`);
    }
    return response.data;
}
```

### 4.5 花圃 MAUI 端 (`MobileApp.Maui`)

**文件**: `clients/MobileApp.Maui/` 中调用 SDK `getAuthConfigAsync` 的位置

花圃作为 MAUI 移动端实验与验证工具，应通过 `BaihuaSdk`（`libs/BaihuaSdk/`）调用 `IPairingService.getAuthConfigAsync()`，不直接构造 HTTP 请求。密钥获取流程与安卓端一致：优先 `getAuthConfigAsync`，失败回退 `registerDevice`。

### 4.6 鸿蒙端 App (ArkTS)

**文件**: `entry/src/main/ets/utils/ServerRegistrationHelper.ets`

在 `registerViaHttp` 成功后，后续密钥获取改用 `getAuthConfigAsync`。在 `SyncService` 的 `onUnauthorized` 回调中，优先使用轻量配置查询而非完整注册。

---

## 5. publicPaths 配置

确保 `/mg/auth/config` 在两端的 `publicPaths` 中（无需 IP 循环回检查），且**不**在 `mobileApiPaths` 中（无需 HMAC 签名——因为此时客户端可能还没有密钥）：

### 百花 (Baihua.Family `Program.cs`)

```csharp
var publicPaths = new[]
{
    // ... 已有路径 ...
    "/mg/auth/config",   // ← 新增
};
```

---

## 6. 安全注意事项

| 风险 | 缓解措施 |
|------|----------|
| 未授权设备枚举 deviceId | 401 不区分"deviceId 不存在"和"未授权"，统一返回"设备未授权" |
| 中间人窃取 sharedSecret | 生产环境应使用 HTTPS；LAN 环境通过局域网物理安全保证 |
| 重放攻击 | 请求不涉及状态变更（只读），重放风险低 |

---

## 7. 迁移步骤

| 步骤 | 内容 | 涉及 |
|------|------|------|
| 1 | 百花后端实现 `/mg/auth/config` | `Baihua.Family` |
| 2 | 安卓 SDK 替换存根为真实实现 | `baihua-sdk` (Kotlin) |
| 3 | 鸿蒙 SDK 新增接口和实现 | `baihua-sdk` (ArkTS) |
| 4 | 安卓 App 改用 `getAuthConfigAsync` 替代 `registerDevice` | `MainViewModel.kt` |
| 5 | 鸿蒙 App 改用 `getAuthConfigAsync` | `ServerRegistrationHelper.ets` |
| 6 | 花圃 MAUI 端使用 SDK `getAuthConfigAsync` | `MobileApp.Maui` |
| 7 | 删除安卓 `fetchSecretForServer` 和 `fetchSharedSecret` 中的 `/mg/auth/config` 裸调用 | `MainViewModel.kt` |
| 8 | 确保 publicPaths 配置正确 | `Program.cs` (Baihua.Family) |
| 9 | 端到端测试：授权设备、未授权设备、密钥变更 | 各端 QA |

---

## 8. 验证标准

| 场景 | 预期结果 |
|------|----------|
| 已授权设备调用 `/mg/auth/config` | 200 + `{ success: true, sharedSecret: "sec-xxx" }` |
| 未授权设备调用 `/mg/auth/config` | 401 + `{ success: false, error: "设备未授权" }` |
| 空 deviceId | 400 + `{ success: false, error: "deviceId 不能为空" }` |
| 密钥在服务端变更后，客户端通过 `/mg/auth/config` 获取新密钥 | 200 + 返回新 `sharedSecret` |
| 首次注册仍通过 `/mg/onehop/register-device` | 不变 |
