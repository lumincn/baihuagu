const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/qr-tool', { waitMs: 5000 });
    await audit.page.waitForTimeout(2000);
    const body = await audit.page.locator('body').innerText().catch(() => '');
    console.log('BODY:', body.replace(/\n/g, ' | ').slice(0, 500));
    const errUi = await audit.page.locator('#blazor-error-ui').isVisible().catch(() => false);
    console.log('blazor-error-ui visible:', errUi);
    // 刷新按钮文字（看 _serverQRCode 是否加载成功）
    const cards = await audit.page.locator('.card').allInnerTexts().catch(() => []);
    cards.forEach((c, i) => console.log(`card${i}:`, c.replace(/\n/g, ' | ').slice(0, 150)));
    // 手动点刷新
    const refreshBtns = audit.page.locator('button', { hasText: '刷新' });
    console.log('刷新按钮数:', await refreshBtns.count());
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
