// 共享审计助手：cli-token 免密登录 + Blazor 页面打开 + 错误收集（百花 WebUI 5177）
// 用法: const { startAudit, openPage, shot } = require('./login');
const { chromium } = require('playwright-core');

const BASE = 'http://127.0.0.1:5177';

/**
 * 启动浏览器（系统 Edge headless）并完成 cli-token 登录。
 * 返回 audit 会话对象：{ browser, context, page, issues, BASE }
 *  issues 自动收集 console error / pageerror / 请求失败，供每个页面审计后读取。
 */
async function startAudit({ headless = true, viewport = { width: 1440, height: 900 } } = {}) {
  const browser = await chromium.launch({
    channel: 'msedge',
    headless,
    args: ['--no-sandbox', '--disable-setuid-sandbox'],
  });
  const context = await browser.newContext({ viewport, locale: 'zh-CN', acceptDownloads: true });
  const page = await context.newPage();

  const issues = { consoleErrors: [], pageErrors: [], failedRequests: [] };
  page.on('console', (msg) => { if (msg.type() === 'error') issues.consoleErrors.push(msg.text()); });
  page.on('pageerror', (err) => issues.pageErrors.push(String(err)));
  page.on('requestfailed', (req) => issues.failedRequests.push(`${req.method()} ${req.url()} :: ${req.failure()?.errorText || '?'}`));

  const resp = await page.request.post(BASE + '/api/auth/cli-token');
  if (!resp.ok()) throw new Error(`cli-token 获取失败: ${resp.status()}`);
  const { token } = await resp.json();
  if (!token) throw new Error('cli-token 响应无 token');

  await page.goto(BASE + '/?cli-token=' + encodeURIComponent(token), { waitUntil: 'domcontentloaded' });
  await page.waitForLoadState('networkidle').catch(() => {});
  return { browser, context, page, issues, BASE };
}

/**
 * 打开指定路径，等待 Blazor 渲染完成。
 * 返回 { url, ok }；ok=false 表示疑似白屏/渲染失败（本身就是审计发现）。
 * 错误一律记录到 issues 而不是抛异常，保证审计流程走完所有页面。
 */
async function openPage(audit, path, { waitSelector = null, waitMs = 2000, timeout = 20000 } = {}) {
  const { page, issues } = audit;
  const url = audit.BASE + path;
  let gotoErr = null;
  await page.goto(url, { waitUntil: 'domcontentloaded' }).catch((e) => { gotoErr = String(e); issues.pageErrors.push(`goto ${path}: ${e}`); });
  await page.waitForLoadState('networkidle', { timeout }).catch(() => {});
  if (waitSelector) {
    await page.waitForSelector(waitSelector, { timeout }).catch(() => {
      issues.pageErrors.push(`waitForSelector ${waitSelector} on ${path} 超时`);
    });
  } else {
    await page.waitForTimeout(waitMs);
  }
  // 检测白屏/异常
  let ok = true;
  try {
    const bodyText = await page.evaluate(() => document.body ? document.body.innerText.trim() : '');
    if (!bodyText) ok = false;
  } catch { ok = false; }
  const errUi = await page.locator('#blazor-error-ui').isVisible().catch(() => false);
  if (errUi) issues.pageErrors.push(`${path}: blazor-error-ui 可见（Blazor 错误条）`);
  return { url: page.url(), ok, gotoErr };
}

/** 截图存档（审计证据），返回文件路径 */
async function shot(audit, name, dir = 'shots') {
  const file = `${dir}/${name}.png`;
  await audit.page.screenshot({ path: file, fullPage: true }).catch(() => {});
  return file;
}

module.exports = { startAudit, openPage, shot, BASE };
