# UX 审计发现 — B 组（家庭 / 学习 / 游戏化，10 页）

- 审计时间：2026-08-11 13:22–13:45（GMT+8）
- 环境：http://127.0.0.1:5177（Blazor Server，系统 Edge headless，1440×900；响应式抽查 375×812）
- 方法：逐页截图 + DOM 深度检查（模型不支持直接看图，以 DOM/坐标/样式为准）+ 核心流程交互 + 数据抽查 + 只读源码核对
- 严重度：P0=功能不可用/数据错误/崩溃；P1=明显 bug/流程断；P2=体验差/易用性；P3=视觉/文案细节
- 截图均在 `tests/ux-audit/shots/`（每页至少 1 张，交互过程另有 b2-*/b-mobile-* 系列）

**总览：10 页全部正常渲染，无白屏、无 console 错误、无 pageerror、无失败请求（各页均为 0/0/0）。** 发现 P1×2、P2×3、P3×9、建议×4。

---

## /family 家庭首页

- [P1] **成员数据重复：存在两个完全相同的「小明」**（LearnerId=1 与 2，同名同头像同色，且 IsDefault 均为 1）
  - 位置：`/family` 家庭成员卡片 ×2、「本周排行榜」×2；同样出现在 /dashboard 成员筛选、/achievements 成员选择、/family/quiz 对战双方
  - 现象：`/api/achievements/dashboard` 返回 `FamilyStats` 两条 `{LearnerId:1,Name:"小明"}`、`{LearnerId:2,Name:"小明"}`（已直接调 API 证实是数据问题而非渲染问题）；学习记录记在 Id=1 上，两个卡片统计显示「1 本周」和「0 本周」，名字完全相同无法区分
  - 期望：成员不应重名重复；需去重工具 + 创建成员时校验重名（`LearnerService.CreateAsync` 无重名校验）
  - 复现：打开 /family 即见
  - 截图：b-family.png、b-dashboard.png、b2-dashboard-filtered2.png、b-family_quiz.png
- [P3] **H1 标题 emoji 重复**：`👨👩👧 👨👩👧 我们的家`（标题资源本身含 emoji，Razor 又硬编码了一个）
  - 位置：/family 页头
  - 截图：b-family.png
- [P3] 「本周学习卡片 / 最长连续打卡」统计卡片数字无单位说明，0 值状态下信息量低（可接受，不展开）

## /dashboard 家长看板

- [P1] **「闻鸡起舞」成就时区错误：北京时间下午学习也解锁「早上 6 点前完成学习」**
  - 位置：/dashboard「最新成就」/ /achievements；根因 `AchievementEngine.cs`：`a.CreatedAt.Hour < 6` 按 UTC 判断（SQLite 存 UTC），13:29 北京（=05:29 UTC）学习即解锁
  - 实测：13:29 打卡后 3 个成就同时解锁（第一步 ✓、百发百中 ✓、闻鸡起舞 ✗ 不应解锁）
  - 期望：按北京时间（+8）判断 `Hour < 6`
  - 复现：任意下午时间学习一张卡片 → 成就墙出现「闻鸡起舞」
  - 截图：b2-dashboard-filtered2.png、b-achievements.png
- [P3] **文案中英混杂**：「今日完成 1 张卡片 ↑ +1 vs 昨天」应为「较昨天 +1」
  - 截图：b2-dashboard-filtered2.png
- [P3] 成长时间线显示「每页 20 条 · 最近 30 天」但无分页控件（数据 <20 条时属轻微误导）
- 说明：今日三件事 / 连续打卡 / 成员筛选交互正常；筛选按钮因 P1 重复成员出现两个同名「小明」

## /checkin 学习打卡

- [P2] **今日学习清单显示原始卡片十六进制 ID 而非卡片内容**
  - 位置：/checkin「今日学习清单」；实测条目为 `✅ 卡片 FE128CBB29200CEE 13:29 每日卡片`
  - 根因：`CheckinService.ResolveContent` 直接返回 `$"卡片 {cardId}"`，未查卡片标题/问题
  - 期望：显示卡片正面问题或简短标题；家长看清单应能知道孩子学了什么
  - 复现：在 /daily-card 学 1 张卡 → 打开 /checkin
  - 截图：b-checkin-final.png
- 说明：7 天日历、补签弹窗（补签 8月8日？本月剩余 2/3 → 取消）、连击保护文案均正常；补签逻辑「3 天窗口+月限 3 次」与源码一致

