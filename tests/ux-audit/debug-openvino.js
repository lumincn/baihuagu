const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/local-models', { waitMs: 5000 });
    await audit.page.locator('.nav-tabs button', { hasText: 'OpenVINO' }).first().click();
    let del = audit.page.locator('button:visible', { hasText: '删除' }).first();
    for (let i = 0; i < 40 && (await del.count()) === 0; i++) await audit.page.waitForTimeout(1000);
    console.log('删除按钮数:', await del.count());
    await del.click().catch(e => console.log('click err:', e.message));
    await audit.page.waitForTimeout(1500);
    const overlays = await audit.page.locator('.modal-overlay').count();
    console.log('modal-overlay 数量:', overlays);
    if (overlays > 0) {
      const txt = await audit.page.locator('.modal-overlay').first().innerText().catch(() => 'ERR');
      console.log('modal 文本:', JSON.stringify(txt.slice(0, 200)));
    }
    // console 错误
    console.log('console errors:', JSON.stringify(audit.issues.consoleErrors.slice(0, 5)));
    console.log('page errors:', JSON.stringify(audit.issues.pageErrors.slice(0, 5)));
    await audit.page.screenshot({ path: 'shots/debug-openvino.png', fullPage: true });
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
