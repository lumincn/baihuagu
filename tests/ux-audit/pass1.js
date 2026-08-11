// Pass 1: 基线遍历 11 页，截图 + 收集页面文本摘要 + issues
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

const PAGES = ['/', '/search', '/browse', '/note', '/messages', '/assistant',
  '/master-chat', '/master-stage', '/generate', '/cards', '/tasks'];

(async () => {
  const audit = await startAudit();
  const out = { pages: {} };
  for (const p of PAGES) {
    const res = await openPage(audit, p, { waitMs: 2500 });
    const name = (p === '/' ? 'home' : p.slice(1));
    await shot(audit, 'pass1-' + name);
    const info = await audit.page.evaluate(() => {
      const t = document.body ? document.body.innerText : '';
      const h = [...document.querySelectorAll('h1,h2,h3')].map(e => e.innerText.trim()).filter(Boolean);
      const btns = [...document.querySelectorAll('button')].map(e => (e.innerText || e.title || '🈚').trim().slice(0, 30)).slice(0, 25);
      const links = [...document.querySelectorAll('a')].map(e => (e.innerText || e.getAttribute('href') || '').trim().slice(0, 25)).slice(0, 25);
      const inputs = [...document.querySelectorAll('input,textarea,select')].map(e => {
        const r = e.getBoundingClientRect();
        return { type: e.tagName + (e.type ? ':' + e.type : ''), ph: e.getAttribute('placeholder') || '', w: Math.round(r.width), visible: r.width > 0 };
      }).filter(x => x.visible).slice(0, 20);
      return { textLen: t.length, textHead: t.slice(0, 600), h, btns, links, inputs };
    });
    out.pages[p] = { url: res.url, ok: res.ok, ...info,
      issues: { ce: [...audit.issues.consoleErrors], pe: [...audit.issues.pageErrors], fr: [...audit.issues.failedRequests] } };
    // 清空 issues，避免跨页累积混淆
    audit.issues.consoleErrors.length = 0;
    audit.issues.pageErrors.length = 0;
    audit.issues.failedRequests.length = 0;
  }
  fs.writeFileSync('pass1.json', JSON.stringify(out, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