## /daily-card 每日一帖

- [P3] **「家长出题」表单：知识库输入框 value 与 placeholder 同为「家长出题」**
  - 位置：/daily-card 底部 ✏️ 家长出题 折叠区；`customDeck = "家长出题"` 且 placeholder 资源也是「家长出题」
  - 现象：输入框看起来像占位符，实际是预填值，不修改提交会得到名为「家长出题」的卡组；占位符与默认值重复易误导
  - 截图：b2-dailycard-customform.png
- 说明：卡片翻转、忘记/困难/记得 三键提交、进度「1 / 10」、空提交校验（"题目和答案不能为空"）均正常
- [建议] 翻转后「点击卡片查看答案」与「点击翻转」两个提示并存，建议统一为一种引导文案

## /achievements 成就墙

- [P3] **「添加奖励」空表单提交无任何反馈**：`AddRewardAsync` 对空名称 `return` 静默退出，无错误提示、无按钮反馈
  - 复现：直接点「添加奖励」（名称/目标值为空）→ 页面无任何变化
  - 期望：提示「奖励名称不能为空」等校验信息（后端 API 有该校验，前端静默吞掉）
  - 截图：b2-achievements-addreward-empty2.png
- [P3] 奖励图标字段默认值为字面量 `"??"`（源码 `RewardIcon = "??"`），若 UI 某处展示图标会显示乱码（当前列表未展示图标，风险低）
- 说明：成就解锁、统计（3/14、金牌 1）、「+ 添加」成员弹窗（添加/取消）均正常

## /leaderboard 家庭赛舟榜

- [P2] **家庭排行默认「孩子榜」对单成员家庭显示空数据**
  - 位置：/leaderboard → 家庭排行 Tab；默认 activeRole="kids"
  - 现象：本家庭仅有 2 个成员且均被兜底规则判为「大人」（IsDefault=1 → adults），「孩子榜」显示「暂无数据，快去每日一帖学习吧！」，需手动勾选「显示全家排行」或切「大人榜」才可见真实数据（本周 1 张卡片）
  - 根因：`LeaderboardService.GetRoleLeaderboardAsync` 无角色字段（源码注释 TECH-08 未完成），按 IsDefault 硬编码大人/孩子，且用户无任何设置角色入口；默认榜与多数家庭（默认学习者=家长）实际情况不符
  - 期望：默认展示「全家」或按成员构成智能选择榜；提供角色设置入口
  - 复现：打开 /leaderboard → 点「家庭排行」
  - 截图：b2-leaderboard-familytab2.png、b2-leaderboard-selftab2.png
- [P3] 「和自己比」上周为 0 时变化量显示 `--`，可优化为「+1」（源码注释为有意设计，但数字场景下 -- 语义不清）
- 说明：分数「21 分」= 卡片数 1 + 正确率加成 20（`Score = total + accuracy*20`），设计如此但家长可能困惑，建议加说明

## /family/quiz 亲子互考

- [P1 同源] **对战双方可选同一个成员**：因 P1 重复成员，成员 A/B 下拉均只有「小明」，实际可「小明 vs 小明」开考（已实测开始互考成功，显示「小明 出题（30 秒限时）」）；下拉选项也是「🙂 小明」出现两次
  - 期望：少于 2 名不同成员时禁止开始并提示「需要至少 2 位家庭成员」（源码已有该空态文案，但重复成员绕过了它）
  - 截图：b2-quiz-after-start.png、b-quiz-started.png
- [P3] **对局进行中无退出/放弃按钮**：开始后只有「提交答案」，误开对局只能靠浏览器后退/导航离开
  - 截图：b-quiz-started.png

## /family-budget 家庭记账

- [P2] **删除记账条目无二次确认，一键直接删除**
  - 位置：/family-budget 本月明细行尾「×」删除按钮；实测点击后条目立即消失（无 confirm/toast），已删除的账目无法恢复
  - 期望：删除前弹确认（金额、分类、日期展示在确认框中）
  - 复现：记一笔 → 点明细行「×」→ 条目直接消失
  - 截图：b2-budget-after-save2.png、b2-budget-after-delete.png
- 说明：金额/分类校验（"请输入金额"/"请选择分类"）、分类下拉（增强组件可搜索）、月度汇总（收入/支出/结余 ±0.00 格式）、上月/下月切换、搜索均正常；创建后汇总实时更新正确（支出 -1.00 → 结余 -1.00）

## /onboarding 欢迎引导

