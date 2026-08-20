import { test, expect } from '@playwright/test';
import { navigateTo, waitForBlazor } from '../helpers';

/**
 * Embedding 配置提供商切换联动测试
 * 验证：点击 ollama/openai/siliconflow/custom 按钮后，Model/BaseUrl 输入框联动预填
 * 关键场景：从 custom（本地 OpenVINO）切换到其他提供商时必须预填默认值（历史 bug）
 */
test.describe('Embedding 配置提供商切换', () => {
  test.beforeEach(async ({ page }) => {
    await navigateTo(page, '/settings');
    await waitForBlazor(page);
  });

  // 定位 Embedding 配置区（页面有多个 section，找标题含"Embedding"的）
  const embeddingSection = (page: import('@playwright/test').Page) =>
    page.locator('.embedding-config-section', { hasText: 'Embedding' }).last();

  async function clickProvider(page: import('@playwright/test').Page, label: string) {
    const section = embeddingSection(page);
    await section.getByRole('button', { name: label }).click();
    await page.waitForTimeout(300); // Blazor 事件处理
  }

  async function getModelBaseUrl(page: import('@playwright/test').Page) {
    const section = embeddingSection(page);
    const inputs = section.locator('input.form-control');
    // 顺序：Model(0) -> BaseUrl(1) -> ApiKey(2)
    const model = await inputs.nth(0).inputValue();
    const baseUrl = await inputs.nth(1).inputValue();
    return { model, baseUrl };
  }

  test('默认加载显示已保存配置（custom/本地 OpenVINO）', async ({ page }) => {
    const section = embeddingSection(page);
    await expect(section).toBeVisible();
    // 配置异步加载，轮询等待输入框有值（避免 flaky）
    await expect.poll(async () => (await getModelBaseUrl(page)).model.length).toBeGreaterThan(0);
    await expect.poll(async () => (await getModelBaseUrl(page)).baseUrl.length).toBeGreaterThan(0);
  });

  test('从 custom 切换到 Ollama 联动预填 nomic-embed-text', async ({ page }) => {
    await clickProvider(page, /Ollama/);
    const { model, baseUrl } = await getModelBaseUrl(page);
    expect(model).toBe('nomic-embed-text');
    expect(baseUrl).toBe('http://localhost:11434/v1');
  });

  test('从 custom 切换到 OpenAI 联动预填 text-embedding-3-small', async ({ page }) => {
    await clickProvider(page, /OpenAI/);
    const { model, baseUrl } = await getModelBaseUrl(page);
    expect(model).toBe('text-embedding-3-small');
    expect(baseUrl).toBe('https://api.openai.com/v1');
  });

  test('从 custom 切换到 SiliconFlow 联动预填 bge-large-zh', async ({ page }) => {
    await clickProvider(page, /SiliconFlow|硅基流动/);
    const { model, baseUrl } = await getModelBaseUrl(page);
    expect(model).toBe('BAAI/bge-large-zh-v1.5');
    expect(baseUrl).toBe('https://api.siliconflow.cn/v1');
  });

  test('从 custom 切换到本地 OpenVINO 联动预填 bge-small-zh + 127.0.0.1:8000', async ({ page }) => {
    await clickProvider(page, /OpenVINO/);
    const { model, baseUrl } = await getModelBaseUrl(page);
    expect(model).toBe('bge-small-zh');
    expect(baseUrl).toBe('http://127.0.0.1:8000/v3');
  });

  test('切换到 custom 保留当前值', async ({ page }) => {
    // 先切到 ollama 拿到默认值
    await clickProvider(page, /Ollama/);
    const ollama = await getModelBaseUrl(page);
    // 再切到 custom
    await clickProvider(page, /Custom|自定义/);
    const custom = await getModelBaseUrl(page);
    // custom 保留之前的值（不预填、不清空）
    expect(custom.model).toBe(ollama.model);
    expect(custom.baseUrl).toBe(ollama.baseUrl);
  });

  test('非 custom 之间切换：未自定义过则预填新默认', async ({ page }) => {
    // 先切到 ollama（从 custom 出发会预填）
    await clickProvider(page, /Ollama/);
    // 再切到 openai —— 当前是 ollama 默认值（未自定义），应预填 openai 默认
    await clickProvider(page, /OpenAI/);
    const { model, baseUrl } = await getModelBaseUrl(page);
    expect(model).toBe('text-embedding-3-small');
    expect(baseUrl).toBe('https://api.openai.com/v1');
  });
});
