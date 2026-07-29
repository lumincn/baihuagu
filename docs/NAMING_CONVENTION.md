# 项目多端命名规范

> 目的：统一沟通口径，避免"taskrunner""webui""后台""前端"等词在不同端之间产生歧义�?

---

## 一、命名原�?

1. **服务端按「版�?+ 层」命�?*：版本（家庭/官网�? 层（后台/前端�?
2. **移动端按「平台」命�?*：直接叫平台名，不加"�?
3. **简称优先用 3~4 字母代号**：口头和书面都方�?
4. **禁止混用的旧叫法**�?
   - �?"后台"——不知道指家庭后台还是官网后�?
   - �?"前端"——不知道�?WebUI 还是移动�?
   - �?"服务�?——两个版本各有一个服务端
   - �?"taskrunner"——默认指家庭版，官网版也�?TaskRunner.Cloud
   - �?"webui"——默认指家庭版，官网版也�?WebUI.Cloud

---

## 二、各端正式名称与代号

### 服务端（Server�?

| 完整名称 | 中文简�?| 英文代号 | 项目目录 |
|---------|---------|---------|---------|
| 家庭版后台服�?| **家庭后台** | `TRF` (TaskRunner Family) | `services/Baihua.Family/` |
| 家庭版Web界面 | **家庭前端** | `WUF` (WebUI Family) | `services/Baihua.Web/` |
| 官网版后台服�?| **官网后台** | `TRC` (TaskRunner Cloud) | `services/TaskRunner.Cloud/` |
| 官网版Web界面 | **官网前端** | `WUC` (WebUI Cloud) | `services/WebUI.Cloud/` |

### 移动端（Mobile�?

| 完整名称 | 中文简�?| 英文代号 | 项目目录 |
|---------|---------|---------|---------|
| 鸿蒙ArkTS移动�?| **鸿蒙�?* | `HMOS` | `arkts/` |
| Android移动�?| **安卓�?* | `AND` | `kotlin/` |

### 其他

| 完整名称 | 中文简�?| 英文代号 | 说明 |
|---------|---------|---------|------|
| 官网静态站 | **官网�?* | `SITE` | `website/`，纯 HTML 官网 |
| 共享契约�?| **契约�?* | `CONTRACTS` | `Baihua.Contracts/`，前后端共享 DTO |

---

## 三、使用示�?

### 口头沟�?
> "今天改的�?**TRC** �?Browse API�?*WUC** 的面包屑也要同步。鸿蒙版不用动�?

### 书面/文档
> 【问题�?*家庭后台** (TRF) �?`GetConfigDirectory()` 存在栈溢出�? 
> 【修复】已�?TRF �?**官网后台** (TRC) 同步修复�?

### Commit Message
```
fix(TRF): GetConfigDirectory 栈溢�?

- 递归调用自身导致 StackOverflowException
- fallback 改为 AppDomain.CurrentDomain.BaseDirectory
```

### Issue / Bug 标签建议
| 标签 | 含义 |
|------|------|
| `TRF` | 家庭后台 |
| `WUF` | 家庭前端 |
| `TRC` | 官网后台 |
| `WUC` | 官网前端 |
| `HMOS` | 鸿蒙�?|
| `AND` | 安卓�?|

---

## 四、目录命名对�?

```
services/
├── Baihua.Family/      �?家庭后台 (TRF)
├── Baihua.Web/           �?家庭前端 (WUF)
├── TaskRunner.Cloud/       �?官网后台 (TRC)
└── WebUI.Cloud/            �?官网前端 (WUC)

apps/
├── arkts/                  �?鸿蒙�?(HMOS)
└── kotlin/                 �?安卓�?(AND)
```

---

## 五、常见问�?

**Q：为什么不�?服务�?前端"这种通用叫法�?*  
A：本项目同时维护两套服务端（家庭/官网），�?服务�?无法区分版本，容易产�?改了官网但家庭版也需�?的遗漏�?

**Q：TaskRunner.Cloud �?WebUI.Cloud 为什么不合并�?官网服务�?�?*  
A：两者是独立进程�?788 / 5177），独立部署，出问题时需要分别排查，分开命名更精确�?

**Q：移动端为什么不�?移动�?而要�?鸿蒙�?安卓�?�?*  
A：两个移动端代码完全不同（ArkTS vs Kotlin），功能进度也不同，需要区分�?

