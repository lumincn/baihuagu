const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/browse', { waitMs: 3000 });
    await audit.page.locator('button', { hasText: '听' }).first().click();
    await audit.page.waitForTimeout(2500);
    const playBtn = audit.page.locator('.modal button', { hasText: '播放' }).first();
    console.log('播放按钮数:', await playBtn.count());
    await playBtn.click();
    await audit.page.waitForTimeout(3000);
    const modalAlive = await audit.page.locator('.modal').isVisible().catch(() => false);
    const circuitDead = await audit.page.locator('#blazor-error-ui').isVisible().catch(() => false);
    const badge = await audit.page.locator('.modal .badge').innerText().catch(() => '');
    // 页面交互仍可用：点"下一首"
    const nextBtn = audit.page.locator('.modal button', { hasText: '下一篇' }).first();
    const nextCount = await nextBtn.count();
    if (nextCount) { await nextBtn.click(); await audit.page.waitForTimeout(1200); }
    const stillAlive = await audit.page.locator('.modal').isVisible().catch(() => false);
    console.log('modal存活:', modalAlive, '| blazor错误条:', circuitDead, '| 状态徽章:', badge, '| 下一篇后可交互:', stillAlive);
    console.log('console errors:', JSON.stringify(audit.issues.consoleErrors.slice(0, 4)));
    console.log('page errors:', JSON.stringify(audit.issues.pageErrors.slice(0, 4)));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
