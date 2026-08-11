const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    // /family 成员卡片
    await openPage(audit, '/family', { waitMs: 3000 });
    const familyText = await audit.page.locator('body').innerText().catch(() => '');
    const familyCount = (familyText.match(/小明/g) || []).length;
    console.log('/family 小明出现次数:', familyCount, familyCount <= 2 ? '✅' : '❌（仍重复）');
    // /dashboard 成员筛选
    await openPage(audit, '/dashboard', { waitMs: 3000 });
    const dashText = await audit.page.locator('body').innerText().catch(() => '');
    const dashCount = (dashText.match(/小明/g) || []).length;
    console.log('/dashboard 小明出现次数:', dashCount, dashCount <= 2 ? '✅' : '❌（仍重复）');
    // API 直接验证
    const resp = await audit.page.request.get('http://127.0.0.1:8788/api/achievements/learners');
    const learners = await resp.json();
    console.log('API 成员列表:', JSON.stringify(learners.map(l => ({ id: l.id, name: l.name }))));
    const dupNames = learners.filter(l => l.name === '小明').length;
    console.log('API 重名数:', dupNames, dupNames === 1 ? '✅' : '❌');
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
