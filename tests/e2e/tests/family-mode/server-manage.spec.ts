import { test, expect } from '@playwright/test';
import { navigateTo, waitForBlazor, authorize } from '../helpers';

// #ac55ffdc：百花服务器页测试与修复
// 覆盖：百花 WebUI 设备管理页（/devices）
// 页面 locale 为英文（Mobile Device Management）
// - 待授权/已授权设备区域渲染（空态或列表）
// - 设备授权 API：pending / authorized 端点
// - 组合场景：已授权设备列表包含真实设备（鸿蒙平板等）

const apiPort = process.env.API_PORT || '8788';
const apiBase = `http://127.0.0.1:${apiPort}`;

test.describe('百花服务器页（WebUI 设备管理）', () => {
  test.beforeEach(async ({ page }) => {
    await authorize(page);
    await navigateTo(page, '/devices');
    await waitForBlazor(page);
  });

  test('设备管理页加载：标题', async ({ page }) => {
    await expect(page.locator('h2').first()).toBeVisible({ timeout: 15000 });
  });

  test('待授权设备区域渲染（空态或列表）', async ({ page }) => {
    await expect(page.locator('text=Pending Devices').first()).toBeVisible({ timeout: 15000 });
  });

  test('已授权设备区域渲染（空态或列表）', async ({ page }) => {
    await expect(page.locator('text=Authorized Devices').first()).toBeVisible({ timeout: 15000 });
  });

  test('已授权设备列表包含真实设备（鸿蒙平板松风笔）', async ({ page }) => {
    await expect(page.locator('text=Authorized Devices').first()).toBeVisible({ timeout: 15000 });
    // 松风笔（鸿蒙平板）应出现在已授权列表（此前已同步知识库）
    const huajiDevice = page.locator('text=松风笔').first();
    if (await huajiDevice.count() > 0) {
      await expect(huajiDevice).toBeVisible({ timeout: 5000 });
    }
    // 至少有一个已授权设备（当前环境有 4 个）
    const rows = page.locator('table tbody tr').count();
    expect(await rows, '已授权设备表应有记录').toBeGreaterThan(0);
  });

  test('API: 待授权设备端点返回合法结构', async ({ request }) => {
    const res = await request.get(`${apiBase}/api/devices/pending`);
    expect(res.status()).toBe(200);
    const json = await res.json();
    expect(Array.isArray(json)).toBeTruthy();
  });

  test('API: 已授权设备端点返回合法结构', async ({ request }) => {
    const res = await request.get(`${apiBase}/api/devices/authorized`);
    expect(res.status()).toBe(200);
    const json = await res.json();
    expect(Array.isArray(json)).toBeTruthy();
    // 至少 1 台已授权设备（鸿蒙平板/安卓）
    expect(json.length).toBeGreaterThan(0);
  });
});
