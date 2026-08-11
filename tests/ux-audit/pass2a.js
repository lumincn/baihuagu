// Pass 2a: 结构性检查 — title/lang/viewport/全部链接 href/icon按钮 title
const { startAudit, openPage } = require('./login');
const fs = require('fs');

const PAGES = ['/', '/search', '/browse', '/note', '/messages', '/assistant',
  '/master-chat', '/master-stage', '/generate', '/cards', '/tasks'];

(async () => {
  const audit = await startAudit();
  const out = {};
  for (const p of PAGES) {
    await openPage(audit, p, { waitMs: 1800 });
    out[p] = await audit.page.evaluate(() => {
      const links = [...document.querySelectorAll('a')].map(a => ({ t: (a.innerText || '').trim().slice(0, 20), href: a.getAttribute('href') || '' })).filter(x => x.href && x.href !== '#').slice(0, 40);
      const iconBtns = [...document.querySelectorAll('button')].filter(b => !b.innerText.trim() && (b.title || b.getAttribute('aria-label'))).map(b => ({ title: b.title || b.getAttribute('aria-label'), cls: (b.className || '').slice(0, 40) }));
      return {
        title: document.title, lang: document.documentElement.lang,
        viewport: document.querySelector('meta[name=viewport]')?.content || 'NONE',
        links, iconBtns,
        scrollH: document.documentElement.scrollHeight, clientH: document.documentElement.clientHeight,
        bodyOverflowX: document.documentElement.scrollWidth > document.documentElement.clientWidth
      };
    });
    audit.issues.consoleErrors.length = 0; audit.issues.pageErrors.length = 0; audit.issues.failedRequests.length = 0;
  }
  fs.writeFileSync('pass2a.json', JSON.stringify(out, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
