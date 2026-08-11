const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    const resp = await audit.page.request.get('http://127.0.0.1:8788/api/achievements/learners');
    console.log('API 原始:', (await resp.text()).slice(0, 300));
    // dashboard 成员筛选下拉选项
    await openPage(audit, '/dashboard', { waitMs: 3000 });
    const memberOpts = await audit.page.evaluate(() => {
      const sels = Array.from(document.querySelectorAll('select'));
      return sels.map(s => Array.from(s.options).map(o => o.text)).filter(a => a.some(t => t.includes('小明')));
    });
    console.log('含小明的下拉:', JSON.stringify(memberOpts));
    // 成员选择器（MemberSelector 组件按钮）
    const memberBtns = await audit.page.locator('button', { hasText: '小明' }).allInnerTexts().catch(() => []);
    console.log('小明按钮数:', memberBtns.length, JSON.stringify(memberBtns.map(b => b.replace(/\n/g, '').slice(0, 20))));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
