const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/browse', { waitMs: 3000 });
    // 点第一张知识库卡片的标题（审计：只有标题可点）
    const title = audit.page.locator('.vault-folder-card .fw-bold').first();
    if (await title.count()) { await title.click(); await audit.page.waitForTimeout(2500); }
    console.log('进入目录 URL:', audit.page.url());
    // 找笔记链接
    const links = await audit.page.evaluate(() => Array.from(document.querySelectorAll('a[href*="/note"]')).map(a => a.getAttribute('href')).slice(0, 5));
    console.log('笔记链接:', JSON.stringify(links));
    if (links.length > 0) {
      await audit.page.goto('http://127.0.0.1:5177' + links[0]);
      await audit.page.waitForLoadState('networkidle').catch(() => {});
      await audit.page.waitForTimeout(2500);
      const body = await audit.page.locator('body').innerText().catch(() => '');
      const loaded = !body.includes('笔记未找到');
      console.log('笔记打开:', loaded ? '✅' : '❌');
      if (loaded) {
        console.log('Anki 按钮数:', await audit.page.locator('button', { hasText: 'Anki' }).count());
        const html = await audit.page.locator('.note-actions').first().evaluate(el => el.outerHTML).catch(() => 'N/A');
        console.log('note-actions:', html.slice(0, 300));
      }
    }
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
