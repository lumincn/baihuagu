import { Page } from '@playwright/test';

export async function navigateTo(page: Page, path: string) {
  const base = process.env.PLAYWRIGHT_BASE_URL || 'http://127.0.0.1:5177';
  const url = base.replace(/\/$/, '') + path;
  await page.goto(url);
  await page.waitForLoadState('networkidle');
}

export async function waitForBlazor(page: Page) {
  // Wait for Blazor to finish rendering: wait for network idle then for dashboard-specific container
  await page.waitForLoadState('networkidle');
  // Wait up to 5s for Blazor-rendered root element to appear
  try {
    await page.waitForSelector('.dashboard-page', { timeout: 5000 });
  } catch {
    // fallback short delay
    await page.waitForTimeout(500);
  }
}
