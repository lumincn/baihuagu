import { test, expect } from '@playwright/test';

/**
 * 移动端管理页 E2E 测试
 * 注：设备注册后页面异步刷新测试已移到手动测试列表
 */

const apiPort = process.env.API_PORT || '8788';
const apiBase = `http://127.0.0.1:${apiPort}`;

test.describe('移动端管理', () => {

  test('OneHop 发现状态端点应返回正确服务信息', async ({ request }) => {
    const res = await request.get(`${apiBase}/api/onehop/status`);
    expect(res.status()).toBe(200);
    const json = await res.json();
    expect(json.ServiceId).toBe('com.lumin.huaji.sync');
    expect(json.Port).toBe(8789);
    expect(json.IsAvailable).toBe(true);
  });
});
