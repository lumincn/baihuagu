# 开发说明（家庭版 Family）

## 环境信息

### PowerShell 版本

| 命令 | 版本 | 默认编码 |
|------|------|----------|
| `pwsh` | 7.6.4 | UTF-8（无需 BOM） |
| `powershell` | 5.1 | GBK（需要 UTF-8 BOM） |

**推荐使用 `pwsh`（PowerShell 7）**，默认支持 UTF-8，无需处理 BOM 问题。

### BOM 处理方式

- **PowerShell 7 (`pwsh`)**: 默认 UTF-8，脚本文件不需要 BOM。中文显示正常。
- **PowerShell 5 (`powershell`)**: 默认 GBK 编码，脚本文件需要 **UTF-8 with BOM** 才能正确显示中文。
- **脚本头部**: 建议添加 `chcp 65001` 确保控制台使用 UTF-8：
  ```powershell
  chcp 65001 > $null
  [Console]::OutputEncoding = [System.Text.Encoding]::UTF8
  ```

### 项目目录结构

```
C:\Users\lumin\src\
├── baihuagu/          # 百花 - 家庭版（本项目）
├── mdyj-cloud/        # 花阁 - 云端版
└── kotlin/            # 百花 Android 客户端

C:\Users\lumin\DevecostudioProjects\
└── arkts/             # 百花鸿蒙客户端
```

### 命令行工具

| 项目 | Linux/Mac | Windows |
|------|-----------|---------|
| 百花 | `./bh` | `.\bh.ps1` |
| 花阁 | `./hg` | `.\hg.ps1` |

> 当前架构已从单体后台拆分为 3 个独立后端服务：
> - **TaskRunner.Family** (8788) — 家庭/亲子功能（任务、成就、OpenClaw、设备配对）
> - **TaskRunner.AI** (8791) — AI 模型、聊天、配置管理
> - **TaskRunner.Vault** (8790) — 知识库、同步、搜索、索引
>
> 3 个服务共用同一个 SQLite `taskrunner.db`（通过 `Core.Shared` 共享数据层）。

## 助手 / 自动化约定

- **只要本仓库内 `dotnet build` 成功**，即应保证后台处于运行状态：
  - TaskRunner.Family **8788**、TaskRunner.AI **8791**、TaskRunner.Vault **8790**、WebUI **5177**
  - 若未监听，应在释放端口/处理文件锁后 **`dotnet watch run`** 拉起对应服务
- **WebUI 与 TaskRunner 之间的共享数据类型和 API 接口定义必须放在 `TaskRunner.Contracts`**，两边禁止各自重复定义。新增或修改 API 契约时，先更新 Contracts，再让两边引用同一版本。
- **共享业务服务（如 `VaultSettingsService`、`VaultNoteIndexer`）放在 `Core.Shared`**，`TaskRunner.Family` 与 `TaskRunner.Vault` 均通过引用 `Core.Shared` 使用，避免 HTTP 调用开销。

## 目录

- `services/TaskRunner.Family/`：家庭版主后台（亲子功能、设备管理）
- `services/TaskRunner.AI/`：AI 微服务（模型、聊天、配置）
- `services/TaskRunner.Vault/`：知识库微服务（Vault、Sync、Search）
- `services/WebUI.Family/`：家庭版 Web 界面（Blazor Server）
- `services/TaskRunner.Contracts/`：共享 DTO 与接口契约
- `services/Core.Shared/`：共享服务层（含 VaultSettingsService、DeviceService 等）
- `services/TaskRunner.Data/`：共享 EF Core 数据层
- `libs/BaihuaSdk/`：跨平台移动端 SDK（net9.0;net10.0，零 MAUI 依赖，主要 target net10.0）
- `libs/MobileContract/`：移动端契约（DTO、接口定义）
- `clients/MobileApp.Maui/`：MAUI Blazor Hybrid 移动客户端
- `bh`：极简 CLI 工具（Linux/Mac）
- `docs/`：协议与架构文档
- `scripts/`：开发、发布、部署脚本
- `tests/BaihuaSdk.Tests/`：SDK 单元测试与集成测试
- `tests/MobileApp.Maui.Tests/`：MAUI DI 回归测试
- `tests/TaskRunner.Family.Tests/`：后端配对服务测试