- 说明：步骤条（1 欢迎 / 2 AI 配置 / 3 完成）、「配置完成！您的家庭知识库已就绪」、CTA 按钮「进入知识库 →」（button 非 link，点击正常跳转 `/`）均正常；已配置状态下可随时访问该页，显示完成态，可接受
- 截图：b-onboarding.png

## /ai-metrics AI 性能监控

- [P3] **趋势图 X 轴标签稀疏且无图例**：「最近 7 天趋势」仅标注 08-07/08-10/08-11 三个日期（跳过了 08-08/09），柱状图 tooltip 才有完整数据；用户无法一眼看出柱子含义（次数/耗时）
  - 截图：b-ai-metrics.png、b2-aimetrics-30d2.png
- 数据正确性抽查通过：总调用 60 = 44(DeepSeek)+15(OpenVINO)+1(embedding)；30 天视图 151 次；模型排行各 provider 次数合计一致；「最佳 Provider」与排行数据吻合
- 说明：1 天/7 天/30 天切换正常，表格/排行数据格式（ms、TPS、K Token）一致

## 响应式抽查（375×812）

- [P3] **AI 状态徽章（ai-status-container）左边缘越界 -19px**：/family、/dashboard、/checkin 三页均出现固定定位元素部分移出屏幕左侧（left:-19, right:157），在 375px 宽度下观感是元素被切掉一角
  - 截图：b-mobile-_family.png、b-mobile-_dashboard.png、b-mobile-_checkin.png
- 三页均无横向滚动（scrollWidth=375=clientWidth）；移动端 ☰ 菜单（40×40，位于 (15,15)）点击展开导航正常
- 截图：b-mobile-menu-open.png

---

## 测试过程中创建的数据（供清理，未自行删除）

按约束记录（均为北京时间 13:29 左右由本审计产生，SQLite 存 UTC 所以时间戳为 05:29 UTC）：

| 表 | Id | 内容 | 清理 SQL（如需） |
|---|---|---|---|
| StudyActivities | 1 | LearnerId=1, activity=study, result=remember, CreatedAt=2026-08-11 05:29:04 | `DELETE FROM StudyActivities WHERE Id=1;` |
| Achievements | 1–3 | LearnerId=1：first_step / accuracy_80 / early_bird | `DELETE FROM Achievements WHERE Id IN (1,2,3);` |
| CheckinMakeupRecords | 2 | MakeupDate=2026-08-08, VaultId=d5c1e389…, CreatedAt=05:29:12 | `DELETE FROM CheckinMakeupRecords WHERE Id=2;` |
| 记账 | — | 创建「支出 1.00 餐饮」后已当场删除，transactions.json 已恢复 `[]`，无需清理 | — |

数据库路径：`%USERPROFILE%\.baihua\db\family.db`（BAIHUA_HOME）；注意 `data/family.db`、`out/native/family/data/family.db` 均非运行中实例使用的库（后者为空文件，勿改）。

## 本组最值得修的 Top 5

1. **[P1] 重复成员数据**（LearnerId 1&2 同名「小明」）— 污染家庭首页/看板/成就/互考/排行全部页面，甚至允许「小明 vs 小明」互考；先清数据，再在创建时加重名校验（或提供去重/改名入口）。
2. **[P1] 「闻鸡起舞」成就按 UTC 判时** — 下午学习即解锁「早上 6 点前」成就，成就体系可信度受损；改为北京时间 Hour < 6。
3. **[P2] 打卡清单显示原始卡片 ID**（`卡片 FE128CBB29200CEE`）— 家长完全无法知道孩子学了什么；应展示卡片问题/标题。
4. **[P2] 家庭排行默认「孩子榜」空数据** — 单成员（默认=家长）家庭打开就是空榜，需勾选「显示全家排行」才见数据；默认改「全家」或提供角色设置。
5. **[P2] 记账删除无确认** — 点「×」直接删账目且无撤销，财务数据误删风险高；加确认弹窗（或撤销能力）。

---

## 审计脚本（可复用，均在 tests/ux-audit/）

- `scan-b.js` — 10 页批量截图+文本+元素清单+错误收集 → `scan-b-report.json`
- `interact-b.js` / `interact-b2.js` — 核心流程交互（打卡/答题/记账/互考/成就/筛选/排行）
- `responsive-b.js` — 375×812 响应式抽查
- `invest1.js` / `probe-select.js` / `checkin2.js` / `probe-final.js` / `probe-final2.js` — 专项取证
