import { test, expect } from '@playwright/test';

/**
 * Family 版部署验证测试
 * 验证本地或局域网部署的服务是否正常运行
 * 适用于 Docker 部署后验证
 */

const WEBUI_BASE = 'http://127.0.0.1:5177';
const FAMILY_BASE = 'http://127.0.0.1:8788';

test.describe('Family 版部署验证', () => {

  test('Baihua.Family 健康检查', async ({ request }) => {
    const resp = await request.get(`${FAMILY_BASE}/health`);
    expect(resp.status()).toBe(200);
  });

  test('Baihua.Family API 能力评估', async ({ request }) => {
    const resp = await request.get(`${FAMILY_BASE}/api/capability`);
    expect(resp.status()).toBe(200);
    const data: any = await resp.json();
    // 兼容 PascalCase（当前）与 camelCase（旧版）
    expect(data.Level ?? data.level).toBeTruthy();
    expect(data.AvailableFeatures ?? data.availableFeatures).toBeTruthy();
    expect(Array.isArray(data.AvailableFeatures ?? data.availableFeatures)).toBe(true);
  });

  test('WebUI 健康检查', async ({ request }) => {
    const resp = await request.get(`${WEBUI_BASE}/health`);
    expect(resp.status()).toBe(200);
  });

  test('WebUI Blazor 框架加载', async ({ page }) => {
    await page.goto(`${WEBUI_BASE}/login`);
    const html = await page.content();
    const hasBlazor = html.includes('<!--Blazor:') || html.includes('blazor.web');
    expect(hasBlazor, '页面应包含 Blazor 框架标记').toBe(true);
  });

  test('知识库列表 API 可访问', async ({ request }) => {
    const resp = await request.get(`${FAMILY_BASE}/api/vaults`);
    expect([200, 401]).toContain(resp.status());
  });

  test('OpenObserve 可访问（如果启用）', async ({ request }) => {
    const resp = await request.get('http://127.0.0.1:5082/api/status', { maxRedirects: 5 }).catch(() => null);
    // OpenObserve 是可选的，404 或连接失败不算错误
    if (resp) {
      expect([200, 401, 404]).toContain(resp.status());
    }
  });

});
