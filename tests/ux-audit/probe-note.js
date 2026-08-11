const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    // note 页面完整状态
    await openPage(audit, '/note?id=' + encodeURIComponent('病因病机/风邪与过敏的关系') + '&vaultId=' + encodeURIComponent('中医抗敏'), { waitMs: 3000 });
    const body = await audit.page.locator('body').innerText().catch(() => '');
    console.log('body 全文:', body.replace(/\n/g, ' | ').slice(0, 600));
    const errUi = await audit.page.locator('#blazor-error-ui').isVisible().catch(() => false);
    console.log('blazor-error-ui:', errUi);
    console.log('page errors:', JSON.stringify(audit.issues.pageErrors.slice(0, 3)));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
