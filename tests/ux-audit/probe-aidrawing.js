const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/ai-drawing', { waitMs: 3500 });
    await audit.page.waitForTimeout(1000);
    console.log('ai-drawing failedRequests:', JSON.stringify(audit.issues.failedRequests.slice(0, 5), null, 1));
    const imgs = await audit.page.evaluate(() => Array.from(document.images).map(i => i.src));
    console.log('页面图片:', JSON.stringify(imgs.slice(0, 5)));
    await audit.page.waitForTimeout(2500);
    console.log('等待后 failedRequests:', JSON.stringify(audit.issues.failedRequests.slice(0, 5), null, 1));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
