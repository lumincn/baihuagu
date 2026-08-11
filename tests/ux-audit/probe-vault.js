const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/browse', { waitMs: 3000 });
    const cards = await audit.page.locator('.vault-folder-card, .card').allInnerTexts().catch(() => []);
    console.log('卡片:', JSON.stringify(cards.slice(0, 5).map(c => c.replace(/\n/g, ' ').slice(0, 50))));
    // 点击第一个知识库进入目录，拿一篇笔记路径
    const first = audit.page.locator('.vault-folder-card .fw-bold, .vault-folder-card').first();
    if (await first.count()) { await first.click(); await audit.page.waitForTimeout(2000); }
    const items = await audit.page.locator('a, .note-item, .browse-item').allInnerTexts().catch(() => []);
    console.log('目录项:', JSON.stringify(items.slice(0, 8)));
    console.log('URL:', audit.page.url());
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
