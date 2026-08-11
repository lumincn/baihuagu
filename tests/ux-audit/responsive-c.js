// C组响应式抽查：375x812 手机视口
const { startAudit, openPage, shot } = require('./login');

(async () => {
  const audit = await startAudit({ viewport: { width: 375, height: 812 } });
  const { page } = audit;
  for (const path of ['/settings', '/stock-advisor', '/local-models']) {
    await openPage(audit, path, { waitMs: 3000 });
    const body = await page.evaluate(() => document.body ? document.body.innerText.slice(0, 200) : '');
    // 检查横向滚动
    const hScroll = await page.evaluate(() => ({ sw: document.documentElement.scrollWidth, cw: document.documentElement.clientWidth }));
    console.log('RESP', path, 'scrollWidth=', hScroll.sw, 'clientWidth=', hScroll.cw, 'overflow=', hScroll.sw > hScroll.cw);
    console.log('RESP body head:', JSON.stringify(body.slice(0, 100)));
    await shot(audit, 'C_resp375_' + path.replace(/\//g, '_'));
  }
  console.log('ISSUES:', JSON.stringify(audit.issues, null, 2).slice(0, 1500));
  await audit.browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
