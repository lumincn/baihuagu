// C组未登录行为验证：不带 cookie 访问 /settings 与 /local-models，应跳 /login?returnUrl=...
const { chromium } = require('playwright-core');
const { shot, BASE } = require('./login');

(async () => {
  const browser = await chromium.launch({ channel: 'msedge', headless: true, args: ['--no-sandbox'] });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 }, locale: 'zh-CN' });
  const page = await context.newPage();
  const issues = { consoleErrors: [], pageErrors: [], failedRequests: [] };
  page.on('console', m => { if (m.type() === 'error') issues.consoleErrors.push(m.text()); });
  page.on('pageerror', e => issues.pageErrors.push(String(e)));

  for (const path of ['/settings', '/local-models']) {
    await page.goto(BASE + path, { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(2500);
    const url = page.url();
    const body = await page.evaluate(() => document.body ? document.body.innerText.slice(0, 300) : '');
    console.log('UNAUTH', path, '->', url, '| body:', JSON.stringify(body.slice(0, 120)));
    await shot({ page }, 'C_unauth_' + path.replace(/\//g, '_'));
  }

  // 登录页友好性检查
  await page.goto(BASE + '/login', { waitUntil: 'domcontentloaded' });
  await page.waitForTimeout(2000);
  const loginBody = await page.evaluate(() => document.body.innerText);
  console.log('LOGIN PAGE body len:', loginBody.length);
  console.log('LOGIN PAGE has 找回/忘记/帮助:', /找回|忘记|帮助|支持/.test(loginBody));
  console.log('LOGIN PAGE full:', JSON.stringify(loginBody.slice(0, 500)));
  await shot({ page }, 'C_login_unauth');

  console.log('ISSUES:', JSON.stringify(issues));
  await browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
