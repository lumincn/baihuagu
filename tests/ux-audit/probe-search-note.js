const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/search', { waitMs: 6000 });
    await audit.page.locator('.search-box input').first().fill('过敏');
    await audit.page.keyboard.press('Enter');
    await audit.page.waitForTimeout(4000);
    const links = await audit.page.evaluate(() => Array.from(document.querySelectorAll('a[href*="/note"]')).map(a => a.getAttribute('href')).slice(0, 5));
    console.log('笔记链接:', JSON.stringify(links));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
