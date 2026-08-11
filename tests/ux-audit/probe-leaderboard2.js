const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/leaderboard', { waitMs: 3500 });
    const btns = await audit.page.locator('button').allInnerTexts().catch(() => []);
    console.log('按钮:', JSON.stringify(btns.filter(b => b.trim()).slice(0, 15)));
    const body = await audit.page.locator('body').innerText().catch(() => '');
    console.log('页面:', body.replace(/\n/g, '|').slice(0, 300));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
