const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/local-models', { waitMs: 600 });
    const html = await audit.page.evaluate(() => {
      const el = document.querySelector('.loading-spinner');
      return el ? el.outerHTML.slice(0, 500) : 'NO .loading-spinner';
    });
    console.log('骨架 HTML:', html);
    // 页面主体有无内容
    const main = await audit.page.evaluate(() => document.querySelector('.local-models, main, .page-content, #app')?.outerHTML.slice(0, 300) || 'none');
    console.log('主体:', main.slice(0, 250));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
