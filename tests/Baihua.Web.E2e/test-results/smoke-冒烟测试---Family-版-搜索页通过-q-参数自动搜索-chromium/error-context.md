# Instructions

- Following Playwright test failed.
- Explain why, be concise, respect Playwright best practices.
- Provide a snippet of code with the fix, if possible.

# Test info

- Name: smoke.spec.ts >> 冒烟测试 - Family 版 >> 搜索页通过 ?q= 参数自动搜索
- Location: smoke.spec.ts:163:7

# Error details

```
Error: expect(locator).toBeVisible() failed

Locator: locator('text=/搜索结果/')
Expected: visible
Timeout: 20000ms
Error: element(s) not found

Call log:
  - Expect "toBeVisible" with timeout 20000ms
  - waiting for locator('text=/搜索结果/')

```

```yaml
- navigation:
  - link " 移动端管理":
    - /url: devices
  - link " AI 生成知识库":
    - /url: generate
  - button "📚 知识库"
  - button "👨‍👩‍👧 家庭"
  - button "🤖 AI 实验室"
  - link " 首页":
    - /url: ""
  - link " 知识库浏览":
    - /url: browse
  - link " 搜索":
    - /url: search
  - link " 记忆卡片":
    - /url: cards
  - link " 知识库管理":
    - /url: vaults
  - link " 错误日志":
    - /url: log-errors
  - separator
  - text: 模式 🔧 专业 👤 简易
- main:
  - link "🤖 AI 默认模型 ●":
    - /url: settings
  - text: 百花（寻芳居） - 家庭知识港湾
  - img "百花"
  - button ""
  - button ""
  - article:
    - heading "🔍 搜索" [level=1]
    - alert:
      - strong: 💡 提示：
      - text: Obsidian 桌面客户端未启动
      - paragraph:
        - text: 启动 Obsidian 后可使用其强大的搜索功能检索本地知识库，支持全文检索、标签搜索和链接关系查询。
        - link "下载 Obsidian":
          - /url: https://obsidian.md/download
      - button "Close"
    - textbox "输入关键词搜索本地笔记...": 鼻渊
    - button "搜索"
    - text: 😕
    - paragraph: 未找到相关结果
- button ""
- text: "}"
```

# Test source

