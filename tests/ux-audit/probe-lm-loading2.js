const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/local-models', { waitMs: 500 });
    let t = '', found = false;
    for (let i = 0; i < 12; i++) {
      await audit.page.waitForTimeout(1000);
      t = await audit.page.locator('.loading-spinner').innerText().catch(() => '');
      if (t) { found = true; break; }
    }
    console.log('骨架文本:', found ? JSON.stringify(t.replace(/\n/g, ' ').trim().slice(0, 60)) : '12s 内未出现');
    const hasDetail = t.includes('检测') || t.includes('扫描') || t.includes('刷新');
    console.log('含具体文案:', hasDetail ? '✅' : '❌');
    await audit.page.screenshot({ path: 'shots/fix3-lm-skeleton.png' });
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
