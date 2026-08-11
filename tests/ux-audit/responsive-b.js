// 响应式抽查：375x812 关键页布局
const { startAudit, openPage, shot } = require('./login');
(async () => {
  const audit = await startAudit({ viewport: { width: 375, height: 812 } });
  const { page } = audit;
  const pages = ['/family', '/dashboard', '/checkin'];
  for (const p of pages) {
    await openPage(audit, p, { waitMs: 2200 });
    const m = await page.evaluate(() => {
      const de = document.documentElement;
      return {
        sw: de.scrollWidth, cw: de.clientWidth, overflowX: de.scrollWidth > de.clientWidth,
        bodyScrollW: document.body.scrollWidth,
        overflowEls: Array.from(document.querySelectorAll('*')).filter(e => {
          const r = e.getBoundingClientRect();
          return r.width > 0 && (r.right > de.clientWidth + 2 || r.left < -2);
        }).slice(0, 8).map(e => ({ tag: e.tagName, cls: String(e.className).slice(0, 40), right: Math.round(e.getBoundingClientRect().right), left: Math.round(e.getBoundingClientRect().left) })),
        h1: document.querySelector('h1')?.innerText
      };
    });
    console.log(`== ${p}`, JSON.stringify(m, null, 1));
    await shot(audit, 'b-mobile-' + p.replace(/\//g, '_'));
  }
  // 手机端菜单按钮
  await openPage(audit, '/family', { waitMs: 2000 });
  const menu = await page.evaluate(() => {
    const btns = Array.from(document.querySelectorAll('button')).filter(b => b.innerText.trim() === '☰');
    return btns.map(b => { const r = b.getBoundingClientRect(); return { vis: r.width > 0, w: Math.round(r.width), h: Math.round(r.height), rect: { x: Math.round(r.x), y: Math.round(r.y) }, parent: b.parentElement?.className }; });
  });
  console.log('mobile menu btns:', JSON.stringify(menu, null, 1));
  if (menu.length > 0 && menu[0].vis) {
    await page.locator('button:has-text("☰")').first().click().catch(e => console.log('menu click err', e.message));
    await page.waitForTimeout(800);
    console.log('after menu click body:', (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(0, 200))));
    await shot(audit, 'b-mobile-menu-open');
  }
  await audit.browser.close();
})();
