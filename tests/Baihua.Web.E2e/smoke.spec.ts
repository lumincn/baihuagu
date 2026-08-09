import { test, expect } from '@playwright/test';

const WEBUI_BASE = 'http://127.0.0.1:5177';
const TASKRUNNER_BASE = 'http://127.0.0.1:8788';

// 获取 CLI token 用于自动认证
async function getCliToken(): Promise<string> {
  const resp = await fetch(`${WEBUI_BASE}/api/auth/cli-token`, { method: 'POST' });
  const data = await resp.json();
  if (!data.token) throw new Error(`无法获取 CLI token: ${JSON.stringify(data)}`);
  return data.token;
}

test.describe('冒烟测试 - Family 版', () => {

  test('TaskRunner 健康检查', async ({ request }) => {
    const resp = await request.get(`${TASKRUNNER_BASE}/health`);
    expect(resp.status()).toBe(200);
  });

  test('WebUI 健康检查', async ({ request }) => {
    const resp = await request.get(`${WEBUI_BASE}/health`);
    expect(resp.status()).toBe(200);
  });

  test('CLI token 认证可用', async () => {
    const token = await getCliToken();
    expect(token).toBeTruthy();
    expect(token.length).toBeGreaterThan(10);
  });

  test('首页加载成功（CLI token 认证）', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/?cli-token=${token}`);
    await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => {});
    await expect(page.locator('main')).toBeVisible({ timeout: 20000 });
    // 首页可能显示 FamilyHome 或 Onboarding 首次配置页，两者都算正常
    const hasHome = await page.locator('text=/少就是多|Less is more/').first().isVisible().catch(() => false);
    const hasOnboarding = await page.locator('text=首次配置').first().isVisible().catch(() => false);
    expect(hasHome || hasOnboarding, '首页应显示家庭首页或首次配置').toBe(true);
  });

  test('WebUI 无 404 静态资源', async ({ page }) => {
    const token = await getCliToken();
    const notFound: string[] = [];
    page.on('response', resp => {
      if (resp.status() === 404 && resp.url().includes('_framework') === false) {
        notFound.push(resp.url());
      }
    });
    await page.goto(`/?cli-token=${token}`);
    await page.waitForLoadState('networkidle');
    const critical404s = notFound.filter(u =>
      u.includes('WebUI.styles.css') ||
      u.includes('blazor.web.js') ||
      u.includes('ReconnectModal')
    );
    expect(critical404s, `关键资源 404: ${critical404s.join(', ')}`).toHaveLength(0);
  });

  test('侧边栏导航 - 点击 OpenClaw 跳转', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/?cli-token=${token}`);
    await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => {});
    await expect(page.locator('main')).toBeVisible({ timeout: 20000 });
    const ocLink = page.locator('nav a[href="openclaw"], aside a[href="openclaw"], a[href="openclaw"]').first();
    await expect(ocLink).toBeVisible({ timeout: 10000 });
    await ocLink.click();
    await expect(page).toHaveURL(/openclaw/);
    await expect(page.locator('body')).toContainText(/OpenClaw/);
  });

  test('知识库页面加载完成', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/vaults?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    // 页面应显示知识库相关内容
    const hasVaultContent = await page.locator('main').isVisible();
    expect(hasVaultContent).toBe(true);
  });

  test('窄屏菜单可展开', async ({ page }) => {
    const token = await getCliToken();
    await page.setViewportSize({ width: 375, height: 812 });
    await page.goto(`/?cli-token=${token}`);
    await page.waitForLoadState('networkidle', { timeout: 30000 }).catch(() => {});
    await expect(page.locator('main')).toBeVisible({ timeout: 20000 });
    // 首次配置（Onboarding）页面没有汉堡菜单，跳过
    const isOnboarding = await page.locator('text=首次配置').first().isVisible().catch(() => false);
    if (isOnboarding) {
      test.skip('首次配置页面无汉堡菜单');
      return;
    }
    const menuBtn = page.locator('button.mobile-menu-toggle');
    await expect(menuBtn).toBeVisible({ timeout: 10000 });
    await menuBtn.click();
    await expect(page.locator('nav.sidebar.open')).toBeVisible();
  });

  test('日志配置页面加载', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/log-settings?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('h1', { hasText: /日志配置|Log Settings/ })).toBeVisible();
  });

  test('AI 设置页面加载', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/settings?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('text=/AI 提供商配置|AI Settings/').first()).toBeVisible();
  });

  test('每日一帖页面加载', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/daily-card?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('h1', { hasText: /每日一帖|Daily Card/ })).toBeVisible();
  });

  test('成就墙页面加载', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/achievements?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('h1', { hasText: /成就墙|Achievements/ })).toBeVisible();
  });

  test('赛舟榜页面加载', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/leaderboard?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('h1', { hasText: /家庭赛舟榜|Regatta/ })).toBeVisible();
  });

  test('家长看板页面加载', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/dashboard?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('h1', { hasText: /家长看板|Dashboard/ })).toBeVisible();
  });

  test('AI 对话页面加载', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/messages?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('h1', { hasText: /AI 对话|AI Chat/ })).toBeVisible();
  });

  test('硬件评测页面显示 INT8/INT4 算力', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/hardware-benchmark?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('th', { hasText: /INT8 算力|INT8 TFLOPS/ })).toBeVisible();
    await expect(page.locator('th', { hasText: /INT4 算力|INT4 TFLOPS/ })).toBeVisible();
    const fp16Cells = page.locator('th', { hasText: /FP16 算力|FP16 TFLOPS/ });
    await expect(fp16Cells).toHaveCount(0);
  });

  test('搜索页通过 ?q= 参数自动搜索', async ({ page }) => {
    // 先用 cli-token 登录获取 cookie
    const token = await getCliToken();
    await page.goto(`/?cli-token=${token}`);
    await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
    // 登录后 cookie 已设置，再导航到搜索页（不带 cli-token，带 q 参数）
    await page.goto('/search?q=%E9%BC%BB%E6%B8%8A');
    await page.waitForLoadState('networkidle', { timeout: 15000 }).catch(() => {});
    // 搜索框应显示关键字的初始值
    const searchInput = page.locator('input[placeholder*="搜索"], input[placeholder*="Search"]');
    await expect(searchInput).toHaveValue('鼻渊', { timeout: 15000 });
    // 等待片刻后检查关键字仍保留（表示无意外刷新丢失状态）
    await page.waitForTimeout(2000);
    await expect(searchInput).toHaveValue('鼻渊', { timeout: 5000 });
  });

  test('OpenClaw 页面加载', async ({ page }) => {
    const token = await getCliToken();
    await page.goto(`/openclaw?cli-token=${token}`);
    await expect(page.locator('main')).toBeVisible({ timeout: 15000 });
    await expect(page.locator('text=/OpenClaw 任务委派|OpenClaw Task Delegation/')).toBeVisible();
  });

  test('能力评估 API 返回正确格式', async ({ request }) => {
    const resp = await request.get(`${TASKRUNNER_BASE}/api/capability`);
    expect(resp.status()).toBe(200);
    const data: any = await resp.json();
    // 兼容 PascalCase（当前）与 camelCase（旧版）
    expect(data.Level ?? data.level).toBeTruthy();
    expect(data.AvailableFeatures ?? data.availableFeatures).toBeTruthy();
    expect(Array.isArray(data.AvailableFeatures ?? data.availableFeatures)).toBe(true);
    expect(data.RestrictedFeatures ?? data.restrictedFeatures).toBeTruthy();
  });

  test('模型推荐只返回 INT4/INT8 模型', async ({ request }) => {
    const resp = await request.get(`${TASKRUNNER_BASE}/api/local-models/recommend`);
    expect(resp.status()).toBe(200);
    const models = await resp.json();
    expect(Array.isArray(models)).toBe(true);
    expect(models.length).toBeGreaterThan(0);
    for (const m of models) {
      const q = ((m.quantization ?? m.Quantization) || '').toUpperCase();
      const isInt4Or8 = q.includes('Q4') || q.includes('Q8') || q.includes('INT4') || q.includes('INT8');
      expect(isInt4Or8, `模型 ${m.name ?? m.Name} 的精度 ${m.quantization ?? m.Quantization} 不是 INT4/INT8`).toBe(true);
    }
  });

});
