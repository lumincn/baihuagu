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

### 终端中文乱码修复（`dotnet build` 输出）

运行 `dotnet build` 时，.NET 输出的 UTF-8 中文可能被 `pwsh` 误解码为 GBK 导致乱码（如"鐢熸垚澶辫触"）。

**修复方式**（已写入 PowerShell Profile `C:\Users\lumin\Documents\PowerShell\profile.ps1`）：
```powershell
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$PSDefaultParameterValues['*:Encoding'] = 'utf8'
```
配置后重启终端或重新打开 VS Code 即可生效。

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
> - **Baihua.Family** (8788) — 家庭/亲子功能（任务、成就、OpenClaw、设备配对）
> - **Baihua.AI** (8791) — AI 模型、聊天、配置管理
> - **Baihua.Vault** (8790) — 知识库、同步、搜索、索引
>
> 3 个服务共用同一个 SQLite `baihua.db`（通过 `Baihua.Core` 共享数据层）。

## 助手 / 自动化约定

- **只要本仓库内 `dotnet build` 成功**，即应保证后台处于运行状态：
  - Baihua.Family **8788**、Baihua.AI **8791**、Baihua.Vault **8790**、Baihua.Web **5177**
  - 若未监听，应在释放端口/处理文件锁后 **`dotnet watch run`** 拉起对应服务
- **WebUI 与后端之间的共享数据类型和 API 接口定义必须放在 `Baihua.Contracts`**，两边禁止各自重复定义。新增或修改 API 契约时，先更新 Contracts，再让两边引用同一版本。
- **共享业务服务（如 `VaultSettingsService`、`VaultNoteIndexer`）放在 `Baihua.Core`**，`Baihua.Family`、`Baihua.Vault` 和 `Baihua.AI` 均通过引用 `Baihua.Core` 使用，避免 HTTP 调用开销。
- **`git push` 失败时**，先启动代理再重试：`pwsh -File "C:\Users\lumin\myhysteria\start.ps1"`，等待几秒后设置 `$env:HTTPS_PROXY="socks5://127.0.0.1:1080"` 再 `git push`。若代理服务器 8.216.46.73 的 443/22 端口同时超时，多半是出口 IP 变化被阿里云安全组拦截：用 `C:\Users\lumin\aliyun-cli\aliyun.exe` 放行新 IP（需先 `aliyun configure`），完整流程见 project-manager 仓库 `docs/ALIYUN_SECURITY_GROUP.md`。

## 目录

- `services/Baihua.Family/`：家庭版主后台（亲子功能、设备管理）
- `services/Baihua.AI/`：AI 微服务（模型、聊天、配置）
- `services/Baihua.Vault/`：知识库微服务（Vault、Sync、Search）
- `services/Baihua.Web/`：家庭版 Web 界面（Blazor Server）
- `services/Baihua.Contracts/`：共享 DTO 与接口契约
- `services/Baihua.Core/`：共享服务层（含 VaultSettingsService、DeviceService 等）
- `services/Baihua.Data/`：共享 EF Core 数据层
- `services/BaiHua.slnx`：服务端解决方案（包含所有 services/ 项目及 libs/MobileContract）
- `services/bh` / `services/bh.ps1`：极简 CLI 工具（Linux/Mac / Windows）
- `libs/BaihuaSdk/`：跨平台移动端 SDK（net9.0;net10.0，零 MAUI 依赖，主要 target net10.0）
- `libs/MobileContract/`：移动端契约（DTO、接口定义）
- `clients/Huapu/`：花圃（BaiHua.Nursery）— 移动端技术实验与验证工具（非正式发布 App，详见下方说明）
- `clients/Huapu.slnx`：花圃解决方案（包含 BaihuaSdk + MobileContract + Huapu）
- `docs/`：协议与架构文档
- `scripts/`：开发、发布、部署脚本
- `tests/Baihua.Family.Tests/`：后端配对服务测试
- `tests/Baihua.Sdk.Tests/`：SDK 单元测试与集成测试
- `tests/Huapu.Tests/`：MAUI DI 回归测试

## 访问授权

```bash
# 一键打开管理面板（自动启动服务）
./bh dashboard
```

无需密码，无需 IP 白名单。授权基于操作系统用户权限（只有能运行 `bh` 命令的本机用户才能访问）。

## 常用命令

```bash
# 开发模式（Linux/macOS，一键启动全部 3 个后台 + WebUI）
cd services && ./bh dashboard

# 或手动分别启动
# 终端 1
cd services/Baihua.AI && dotnet watch run --non-interactive --no-hot-reload --urls "http://0.0.0.0:8791"
# 终端 2
cd services/Baihua.Vault && dotnet watch run --non-interactive --no-hot-reload --urls "http://0.0.0.0:8790"
# 终端 3
cd services/Baihua.Family && dotnet watch run --non-interactive --no-hot-reload
# 终端 4
cd services/Baihua.Web && dotnet watch run --non-interactive

# 编译验证（推送前必须执行）
dotnet build services/BaiHua.slnx -c Release
```

## 端口

| 服务 | 端口 | 说明 |
|------|------|------|
| Baihua.Family | 8788 | HTTP API（家庭/亲子功能、设备管理） |
| Baihua.AI | 8791 | HTTP API（AI 模型与配置） |
| Baihua.Vault | 8790 | HTTP API（知识库、同步、搜索） |
| Baihua.Web | 5177 | HTTP Blazor Server |

