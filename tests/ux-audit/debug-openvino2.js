const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/local-models', { waitMs: 5000 });
    await audit.page.locator('.nav-tabs button', { hasText: 'OpenVINO' }).first().click();
    let del = audit.page.locator('button:visible', { hasText: '删除' }).first();
    for (let i = 0; i < 40 && (await del.count()) === 0; i++) await audit.page.waitForTimeout(1000);
    const btnHtml = await del.evaluate(el => el.outerHTML);
    console.log('按钮HTML:', btnHtml.slice(0, 300));
    const activeTab = await audit.page.locator('.nav-tabs button.active').allInnerTexts();
    console.log('active tab:', JSON.stringify(activeTab));
    // 检查按钮所在表格标题
    const rowHtml = await del.evaluate(el => el.closest('tr') ? el.closest('tr').outerHTML.slice(0, 400) : 'no row');
    console.log('行HTML:', rowHtml.replace(/\n/g, ' ').slice(0, 400));
    // 用 JS 直接触发 click 看看（绕过 Playwright actionability）
    await del.evaluate(el => el.click());
    await audit.page.waitForTimeout(1500);
    console.log('JS click 后 modal 数:', await audit.page.locator('.modal-overlay').count());
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
