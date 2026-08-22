# 虚拟师父系统 — 百花服务端（WebUI）实现

## 你的任务
在百花 WebUI（Blazor Server）中实现虚拟师父功能。百花是开源家庭版软件，用户在自己电脑上运行，通过浏览器使用。百花是本地服务端架构：WebUI 通过后端 AI 服务调用 DeepSeek，用户在 WebUI 设置中配置 API Key。

## 项目位置
- 百花根目录：C:\Users\lumin\src\baihua\
- WebUI：C:\Users\lumin\src\baihua\services\WebUI.Family\
- WebUI 页面：services\WebUI.Family\Pages\（已有 Messages.razor、KnowledgeGenerate.razor 等）
- WebUI 导航：services\WebUI.Family\Shared\FamilyNavMenu.razor（三场景导航：知识库/家庭/AI实验室）
- WebUI 服务层：services\WebUI.Family\Services\ApiService.cs（核心 API 调用，3 个 HttpClient）
- 后端 Controllers：services\TaskRunner.Family\Controllers\AI\（已有 AIController 系列，含 SSE 流式对话）
- 后端 AI 服务：services\TaskRunner.Family\Services\（AiClientService、ChatMemoryService 等）
- Contracts DTO：services\TaskRunner.Contracts\Ai\（ChatRequest、ChatResponse、ChatHistoryItem 等）
- 花阁已实现的参考代码：
  - MasterController：C:\Users\lumin\src\mdyj-cloud\services\TaskRunner.Cloud.KnowledgeBase\Controllers\MasterController.cs
  - MasterPromptBuilder：C:\Users\lumin\src\mdyj-cloud\services\TaskRunner.Cloud.KnowledgeBase\Services\MasterPromptBuilder.cs
  - Master DTO：C:\Users\lumin\src\mdyj-cloud\services\TaskRunner.Contracts\Master\MasterDtos.cs

## 架构决策（重要）
- **本地服务端架构**：百花运行在用户自己电脑上，WebUI → 后端 API → AiClientService → DeepSeek API。与手机端不同，百花不是纯客户端直连，而是通过本地后端服务中转
- **复用现有 AI 基础设施**：AIController 已有完整的 SSE 流式对话、ChatMemoryService 三层记忆、AiClientService
- **配额系统暂不实现**：本地运行不需要配额
- **师父数据存储**：使用本地 SQLite（AppDbContext），新增 masters、conversations、stage_summaries、apprentice_profiles、exam_checkpoints 五张表

## 需要实现的任务

### T-B01 后端：数据库表 + MasterController
1. 在 TaskRunner.Data 的 AppDbContext 中新增 5 张表（masters、conversations、stage_summaries、apprentice_profiles、exam_checkpoints），参考花阁 design.md 中的 SQL
2. 新建 Controllers\AI\MasterController.cs，实现以下端点（参考花阁的 MasterController，但使用百花的 AiClientService）：
   - `POST /api/master/create`：创建师父（goal + industry → masterId + masterName + 五阶段计划 + AI 欢迎语）
   - `POST /api/master/chat/stream`：SSE 流式对话（组装 System Prompt + 三层记忆 → 转发 DeepSeek → 流式返回）
   - `POST /api/master/{id}/stage-complete`：阶段完成（AI 生成摘要 → 推进阶段）
   - `GET /api/master/{id}/profile`：获取学徒画像
   - `POST /api/master/{id}/assess`：能力评估
   - `GET /api/master`：列出所有师父
   - `DELETE /api/master/{id}`：删除师父
3. 新建 Services\MasterPromptBuilder.cs（参考花阁的实现，包含行业人格映射、五阶段角色切换、三层记忆注入、内容安全过滤）
4. 在 TaskRunner.Contracts 中新增 Master DTO（可复制花阁的 MasterDtos.cs）

### T-B02 WebUI：师父对话页
新建 Pages\MasterChat.razor（@page "/master-chat"），参考 Messages.razor 的模式：
- 左侧师父列表（师父卡片：名称+目标+当前阶段+进度）
- 右侧对话区域（消息气泡 + 流式输出 + Markdown 渲染）
- 创建新师父弹窗（选择行业 + 输入目标）
- 顶部显示师父名 + 当前阶段标签
- 底部输入框 + 发送按钮
- 不同阶段特殊 UI 提示（入道引导式提问、筑基每日功课、磨砺模拟考试等）

### T-B03 WebUI：阶段进度页
新建 Pages\MasterStage.razor（@page "/master-stage"）：
- 垂直时间线展示所有阶段（已完成/进行中/未开始）
- 每阶段显示：名称 + 描述 + 进度条
- 阶段完成操作按钮

### T-B04 导航集成
修改 Shared\FamilyNavMenu.razor：
- 在场景 2（AI 实验室）的导航中添加"虚拟师父"入口
- 在 OnLocationChanged 路由推断中添加 "master-chat" / "master-stage" => 2

## 五阶段教学体系
| 阶段 | 师父角色 | 重点 | 完成标准 |
|------|---------|------|---------|
| 入道 | 引路人（温和好奇善问） | 确定目标、评估基础 | 完成初始任务+师父评估 |
| 筑基 | 严师（有耐心但要求严格） | 建立知识框架、每日任务 | 基础测试≥70%+连续学习≥7天 |
| 精进 | 匠人（极其耐心绝不放过细节） | 分科细化、攻克薄弱 | 考点覆盖≥85% |
| 磨砺 | 考官（模拟真实考试环境） | 模拟考试、查漏补缺 | 连续2次模拟达通过线 |
| 出师 | 前辈（实战建议考试经验） | 能力认证、报考指导 | 师父确认+报名完成 |

## 师父人格映射
| 行业 | 师父名 |
|------|--------|
| 中医/医学 | 岐伯 |
| 计算机/IT | 图灵 |
| 会计/财务 | 算圣 |
| 教资/教育 | 夫子 |
| 法律 | 廷尉 |
| 建筑 | 鲁班 |
| 通用 | 先生 |

## 三层记忆注入策略
System Prompt 组装顺序：
1. 师父角色设定 + 目标 + 当前阶段 (~1K tokens)
2. 核心画像 apprentice_profiles (~0.5K tokens)
3. 当前阶段摘要 stage_summaries (~1K tokens)
4. 最近20轮对话 conversations (~5K tokens)
总计: ~8K tokens

## 设计文档
完整设计见：C:\Users\lumin\src\project-manager\.codeartsdoer\specs\vmaster\design.md
任务列表见：C:\Users\lumin\src\project-manager\.codeartsdoer\specs\vmaster\tasks.md

## 代码风格要求
- 后端 Controller 遵循 AIController 的 partial class + SSE 流式模式
- 后端 Service 遵循现有 Services/ 的 Singleton + 构造函数注入模式
- WebUI 页面遵循 Messages.razor 的 @rendermode InteractiveServer + inject + SSE 解析模式
- DTO 放在 TaskRunner.Contracts 对应子目录
- 复用 AiClientService 和 ChatMemoryService，不要重复实现 AI 调用
- 不要添加注释除非被要求
- 编译验证：dotnet build services/BaiHua.slnx -c Release
