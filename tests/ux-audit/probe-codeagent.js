const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/code-agent', { waitMs: 3000 });
    const sel = audit.page.locator('select').first();
    const opts = await sel.locator('option').allInnerTexts().catch(() => []);
    console.log('提供方选项:', JSON.stringify(opts));
    const sel2 = audit.page.locator('select').nth(1);
    console.log('模型默认选项:', JSON.stringify(await sel2.locator('option').allInnerTexts().catch(() => [])));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