## 访问授权

```bash
# 一键打开管理面板（自动启动服务）
./bh dashboard
```

无需密码，无需 IP 白名单。授权基于操作系统用户权限（只有能运行 `bh` 命令的本机用户才能访问）。

## 常用命令

```bash
# 开发模式（Linux/macOS，一键启动全部 3 个后台 + WebUI）
./bh dashboard

# 或手动分别启动
# 终端 1
cd services/TaskRunner.AI && dotnet watch run --non-interactive --no-hot-reload --urls "http://0.0.0.0:8791"
# 终端 2
cd services/TaskRunner.Vault && dotnet watch run --non-interactive --no-hot-reload --urls "http://0.0.0.0:8790"
# 终端 3
cd services/TaskRunner.Family && dotnet watch run --non-interactive --no-hot-reload
# 终端 4
cd services/WebUI.Family && dotnet watch run --non-interactive

# 编译验证（推送前必须执行）
dotnet build services/BaiHua.slnx -c Release
```

## 端口

| 服务 | 端口 | 说明 |
|------|------|------|
| TaskRunner.Family | 8788 | HTTP API（家庭/亲子功能） |
| TaskRunner.AI | 8791 | HTTP API（AI 模型与配置） |
| TaskRunner.Vault | 8790 | HTTP API（知识库、同步、搜索） |
| WebUI | 5177 | HTTP Blazor Server |

## 移动端兼容

移动端（鸿蒙/安卓）通过 `http://<server>:8788` 发现服务器并调用 API。
`TaskRunner.Family` 在 8788 上保留了一个**转发中间件**，将移动端调用的 Vault 域 API 路径（如 `/mg/manifest`、`/mg/file`、`/mg/cards`、`/mg/vaults` 等）透明转发到 `TaskRunner.Vault`（8790）。因此 **移动端代码无需任何改动**。

授权与认证：
- 局域网发现/配对阶段通过 HMAC 签名（共享 `sharedSecret`）校验设备身份。
- 转发到 `TaskRunner.Vault` 时，`TaskRunner.Family` 会为已授权设备自动附加 `Authorization: Bearer <accessToken>`，Vault 侧校验 Bearer Token 或本机回环请求。

## BaihuaSdk（跨平台移动端 SDK）

**位置**: `libs/BaihuaSdk/` — 纯 C# `net9.0;net10.0` 类库，零 MAUI 依赖。

封装了与百花服务器通信的全部协议层：

| 模块 | 说明 |
|------|------|
| `Signing/` | HMAC-SHA256 请求签名（与 Kotlin `RequestSigner.kt` 算法一致） |
| `Transport/` | HttpClient 封装、签名注入、HTTPS→HTTP 降级、错误中文映射 |
| `Services/SyncServiceImpl.cs` | 知识库同步（manifest → 文件下载 → 本地写入） |
| `Services/PairingServiceImpl.cs` | QR 码解析、多地址格式、设备注册 |
| `Services/LogServiceImpl.cs` | 批量缓冲日志上报 |
| `Services/QuotaServiceImpl.cs` | 配额/购买 API |
| `Push/PushWebSocketService.cs` | WebSocket 实时推送 + HTTP 轮询降级 |
| `Storage/` | ISecureStore / IServerConfigStore 接口（平台层实现） |

```bash
# 运行 SDK 单元测试
dotnet test tests/BaihuaSdk.Tests/BaihuaSdk.Tests.csproj

# 运行集成测试（需要百花服务器）
export BaiHua_TEST_URL=http://192.168.3.x:8788
export BaiHua_TEST_SECRET=<shared-secret>
export BaiHua_TEST_VAULT_ID=<vault-id>
dotnet test tests/BaihuaSdk.Tests/BaihuaSdk.Tests.csproj --filter Integration
```

## MobileApp.Maui（百花移动客户端）

**位置**: `clients/MobileApp.Maui/` — .NET MAUI Blazor Hybrid App。

- **Android**: `dotnet build -f net9.0-android -c Release` → APK 在 `bin/Release/net9.0-android/com.lumin.BaiHua-Signed.apk`
- **iOS**: 需要 macOS + Xcode（GitHub Actions CI 已配置 `.github/workflows/ci.yml`）