```ts
  75  |   });
  76  | 
  77  |   test('知识库页面加载完成', async ({ page }) => {
  78  |     const token = await getCliToken();
  79  |     await page.goto(`/vaults?cli-token=${token}`);
  80  |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  81  |     // 页面应显示知识库相关内容
  82  |     const hasVaultContent = await page.locator('main').isVisible();
  83  |     expect(hasVaultContent).toBe(true);
  84  |   });
  85  | 
  86  |   test('窄屏菜单可展开', async ({ page }) => {
  87  |     const token = await getCliToken();
  88  |     await page.setViewportSize({ width: 375, height: 812 });
  89  |     await page.goto(`/?cli-token=${token}`);
  90  |     await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => {});
  91  |     await expect(page.locator('main')).toBeVisible({ timeout: 20000 });
  92  |     // 首次配置（Onboarding）页面没有汉堡菜单，跳过
  93  |     const isOnboarding = await page.locator('text=首次配置').first().isVisible().catch(() => false);
  94  |     if (isOnboarding) {
  95  |       test.skip('首次配置页面无汉堡菜单');
  96  |       return;
  97  |     }
  98  |     const menuBtn = page.locator('button.mobile-menu-toggle');
  99  |     await expect(menuBtn).toBeVisible({ timeout: 10000 });
  100 |     await menuBtn.click();
  101 |     await expect(page.locator('nav.sidebar.open')).toBeVisible();
  102 |   });
  103 | 
  104 |   test('日志配置页面加载', async ({ page }) => {
  105 |     const token = await getCliToken();
  106 |     await page.goto(`/log-settings?cli-token=${token}`);
  107 |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  108 |     await expect(page.locator('h1', { hasText: '日志配置' })).toBeVisible();
  109 |   });
  110 | 
  111 |   test('AI 设置页面加载', async ({ page }) => {
  112 |     const token = await getCliToken();
  113 |     await page.goto(`/settings?cli-token=${token}`);
  114 |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  115 |     await expect(page.locator('text=AI 提供商配置')).toBeVisible();
  116 |   });
  117 | 
  118 |   test('每日一帖页面加载', async ({ page }) => {
  119 |     const token = await getCliToken();
  120 |     await page.goto(`/daily-card?cli-token=${token}`);
  121 |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  122 |     await expect(page.locator('h1', { hasText: '每日一帖' })).toBeVisible();
  123 |   });
  124 | 
  125 |   test('成就墙页面加载', async ({ page }) => {
  126 |     const token = await getCliToken();
  127 |     await page.goto(`/achievements?cli-token=${token}`);
  128 |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  129 |     await expect(page.locator('h1', { hasText: '成就墙' })).toBeVisible();
  130 |   });
  131 | 
  132 |   test('赛舟榜页面加载', async ({ page }) => {
  133 |     const token = await getCliToken();
  134 |     await page.goto(`/leaderboard?cli-token=${token}`);
  135 |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  136 |     await expect(page.locator('h1', { hasText: '家庭赛舟榜' })).toBeVisible();
  137 |   });
  138 | 
  139 |   test('家长看板页面加载', async ({ page }) => {
  140 |     const token = await getCliToken();
  141 |     await page.goto(`/dashboard?cli-token=${token}`);
  142 |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  143 |     await expect(page.locator('h1', { hasText: '家长看板' })).toBeVisible();
  144 |   });
  145 | 
  146 |   test('AI 对话页面加载', async ({ page }) => {
  147 |     const token = await getCliToken();
  148 |     await page.goto(`/messages?cli-token=${token}`);
  149 |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  150 |     await expect(page.locator('h1', { hasText: 'AI 对话' })).toBeVisible();
  151 |   });
  152 | 
  153 |   test('硬件评测页面显示 INT8/INT4 算力', async ({ page }) => {
  154 |     const token = await getCliToken();
  155 |     await page.goto(`/hardware-benchmark?cli-token=${token}`);
  156 |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  157 |     await expect(page.locator('th', { hasText: 'INT8 算力' })).toBeVisible();
  158 |     await expect(page.locator('th', { hasText: 'INT4 算力' })).toBeVisible();
  159 |     const fp16Cells = page.locator('th', { hasText: 'FP16 算力' });
  160 |     await expect(fp16Cells).toHaveCount(0);
  161 |   });
  162 | 
  163 |   test('搜索页通过 ?q= 参数自动搜索', async ({ page }) => {
  164 |     // 先用 cli-token 登录获取 cookie
  165 |     const token = await getCliToken();
  166 |     await page.goto(`/?cli-token=${token}`);
  167 |     await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
  168 |     // 登录后 cookie 已设置，再导航到搜索页（不带 cli-token，带 q 参数）
  169 |     await page.goto('/search?q=%E9%BC%BB%E6%B8%8A');
  170 |     await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
  171 |     // 搜索框应显示关键字的初始值
  172 |     const searchInput = page.locator('input[placeholder*="搜索"]');
  173 |     await expect(searchInput).toHaveValue('鼻渊', { timeout: 15000 });
  174 |     // 应自动触发搜索并显示结果数（如"搜索结果 (21 条)"）
> 175 |     await expect(page.locator('text=/搜索结果/')).toBeVisible({ timeout: 20000 });
      |                                               ^ Error: expect(locator).toBeVisible() failed
  176 |   });
  177 | 
  178 |   test('OpenClaw 页面加载', async ({ page }) => {
  179 |     const token = await getCliToken();
  180 |     await page.goto(`/openclaw?cli-token=${token}`);
  181 |     await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
  182 |     await expect(page.locator('text=OpenClaw 任务委派')).toBeVisible();
  183 |   });
  184 | 
  185 |   test('能力评估 API 返回正确格式', async ({ request }) => {
  186 |     const resp = await request.get(`${TASKRUNNER_BASE}/api/capability`);
  187 |     expect(resp.status()).toBe(200);
  188 |     const data = await resp.json();
  189 |     expect(data).toHaveProperty('level');
  190 |     expect(data).toHaveProperty('availableFeatures');
  191 |     expect(data).toHaveProperty('restrictedFeatures');
  192 |     expect(Array.isArray(data.availableFeatures)).toBe(true);
  193 |   });
  194 | 
  195 |   test('模型推荐只返回 INT4/INT8 模型', async ({ request }) => {
  196 |     const resp = await request.get(`${TASKRUNNER_BASE}/api/local-models/recommend`);
  197 |     expect(resp.status()).toBe(200);
  198 |     const models = await resp.json();
  199 |     expect(Array.isArray(models)).toBe(true);
  200 |     expect(models.length).toBeGreaterThan(0);
  201 |     for (const m of models) {
  202 |       const q = (m.quantization || '').toUpperCase();
  203 |       const isInt4Or8 = q.includes('Q4') || q.includes('Q8') || q.includes('INT4') || q.includes('INT8');
  204 |       expect(isInt4Or8, `模型 ${m.name} 的精度 ${m.quantization} 不是 INT4/INT8`).toBe(true);
  205 |     }
  206 |   });
  207 | 
  208 | });
  209 | 
```