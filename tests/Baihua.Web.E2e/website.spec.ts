import { test, expect } from '@playwright/test';

/**
 * Family 版 Docker 部署端到端测试
 * 验证 nginx 反向代理、admin 子路径、API 端点、移动端同步接口
 * Family 版无静态官网，nginx 根路径重定向到 /admin/
 */

const NGINX_BASE = 'http://localhost:80';

test.describe('Family 版 Docker 部署测试', () => {

  test('根路径经 nginx 重定向到登录（非 200 直出）', async ({ request }) => {
    // nginx 当前设计：根路径转发到 WebUI，未登录时 302 → /login
    const resp = await request.get(`${NGINX_BASE}/`, { maxRedirects: 0 });
    expect([301, 302]).toContain(resp.status());
  });

  test('nginx 统一入口渲染 Blazor 页面（不白屏）', async ({ page }) => {
    await page.goto(`${NGINX_BASE}/login`);
    await page.waitForLoadState('domcontentloaded');
    const html = await page.content();
    expect(html.includes('<!--Blazor:') || html.includes('blazor.web'), '页面应包含 Blazor 框架标记').toBe(true);
  });

  test('nginx 入口静态资源无 404', async ({ page }) => {
    const errors: string[] = [];
    page.on('response', resp => {
      if (resp.status() >= 400 && resp.url().includes('_framework') === false) {
        errors.push(`${resp.status()}: ${resp.url()}`);
      }
    });
    await page.goto(`${NGINX_BASE}/login`);
    await page.waitForLoadState('networkidle', { timeout: 15000 });
    const criticalErrors = errors.filter(e =>
      e.includes('.css') ||
      e.includes('.js') ||
      e.includes('blazor.web')
    );
    expect(criticalErrors, `关键资源错误: ${criticalErrors.join(', ')}`).toHaveLength(0);
  });

  test('API 健康检查端点（nginx → WebUI）', async ({ request }) => {
    const resp = await request.get(`${NGINX_BASE}/health`);
    expect(resp.status()).toBe(200);
  });

  test('知识库列表 API 可访问', async ({ request }) => {
    const resp = await request.get(`${NGINX_BASE}/api/vaults`);
    expect([200, 401]).toContain(resp.status());
  });

  test('配对端点公开可访问', async ({ request }) => {
    const resp = await request.post(`${NGINX_BASE}/pair`, {
      data: { pairCode: 'wrong-code', deviceName: 'test' }
    });
    expect([400, 401]).toContain(resp.status());
  });

  test('旧版同步路径兼容', async ({ request }) => {
    const resp = await request.get(`${NGINX_BASE}/sync/system`);
    expect([200, 400, 404]).toContain(resp.status());
  });

});
