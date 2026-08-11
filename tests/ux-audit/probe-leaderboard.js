const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/leaderboard', { waitMs: 3000 });
    // 勾选"显示全家排行"或切 tab
    const famTab = audit.page.locator('button', { hasText: '家庭排行' }).first();
    if (await famTab.count()) { await famTab.click(); await audit.page.waitForTimeout(1200); }
    const allChk = audit.page.locator('input[type="checkbox"]').first();
    if (await allChk.count()) { await allChk.click().catch(() => {}); await audit.page.waitForTimeout(1200); }
    const sv = audit.page.locator('.score-value').first();
    console.log('score-value 数量:', await sv.count());
    if (await sv.count()) {
      const title = await sv.getAttribute('title').catch(() => '');
      console.log('title:', JSON.stringify(title));
      const text = await sv.innerText().catch(() => '');
      console.log('分数值:', text);
    }
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
