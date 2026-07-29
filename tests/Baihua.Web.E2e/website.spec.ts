import { test, expect } from '@playwright/test';

/**
 * Family 版 Docker 部署端到端测试
 * 验证 nginx 反向代理、admin 子路径、API 端点、移动端同步接口
 * Family 版无静态官网，nginx 根路径重定向到 /admin/
 */

const NGINX_BASE = 'http://localhost:80';

test.describe('Family 版 Docker 部署测试', () => {

  test('根路径重定向到 /admin/', async ({ page }) => {
    const resp = await page.goto(`${NGINX_BASE}/`);
    // nginx 配置: location / { return 301 /admin/; }
    expect([301, 302]).toContain(resp?.status() || 0);
  });

  test('admin 管理后台不白屏', async ({ page }) => {
    await page.goto(`${NGINX_BASE}/admin/`);
    // 验证 HTML 中包含 Blazor 组件标记
    const html = await page.content();
    expect(html).toContain('<!--Blazor:');
  });

  test('admin 后台静态资源无 404', async ({ page }) => {
    const errors: string[] = [];
    page.on('response', resp => {
      if (resp.status() >= 400 && resp.url().includes('/admin/')) {
        errors.push(`${resp.status()}: ${resp.url()}`);
      }
    });
    await page.goto(`${NGINX_BASE}/admin/`);
    await page.waitForLoadState('networkidle', { timeout: 15000 });
    const criticalErrors = errors.filter(e =>
      e.includes('.css') ||
      e.includes('.js') ||
      e.includes('blazor.web')
    );
    expect(criticalErrors, `关键资源错误: ${criticalErrors.join(', ')}`).toHaveLength(0);
  });

  test('API 健康检查端点', async ({ request }) => {
    const resp = await request.get(`${NGINX_BASE}/api/health`);
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
