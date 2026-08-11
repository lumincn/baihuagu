const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/qr-tool', { waitMs: 5000 });
    await audit.page.waitForTimeout(3000);
    // 三个容器的内容
    for (const id of ['server-qr-container', 'aikey-qr-container', 'general-qr-container']) {
      const info = await audit.page.evaluate((sel) => {
        const el = document.getElementById(sel);
        if (!el) return { exists: false };
        return { exists: true, html: el.innerHTML.slice(0, 200), children: el.children.length };
      }, id);
      console.log(id, JSON.stringify(info));
    }
    console.log('console errors:', JSON.stringify(audit.issues.consoleErrors.slice(0, 5)));
    // 通用二维码输入框选择器探测
    const inputs = await audit.page.locator('input').allInnerTexts().catch(() => []);
    const inputTypes = await audit.page.evaluate(() => Array.from(document.querySelectorAll('input')).map(i => ({ cls: i.className, ph: i.placeholder, type: i.type })));
    console.log('inputs:', JSON.stringify(inputTypes));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