## 移动端兼容

移动端（鸿蒙/安卓）通过 `http://<server>:8788` 发现服务器并调用 API。
`Baihua.Family` 在 8788 上保留了一个**转发中间件**，将移动端调用的 Vault 域 API 路径（如 `/mg/manifest`、`/mg/file`、`/mg/cards`、`/mg/vaults` 等）透明转发到 `Baihua.Vault`（8790）。因此 **移动端代码无需任何改动**。

授权与认证：
- 局域网发现/配对阶段通过 HMAC 签名（共享 `sharedSecret`）校验设备身份。
- 转发到 `Baihua.Vault` 时，`Baihua.Family` 会为已授权设备自动附加 `Authorization: Bearer <accessToken>`，Vault 侧校验 Bearer Token 或本机回环请求。

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
dotnet test tests/Baihua.Sdk.Tests/

# 运行集成测试（需要百花服务器）
export BaiHua_TEST_URL=http://192.168.3.x:8788
export BaiHua_TEST_SECRET=<shared-secret>
export BaiHua_TEST_VAULT_ID=<vault-id>
dotnet test tests/Baihua.Sdk.Tests/ --filter Integration
```

## 花圃 / BaiHua.Nursery（移动端技术实验与验证工具）

**位置**: `clients/Huapu/` — .NET MAUI Blazor Hybrid App。
**解决方案**: `clients/Huapu.slnx`（包含 BaihuaSdk + MobileContract + Huapu）

> **定位说明**：花圃（BaiHua.Nursery）是百花服务对移动端支持的**技术验证工具**，用于验证 BaihuaSdk 协议、配对流程、同步功能等在真实移动设备上的表现。它**不是正式发布的 App**，不具备产品级功能完整性。花记的正式移动端是鸿蒙端（ArkUI）和安卓端（Jetpack Compose），它们功能远超花圃。
>
> 花圃的职责边界：
> - ✅ 验证百花服务端 API 对移动端的兼容性
> - ✅ 验证 BaihuaSdk 的配对/同步/签名协议
> - ✅ 作为 .NET MAUI 技术实验平台
> - ✅ 对鸿蒙/安卓端花记的功能创意起到互相启发的作用
> - ❌ 不承担正式移动客户端角色
> - ❌ 不与鸿蒙/安卓端功能完全对齐
>
> **与花记的关系**：花圃参考鸿蒙/安卓花记的 UI/UX 设计（底部 Tab 导航、暗色模式、品牌色等），但功能范围远小于花记。花圃可作为新功能的快速验证平台，验证通过后再移植到鸿蒙/安卓端。

**UI 设计参考**（对齐鸿蒙/安卓花记）：
- 底部 3 Tab 导航：首页 / 获取知识 / 我的
- 品牌色：红色 `#FF2442`（与鸿蒙花记一致）
- 完整暗色模式支持（CSS 变量 + `data-theme` 属性）
- 语义化颜色系统（19 个 CSS 变量，亮/暗双主题）

**页面结构**：
| 页面 | 路由 | 说明 |
|------|------|------|
| 首页 | `/` | 快捷入口、已配对服务器、搜索入口 |
| 获取知识 | `/knowledge` | 百花同步 + 配对（子 Tab 切换） |
| 我的 | `/profile` | 设备信息、数据概览、功能菜单 |
| 搜索 | `/search` | 全文搜索已同步知识库 |
| 配对 | `/pairing` | 扫码/手动配对服务器 |
| 同步 | `/sync` | 知识库获取（独立页面入口） |
| 已获取 | `/vaults` | 文件浏览器、Markdown 预览 |
| 设置 | `/settings` | 暗色模式切换、数据管理、关于 |

**组件拆分**：
- `SyncContent.razor` / `PairingContent.razor`：可复用内容组件，供 KnowledgePage 和独立页面共用

- **Android**: `dotnet build clients/Huapu.slnx -f net9.0-android -c Release` → APK 在 `clients/Huapu/bin/Release/net9.0-android/com.lumin.BaiHua-Signed.apk`
- **iOS**: 需要 macOS + Xcode（GitHub Actions CI 已配置 `.github/workflows/ci.yml`）

```bash
# Android Release 编译
dotnet build clients/Huapu.slnx -f net9.0-android -c Release

# 安装到手机
adb install clients/Huapu/bin/Release/net9.0-android/com.lumin.BaiHua-Signed.apk```
```

### 花圃 Honor/部分 Android 设备 .NET 10 兼容性

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

### 花圃 Debug TLS 证书宽松

Debug 构建跳过 TLS 证书验证（方便本地自签名证书开发），Release 构建严格校验证书。见 `MauiProgram.cs` 中的 `#if DEBUG` 条件判断。


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

### 花圃测试

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

- **华为/荣耀手机**: .NET 10 Preview 存在 `NavigationRootManager_ElementBasedFragment` 崩溃。当前 Android 目标框架已回退至 .NET 9 LTS 规避该问题，待 MAUI 10 兼容性验证通过后再考虑升级。详见上方「花圃 → Honor 兼容性」。
- **Android 模拟器**: 需要 KVM 硬件加速（`sudo modprobe kvm_intel`，BIOS 中启用 VT-x）。
