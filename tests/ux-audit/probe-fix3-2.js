const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    // ai-drawing ORB 重测
    await openPage(audit, '/ai-drawing', { waitMs: 3500 });
    await audit.page.waitForTimeout(1500);
    const orbErr = audit.issues.failedRequests.filter(r => r.includes('ORB'));
    console.log('ai-drawing ORB 错误数:', orbErr.length);
    // note Anki 按钮
    await openPage(audit, '/note?id=' + encodeURIComponent('病因病机/风邪与过敏的关系') + '&vaultId=' + encodeURIComponent('中医抗敏'), { waitMs: 3000 });
    const body = await audit.page.locator('body').innerText().catch(() => '');
    console.log('note 页面含 Anki:', body.includes('Anki'), '| 含标题:', body.split('\n').slice(0, 8).join('|'));
    console.log('note 按钮数:', await audit.page.locator('button', { hasText: 'Anki' }).count());
    // local-models 骨架
    await openPage(audit, '/local-models', { waitMs: 800 });
    const spinner = await audit.page.locator('.loading-spinner').innerText().catch(() => 'NO_SPINNER');
    console.log('loading-spinner 文本:', JSON.stringify(spinner.slice(0, 60)));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
