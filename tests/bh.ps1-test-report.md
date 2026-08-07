# bh.ps1 全面测试报告（Issue #7b7423e2）

**日期**: 2026-08-07 14:20 - 15:10
**范围**: bh.ps1（百花 Family 版 Windows PowerShell CLI）
**版本**: v2.0.0 → v2.0.0（--nologin 新增）
**提交**: d5cda41（重构+测试）→ 678c96e（--nologin）
**状态**: ✅ 全部通过

---

## 一、背景与目标

bh.ps1 是百花 Family 版的 CLI 管理脚本，历史 bug 频发：
- **8/6**: Release/Debug 编译不一致、`stop webui` 误停全部服务、UTF-8 BOM 编码坑
- **8/7**: dev 监听作用域 bug（LASTEXITCODE 误判）、FileSystemWatcher → dotnet watch 改造

用户要求：**从终端用户角度调整功能，再用专业代码实现**，并实现全面自动化测试。

## 二、功能 Review 结果（用户视角）

| # | 发现的问题 | 严重度 | 修复 |
|---|-----------|--------|------|
| 1 | 未知命令静默执行 dashboard（打错字直接开浏览器） | 高 | ✅ 提示"未知命令"+ help 指引 |
| 2 | logs 只有跟随模式，无行数参数 | 中 | ✅ `logs <name> [lines]`，`-f` 跟随 |
| 3 | restart 不支持单服务（restart webui 会停全部） | 高 | ✅ `restart [name]` 单服务 |
| 4 | stop/restart 未知服务名无提示 | 中 | ✅ 提示可选服务列表 |
| 5 | status 不显示端口/日志路径 | 中 | ✅ 显示 port + log 路径 |
| 6 | 健康检查 URL/端口散落 10+ 处硬编码 | 高 | ✅ 统一收进 Get-ServiceConfig 单一数据源 |
| 7 | exe 查找硬编码 net10.0（TFM 升级必炸） | 高 | ✅ Find-ServiceExe 通配 TFM |
| 8 | 无 version 命令 / 无 PS 5.1 兼容提示 | 低 | ✅ 新增 |
| 9 | 脚本无可测试性（无法单测） | 高 | ✅ 主入口守卫 + 函数化 |
| 10 | restart/start 后 pwsh 进程不退出（句柄继承） | 中 | ✅ exit 0 |
| 11 | dev/dashboard 自动开浏览器登录，测试/CI 场景不便 | 中 | ✅ 新增 `--nologin` 参数 |

## 三、自动化测试设计

### 3.1 单元测试（Pester 3.4，tests/bh.ps1.tests.ps1）— 26/26 全绿

**静态检查（7 用例）**
- 文件存在 / UTF-8 with BOM（PS 5.1 硬要求）/ 语法可解析
- 主入口守卫（dot-source 可测试性）/ 版本号 / Get-ServiceConfig 单一数据源
- 端口号零散落（剥离配置函数 + 注释后无硬编码端口）

**函数单元测试（10 用例）**
- Get-HgRoot 返回脚本目录
- Get-ServiceConfig 含 4 服务 / 每项 Project+Health+Port / 端口唯一
- ServiceOrder / StopOrder 顺序正确
- Get-LogPath / Get-PidPath 使用 TEMP
- Test-TcpPort 未知端口返回 false
- Find-ServiceExe 未知服务返回 null（不抛异常）/ 已知服务返回路径
- Test-NeedsRebuild git 不可用容错返回 false

**命令冒烟测试（9 用例）**
- version 输出版本号 / help 显示用法 / 未知命令提示
- logs 无参数提示 / logs 未知服务提示 / logs webui 行数参数
- status 输出端口 / status --nologin 参数解析 / help 含 --nologin 说明

### 3.2 集成测试（tests/bh.ps1.integration.ps1）— 8/8 全绿

| 用例 | 验证点 |
|------|--------|
| 初始状态检查 | 4 服务运行 |
| stop webui 只停 webui | **回归 8/6 的"stop 全停"坑** |
| restart webui 恢复 | 单服务重启 + 就绪 |
| 4 服务健康检查 200 | 8791/8790/8788/5177 |
| logs webui 非跟随读取 | 显示 last 5 lines |
| stop/restart 未知服务提示 | 容错 |
| dashboard --nologin 跳过浏览器 | 无 cli-token 调用、无浏览器打开 |

### 3.3 手工验证（命令流实测）
- `restart webui`：PID 19128→13532，单服务重启 ✅
- `stop webui`：其他 3 服务保持运行 ✅（8/6 坑回归）
- `dashboard --nologin`：跳过浏览器，服务就绪提示 ✅
- `start --nologin`：正常启动 4 服务 ✅

## 四、测试过程中发现并解决的问题

### 4.1 测试代码自身的坑（自动化测试的教训）
1. **`$Args` 是 PowerShell 自动变量**：`param([string[]]$Args)` 同名遮蔽导致 `@Args` 展开为空 → 冒烟测试意外触发真实 dashboard（停服务+重编译）。改名 `$CmdArgs`
2. **Write-Host 走信息流（6）**：`2>&1 | Out-String` 捕获不到 → 需 `6>&1`
3. **Pester 3.4 Mock 对原生外部命令无效**：`Mock git { throw }` 不拦截 git → 改测容错路径
4. **块注释 `<#...#>` 剥除**：正则 `^\s*#` 只过滤行注释

### 4.2 产品代码问题
- **`--nologin` 泄漏到 $Arg**：`dashboard --nologin` 时 Cmd-Stop 读到 `--nologin` 报"未知服务" → 参数解析区显式清除 $Arg/$Browser
- **dashboard 重编译撞上团队半成品代码**：FAM-33 的 Checkin.razor CS0029 编译错误阻塞构建 → 通知 devbh 修复（2d65c3b）

## 五、结果汇总

| 类别 | 用例数 | 结果 |
|------|--------|------|
| 静态检查 | 7 | ✅ 全绿 |
| 函数单元 | 10 | ✅ 全绿 |
| 命令冒烟 | 9 | ✅ 全绿 |
| 集成测试 | 8 | ✅ 全绿 |
| **合计** | **34** | **34/34 通过** |

## 六、遗留建议
- `status` 输出 ✓/⚠ 在部分终端显示为 `?`（编码），可考虑 ASCII 化
- `Test-NeedsRebuild` 把"未提交的测试文件改动"也视为需重建（git status --short），CI 场景可加忽略选项
- 建议 bh.ps1 测试纳入 CI（本地 Pester + 集成测试手动触发）
