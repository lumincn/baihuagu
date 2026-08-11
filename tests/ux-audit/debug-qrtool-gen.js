const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/qr-tool', { waitMs: 4000 });
    await audit.page.waitForTimeout(2000);
    await audit.page.locator('.card-header', { hasText: '通用二维码' }).click();
    await audit.page.waitForTimeout(800);
    await audit.page.locator('textarea.form-control').fill('测试内容ABC123');
    await audit.page.locator('button', { hasText: '生成二维码' }).click();
    await audit.page.waitForTimeout(2500);
    const st = await audit.page.evaluate(() => {
      const el = document.getElementById('general-qr-container');
      return { exists: !!el, html: el ? el.innerHTML.slice(0, 150) : null, children: el ? el.children.length : -1 };
    });
    console.log('general 容器:', JSON.stringify(st));
    const body = await audit.page.locator('body').innerText().catch(() => '');
    console.log('错误提示:', body.split('\n').filter(l => l.includes('失败') || l.includes('请输入')).join('|') || '无');
    console.log('console errors:', JSON.stringify(audit.issues.consoleErrors.slice(0, 6)));
    // 手动调用 JS 看返回值
    const ret = await audit.page.evaluate(async () => {
      try {
        const r = await window.generateCompactQRCode('general-qr-container', '手动测试123');
        return { ret: r, html: document.getElementById('general-qr-container').innerHTML.slice(0, 100) };
      } catch (e) { return { err: String(e) }; }
    });
    console.log('手动JS调用:', JSON.stringify(ret));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
