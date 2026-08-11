const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/local-models', { waitMs: 500 });
    await audit.page.waitForTimeout(400);
    let t = await audit.page.locator('.loading-spinner').innerText().catch(() => '');
    if (!t) { await audit.page.waitForTimeout(600); t = await audit.page.locator('.loading-spinner').innerText().catch(() => ''); }
    console.log('骨架文本:', JSON.stringify(t.replace(/\n/g, ' ').trim().slice(0, 50)));
    const hasDetail = t.includes('检测') || t.includes('扫描') || t.includes('刷新');
    console.log('含具体文案:', hasDetail ? '✅' : '❌');
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
