// 探测 budget 页自定义下拉结构
const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  await openPage(audit, '/family-budget', { waitMs: 2500 });
  const info = await audit.page.evaluate(() => {
    const sel = document.querySelector('select[data-enhanced]');
    const out = { selFound: !!sel, selRect: null, siblings: [] };
    if (sel) {
      const r = sel.getBoundingClientRect();
      out.selRect = { x: r.x, y: r.y, w: r.width, h: r.height, display: getComputedStyle(sel).display };
      let el = sel.parentElement;
      for (let i = 0; i < 3 && el; i++) {
        out.siblings.push({ tag: el.tagName, cls: el.className, html: el.outerHTML.slice(0, 600) });
        el = el.parentElement;
      }
    }
    // 找自定义下拉容器
    const all = Array.from(document.querySelectorAll('[class*=dropdown], [class*=select], [class*=picker], [class*=enhanced]'));
    out.customs = all.map(e => ({ tag: e.tagName, cls: e.className, visible: !!(e.getBoundingClientRect().width), html: e.outerHTML.slice(0, 300) })).slice(0, 12);
    return out;
  });
  console.log(JSON.stringify(info, null, 2));
  await audit.browser.close();
})();
