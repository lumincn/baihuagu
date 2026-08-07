# 百花服务器页测试报告（#ac55ffdc）

**日期**: 2026-08-07 19:20-19:45
**范围**: 百花服务器页（WebUI 设备管理 + 鸿蒙服务器管理页）
**提交**: 76864cc（baihuagu）+ b23e0e8（arkts）

---

## 一、背景

百花服务器页出过太多问题（闪退、官网误显、扫码乱码、下载失败等），需要**设计完善的自动化测试**：
1. Playwright 操作百花 WebUI（设备管理）
2. AutoGLM 操作鸿蒙版花记（服务器管理页）
3. 各种情况组合不能漏

## 二、Playwright 测试（百花 WebUI 设备管理页 /devices）

**文件**: `tests/e2e/tests/family-mode/server-manage.spec.ts`（8 用例）+ devices.spec.ts（1 用例修复）

| # | 用例 | 验证点 |
|---|------|--------|
| 1 | 页面加载：标题 + Tab 导航 | h2 标题 + Devices/Discover Tab |
| 2 | 待授权设备区域渲染 | Pending Devices 区域（空态或列表） |
| 3 | 已授权设备区域渲染 | Authorized Devices 区域 |
| 4 | 已授权列表含真实设备 | 鸿蒙平板"松风笔"在列表中（已同步 4 个知识库） |
| 5 | API: pending 端点 | 200 + 数组结构 |
| 6 | API: authorized 端点 | 200 + 数组 + ≥1 台设备 |
| 7 | API: onehop status | 200 + IsRunning/ServiceId（PascalCase） |
| 8 | 发现 Tab 切换 | OneHop 发现面板显示 |
| 9 | devices.spec（旧）| mDNS 端点 → /api/onehop/status 修复 |

**结果**: 9/9 全绿 ✅

**测试中发现并修复**:
- 旧 devices.spec 用 `/mg/discovery`（已移除）→ 改用 `/api/onehop/status`
- OneHop 状态 API 返回 **PascalCase**（IsRunning 非 isRunning）

## 三、AutoGLM 测试（鸿蒙服务器管理页）

**任务**: 进入"我的"→"服务器管理"→ 观察页面/服务器状态/功能入口

**结果**: ✅ 全部通过
- 页面正常打开，**无闪退**
- 标题"百花服务器"
- 已授权服务器：寻芳居（192.168.3.9），状态"**在线**"（绿色圆点）
- 扫码添加百花服务器入口存在
- "什么是百花服务器？"帮助链接可展开（内容正确）
- 无异常

## 四、此前修复回顾（同一页面）

| 问题 | 修复 |
|------|------|
| 服务器管理页闪退 | @Link currentPage 死参数（5df3758 + bf7bc1f 重写） |
| 官网误显 | 重写后固定只显示百花（bf7bc1f） |
| 扫码图标乱码 | SymbolGlyph 矢量图标（bf7bc1f） |
| 官网"未连接" | official_status_checked 重试（b23e0e8） |
| 知识库下载失败 | 空库重试 + serverId 兜底 + 服务恢复（b23e0e8） |

## 五、结论

**百花服务器页两端（WebUI + 鸿蒙）功能正确**，自动化测试覆盖：
- WebUI 设备管理：加载/列表/API/发现（Playwright 9 用例）
- 鸿蒙服务器管理：打开/状态/入口/帮助（AutoGLM 实测）