```bash
# Android Release 编译
dotnet build -f net9.0-android -c Release

# 安装到手机
adb install clients/MobileApp.Maui/bin/Release/net9.0-android/com.lumin.BaiHua-Signed.apk
```

### Honor/部分 Android 设备 .NET 10 兼容性

**已知问题**: 2026-06 期间，Honor 真机（`ADNQUT5813009383`）安装 .NET 10 Preview APK 后启动崩溃：
```
java.lang.IllegalArgumentException: No view found for id 0x7f0800ff
for fragment NavigationRootManager_ElementBasedFragment
```
这是 MAUI 10 Preview 在部分 Android 设备上的已知框架问题（[dotnet/maui#32029](https://github.com/dotnet/maui/issues/32029)）。

**当前状态（2026-06-27）**:
- 为规避 Honor 设备兼容性问题，Android 目标框架已回退至 **.NET 9 LTS**（`net9.0-android`）
- MAUI workload `9.0.x`，`ZXing.Net.Maui.Controls` 降级至 `0.6.0`
- Debug + Release 构建成功（0 错误 0 警告）
- 单元测试全部通过（155 + 9）
- **✅ 真机验证通过**: Honor `ADNQUT5813009383` 安装 .NET 9 APK 后启动正常，MainActivity 可见，无崩溃

**csproj 关键防御配置**（已启用）:
```xml
<AndroidEnableFastDeployment>false</AndroidEnableFastDeployment>
<EmbedAssembliesIntoApk>true</EmbedAssembliesIntoApk>
<AndroidStoreUncompressedFileExtensions>.so;.dll</AndroidStoreUncompressedFileExtensions>
<AndroidEnableCompressionInNativeLibraries>false</AndroidEnableCompressionInNativeLibraries>
```

**后续若需升级 .NET 10**: 需先在 Honor/相关设备上重新验证 MAUI 10 Fragment 兼容性，确认无崩溃后再将 `TargetFrameworks` 改回 `net10.0-android`。

### Debug TLS 证书宽松

Debug 构建跳过 TLS 证书验证（方便本地自签名证书开发），Release 构建严格校验证书。见 `MauiProgram.cs` 中的 `#if DEBUG` 条件判断。

页面：首页（服务器列表）、配对（扫码/手动注册）、同步（知识库拉取+文件下载）、已同步（文件浏览器）。

## 测试

### BaihuaSdk 测试

**单元测试**（无需服务器，覆盖核心算法和逻辑）：

```bash
dotnet test tests/BaihuaSdk.Tests/BaihuaSdk.Tests.csproj --filter Unit
```

覆盖模块：
- `Signing/RequestSigner`：签名算法、密钥管理、SHA256/HMAC 验证
- `Transport/HttpTransport`：URL 规范化、错误提取、HTTP 状态码映射
- `Services/SyncServiceImpl`：文件类型判断、路径安全验证
- `Services/PairingServiceImpl`：QR 码解析（新旧格式）、服务器地址提取

**集成测试**（需要运行中的百花服务器）：

```bash
export BaiHua_TEST_URL=http://192.168.x.x:8788
export BaiHua_TEST_SECRET=<shared-secret>
export BaiHua_TEST_VAULT_ID=<vault-id>
dotnet test tests/BaihuaSdk.Tests/BaihuaSdk.Tests.csproj --filter Integration
```

测试完整流程：配对 → 获取知识库列表 → 获取 manifest → 同步文件

### MobileApp.Maui 测试

**DI 回归测试**（确保所有服务可正确构造）：

```bash
dotnet test tests/MobileApp.Maui.Tests/MobileApp.Maui.Tests.csproj
```

### TaskRunner.Family 测试

**后端配对服务测试**：

```bash
dotnet test tests/TaskRunner.Family.Tests/TaskRunner.Family.Tests.csproj
```

## 已知限制

- **华为/荣耀手机**: .NET 10 Preview 存在 `NavigationRootManager_ElementBasedFragment` 崩溃。当前 Android 目标框架已回退至 .NET 9 LTS 规避该问题，待 MAUI 10 兼容性验证通过后再考虑升级。详见上方「MobileApp.Maui → Honor 兼容性」。
- **Android 模拟器**: 需要 KVM 硬件加速（`sudo modprobe kvm_intel`，BIOS 中启用 VT-x）。
