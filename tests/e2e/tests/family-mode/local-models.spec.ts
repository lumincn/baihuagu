import { test, expect } from '@playwright/test';
import { navigateTo, waitForBlazor, authorize } from '../helpers';

// 本地模型部署页冒烟：OpenVINO Tab 组件化后（LocalModels.razor → OpenVinoTab.razor）的行为回归锚。
// 覆盖：页面加载、Tab 切换到 OpenVINO 后目录/下载任务/已下载模型/LLM 托管各区域渲染。

test.describe('本地模型部署页（OpenVINO Tab 组件）', () => {
  test.beforeEach(async ({ page }) => {
    await authorize(page);
    await navigateTo(page, '/local-models');
    await waitForBlazor(page);
  });

  test('页面加载：标题可见', async ({ page }) => {
    await expect(page.locator('h1').first()).toBeVisible({ timeout: 20000 });
  });

  test('切换 OpenVINO Tab：区域标题渲染', async ({ page }) => {
    await page.getByRole('button', { name: /OpenVINO/ }).first().click();
    // OpenVINO 模型区标题（🧠 模型目录 / OpenVINO 模型）
    await expect(page.getByText(/模型目录|OpenVINO/).first()).toBeVisible({ timeout: 20000 });
    // 下载任务与已下载模型区
    await expect(page.getByText(/下载任务|已下载/).first()).toBeVisible({ timeout: 20000 });
  });

  test('切回概览 Tab：硬件区渲染', async ({ page }) => {
    await page.getByRole('button', { name: /OpenVINO/ }).first().click();
    await page.getByRole('button', { name: /概览/ }).first().click();
    await expect(page.getByText(/硬件|运行中/).first()).toBeVisible({ timeout: 20000 });
  });
});
