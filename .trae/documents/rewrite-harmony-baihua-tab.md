# 重写鸿蒙"获取知识 - 百花"Tab（对齐安卓）

## Context（为什么做这件事）

鸿蒙端"获取知识"里的"百花"Tab 当前复用 1666 行的 `ManageKnowledgeContent.ets`（`serverFilter: 'family'`），与"花阁"Tab 共用一个组件、靠条件分支拼凑，且知识库列表用按行业**折叠分组**的 `VaultListComponent`，与安卓"百花"Tab 的**扁平 FilterChip + Card** 列表完全不同。这种"一个组件两套模式"的设计是 bug 难修的根因。

安卓"百花"Tab（[KnowledgeTab.kt:237-484](file:///C:/Users/lumin/src/kotlin/app/src/main/java/com/lumin/huaji/android/ui/KnowledgeTab.kt)）是简洁的内联 UI：右上角刷新 → 发现卡片 → ⏳等待授权 → 未授权 → 已授权横向 Tab → 选中服务器的 FilterChip 行业筛选 + 扁平 Card 知识库列表（"获取"按钮 + 进度条）→ 空状态 🌸。它直接消费 `AuthViewModel`，**没有扫码/手动输入入口**（仅局域网发现）。

目标：新建独立组件 `BaihuaTabContent.ets`，1:1 复刻安卓"百花"Tab 的 UI 与逻辑；从 `KnowledgeTab.ets` 接入；一并清理 `ManageKnowledgeContent` 的 family 分支。

## SDK 评估结论（回应"是否先复刻 baihua_sdk"）

**不需要复刻安卓 `SdkSyncGateway`。** 安卓 `SdkSyncGateway` 仅是 `ISyncService.fetchVaultListAsync` + 5 分钟 TTL 缓存的薄封装；鸿蒙 `ServerManager` 已提供等价能力：`fetchVaults(server): Promise<VaultItem[]>`、`isVaultListCached(baseUrl)`、`clearVaultListCache(baseUrl)`，同样 5 分钟 TTL（[ServerManager.ets:164,532,691](file:///C:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/ServerManager.ets)）。`AuthViewModel` 保持不变，vault 状态放在新组件里（避免改动被 `ManageKnowledgePage` 共用的 `AuthViewModel`，降低风险）。

## 已确认的关键 API

- `getAuthViewModel(): AuthViewModel`（[EntryAbility.ets:217](file:///C:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/entryability/EntryAbility.ets#L217)），单例，`onUpdate(cb)` 订阅；字段 `listPending/listUnauthorized/listB: ServerAuthInfo[]`、`selectedServerUrl: string|null`、`lastError: string|null`；方法 `addDiscoveredServer(serverId,name,httpUrl)`、`requestAuth(info)`、`deleteServer(info): Promise<void>`、`clearRejected(info)`、`clearError()`、`selectServer(url)`、`markRevoked(info)`
- `ServerAuthInfo`：`serverId`(readonly)、`name`、`httpUrl`、`isOnline`、`state: AuthState`；`AuthState` = UNAUTHORIZED/PENDING/REJECTED/AUTHORIZED/REVOKED
- `EnhancedPairingService`：`getEnhancedPairingService(ctx)`、`initialize(): Promise`、`startDiscovery(): Promise<boolean>`、`stopDiscovery(): Promise<boolean>`、`getDiscoveredServers(): ServerConnectionInfo[]`；`ServerConnectionInfo` = { method, deviceId?, deviceName?, baseUrl?, hostName, serverIp, port, ... }
- `SyncService`：`new SyncService(ctx, uiContext)`、`syncVault(server, vault): Promise<SyncResult>`
- `getNoteDatabase(ctx)` → `db.getVaults()` 返回 `DatabaseResult<VaultInfo[]>`，`VaultInfo.vaultId` 为本地已获取标识
- `ThemeColors`：静态 `ResourceColor` 属性（primary/primaryText/secondaryText/tertiaryText/background/surface/surfaceAlt/divider/error/errorLight）+ `*String` 字符串；无 `primaryContainer`，选中态用 `errorLight`、未选中用 `surfaceAlt` 近似
- `ErrorHandler.safeShowToast(uiCtx, {message, duration})`、`getDeviceId(): string`

## 实施步骤

### 步骤 1：新建 `entry/src/main/ets/components/BaihuaTabContent.ets`

`@Component export struct BaihuaTabContent`，1:1 复刻安卓"百花"分支。结构（自上而下）：

1. **错误条**：`authVm.lastError` 非空时显示 `⚠ {lastError}` + "关闭"按钮（`authVm.clearError()`），红色 `errorLight` 背景
2. **刷新按钮行**：右侧"扫描中…"+`LoadingProgress(14)`（`isDiscoveringAuth` 时）+ 🔄 按钮（`startAuthDiscovery()`）
3. **发现的服务器卡片**（`discoveredAuthServers.length > 0` 时）：标题"发现的服务器" + 每行 `hostName` + `baseUrl` + "添加"（`addDiscoveredToAuth`）/ "已添加"（已在三组列表中）
4. **⏳ 等待授权分组**（`listPending`）：每行 名称+URL+"⏳ 等待中"+✕（`authVm.deleteServer(info)`）
5. **未授权分组**（`listUnauthorized`）：状态文案（UNAUTHORIZED→"未授权"/REJECTED→"授权被拒绝"/REVOKED→"已撤销"）+ 按钮（REJECTED→"再次请求授权"先 `clearRejected`；其余→"请求授权"，均调 `requestAuth`）+ ✕（`deleteServer`）
6. **已授权横向 Tab**（`listB`）：`Scroll` 横向 + 每个胶囊 `Row`：圆点（`primary`在线/`error`离线）+ 名称 + "在线/离线" + ✕；选中态 `errorLight` 背景，点击 `authVm.selectServer(url)`（再次点同一项 → `selectServer(null)` 收起，对齐安卓）
7. **选中服务器知识库区**（`selectedServerUrl` 非 null）：
   - 加载中 → `LoadingProgress` 居中
   - 错误 → 红色卡片 `⚠ {vaultError}` + "重试"（`loadVaultsForServer`）
   - 空库 → "📚 暂无知识库"
   - 有库 → **FilterChip 行**（"全部" + `industries = vaults.map(v=>v.industry||'其他').distinct().sorted()`，`selectedIndustry` 默认"全部"）+ **扁平 Card 列表**（`filtered = vaults.filter(排除 localVaultIds + 行业匹配)`）：
     - 每个 Card：`Column` → `Row(padding 12)`：左 `Column(layoutWeight 1)` 名称(15,Medium) + 行业(12,secondary)；右：同步中→`LoadingProgress(20)`，否则→"获取"按钮(13,surface 底/primary 字,padding h12 v6)；同步中→底部细进度条 `Progress({type:Linear,value:syncPercent,total:100})`（无 percent 时用 `LoadingProgress().height(3)` 占位）
   - `filtered.isEmpty()` 且非全部已获取 → "所有知识库已获取 ✅"
8. **空状态**（三组列表全空）：🌸 + "点击上方刷新按钮发现百花服务器"
9. 底部 `Blank().height(80)` 占位

**组件状态与生命周期**：
- `@State`：`listPending/listUnauthorized/listB: ServerAuthInfo[]`、`selectedServerUrl: string|null`、`lastError: string|null`、`discoveredAuthServers: ServerConnectionInfo[]`、`isDiscoveringAuth: boolean`、`vaults: VaultItem[]`、`isLoadingVaults: boolean`、`vaultError: string|null`、`localVaultIds: Set<string>`、`selectedIndustry: string`（默认"全部"）、`syncingVaultId: string`、`syncPercent: number`
- `aboutToAppear`：取 `getAuthViewModel()` + `getEnhancedPairingService(ctx)` + `new ServerManager(ctx)` + `new SyncService(ctx, uiContext)`；`authVm.onUpdate(() => this.syncFromAuthVm())`；`syncFromAuthVm()` 快照三组列表 + `selectedServerUrl` + `lastError`，若 `selectedServerUrl` 变化则触发 `loadVaultsForServer`；`localVaultIds = (await getNoteDatabase(ctx).getVaults()).data.map(v=>v.vaultId)`；首次进入且 `discoveredAuthServers` 空 → 自动发现一次（对齐安卓 + 当前鸿蒙行为，30 秒后停）
- `aboutToDisappear`：`clearInterval(pollTimer)` + `enhancedPairingService.stopDiscovery()`
- `startAuthDiscovery()`：`isDiscoveringAuth=true` → `enhancedPairingService.startDiscovery()` → 30 秒后 `stopDiscovery` + 刷新 `discoveredAuthServers`；同时启动 2 秒轮询 `pollTimer` 刷新发现列表（对齐当前 `startAuthDiscoveryPolling`）
- `loadVaultsForServer(url)`：从 `listB` 找 `ServerAuthInfo` → 构造 `ServerConfig`（复用 `ManageKnowledgeContent.syncServersFromAuthVm` 的字段映射）→ `isLoadingVaults=true` → `serverManager.fetchVaults(server)` → `vaults = data`；401 → `authVm.markRevoked(info)` + 清空 `vaults`；其他异常 → `vaultError = msg`
- `onSyncVault(vault)`：构造 `VaultConfig`（`{id,name,serverId,industry}`）+ `ServerConfig` → `syncingVaultId=vault.id` → `syncService.syncVault(server, vault)` → 成功后 `localVaultIds.add(vault.id)` + 从 `vaults` 移除该库 + `loadVaultsForServer` 重载；失败 → toast
- `addDiscoveredToAuth(srv)`：`serverId = srv.deviceId ?? \`${srv.serverIp}_${srv.port}\``、`baseUrl = srv.baseUrl ?? \`http://${srv.serverIp}:${srv.port}\``、`name = srv.hostName || srv.serverIp` → `authVm.addDiscoveredServer(serverId, name, baseUrl)`

**ArkTS 注意点**：对象不能 spread（逐字段复制）；`@State` 数组赋值需新引用触发刷新（`this.listB = [...authVm.listB]`）；`throw` 只能抛 `Error`；类中不能嵌套 interface。

### 步骤 2：改 `entry/src/main/ets/components/KnowledgeTab.ets`

`sourceTab === 2`（百花）分支：把
```
ManageKnowledgeContent({ embedded: true, currentPage: $manageKnowledgeCurrentPage, sharedServerManager: this.sharedServerManager, serverFilter: 'family', onDataChanged: ... })
```
替换为
```
BaihuaTabContent({ sharedServerManager: this.sharedServerManager, onDataChanged: ... })
```
其余三个 Tab 分支不动。导入新组件。

### 步骤 3：清理 `ManageKnowledgeContent.ets` 的 family 分支

移除 family 专属代码（`serverFilter === 'family'` 分支与相关字段/方法）：
- 字段：`authViewModel`、`discoveredAuthServers`、`isDiscoveringAuth`、`authDiscoveryTimer`
- 方法：`syncServersFromAuthVm`、`shouldShowDiscoverySection`、`isDiscoveredInAuthList`、`addDiscoveredToAuth`、`startAuthDiscovery`、`startAuthDiscoveryPolling`、`deleteServerFromAuth`
- `aboutToAppear` 中 `if (serverFilter === 'family') {...}` 整块移除
- `loadServers` 中 `if (serverFilter === 'family' && authViewModel)` 早返回移除
- `getUnauthorizedServers`/`getAuthorizedServers` 移除 family 分支，回到 `getServerStatus` 判定
- `buildServerSections` 移除 family 的发现卡片 / pending 分组 / family 未授权分支（保留 website 流程）
- `buildServerList` 中 `serverFilter === 'family'` 的刷新按钮行移除
- `loadVaultsFromServer` 中 `if (serverFilter === 'family' && authViewModel)` 的 `markRevoked` 分支移除（保留通用 401 → `removeServerSecret` 处理）
- `requestAuthorization`、删除服务器回调中 family 分支移除

`ManageKnowledgeContent` 退化为"花阁/官网"专用组件。`serverFilter` 参数保留（仍可传 'website' 或 undefined）。

### 步骤 4：`ManageKnowledgePage` 处理

`ManageKnowledgePage`（[ManageKnowledgePage.ets](file:///C:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/ManageKnowledgePage.ets)）由"一键同步"无服务器时跳转（[Index.ets:530](file:///C:/Users/lumin/DevecostudioProjects/arkts/entry/src/main/ets/pages/Index.ets#L530)）。清理 family 分支后，它仅显示官网服务器。

**决策**：保留 `ManageKnowledgePage` 不动（行为退化为仅花阁/官网），`doOneClickSync` 跳转行为不变。用户管理百花服务器统一走"获取知识-百花"Tab。这样改动面最小、不破坏"一键同步"兜底入口。（如需让该页也能管理百花服务器，可后续将其内容替换为 `BaihuaTabContent`，但本次不做。）

### 步骤 5：`ConnectionMethodsComponent` 保留

仅在 `ManageKnowledgeContent` 中使用（"百花"Tab 改用新组件后不再用它，`ManageKnowledgePage` 仍用）。按用户指示保留不动。

## 验证

1. **编译**：`hvigorw.bat assembleApp`（设置 `DEVECO_SDK_HOME` + `NODE_HOME`），0 错误 0 警告
2. **真机端到端**（对齐安卓行为）：
   - 进入"获取知识-百花"Tab → 自动扫描，右上角🔄可手动重扫
   - 发现卡片出现局域网服务器 → 点"添加" → 进入"⏳ 等待授权"
   - WebUI 授权后 → 移到"已授权"横向 Tab，自动选中并加载知识库列表
   - FilterChip 行业筛选生效；点"获取" → spinner → 成功后该库从列表移除、`localVaultIds` 更新
   - 删除已授权服务器（✕）→ 从列表移除 + WebUI 设备列表刷新
   - 被撤销授权（401）→ 自动移到"未授权"组，状态"已撤销"
   - 空状态：清空所有服务器 → 🌸 "点击上方刷新按钮发现百花服务器"
3. **回归**："花阁"Tab 与 `ManageKnowledgePage` 仍可正常添加官网服务器、加载知识库
4. **ArkTS 语法**：确认无对象 spread、无 `any`、`throw` 均抛 `Error`

## 关键文件

| 文件 | 操作 |
|------|------|
| `entry/src/main/ets/components/BaihuaTabContent.ets` | **新建** |
| `entry/src/main/ets/components/KnowledgeTab.ets` | 改"百花"分支接线 |
| `entry/src/main/ets/components/ManageKnowledgeContent.ets` | 清理 family 分支 |
| `entry/src/main/ets/pages/ManageKnowledgePage.ets` | 不动 |
| 参考安卓：`kotlin/app/.../ui/KnowledgeTab.kt` L237-484 | 1:1 复刻源 |
