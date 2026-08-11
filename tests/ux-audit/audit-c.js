// C组 14 页全量巡检：截图 + 每页收集 console/pageerror/失败请求 + 页面结构信息
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

const PAGES = [
  '/settings', '/local-models', '/openclaw', '/prompt-templates', '/model-benchmark',
  '/hardware-benchmark', '/image-recognition', '/ai-drawing', '/stock-advisor',
  '/qr-tool', '/code-agent', '/log-errors', '/log-settings', '/login'
];

(async () => {
  const audit = await startAudit();
  const results = [];
  for (const path of PAGES) {
    const before = {
      ce: audit.issues.consoleErrors.length,
      pe: audit.issues.pageErrors.length,
      fr: audit.issues.failedRequests.length,
    };
    const r = await openPage(audit, path, { waitMs: 2500 });
    await shot(audit, 'C' + path.replace(/\//g, '_'));
    const info = await audit.page.evaluate(() => {
      const txt = (document.body ? document.body.innerText : '');
      const pick = (sel) => Array.from(document.querySelectorAll(sel)).slice(0, 30).map(e => e.textContent.trim()).filter(Boolean);
      return {
        title: document.title,
        textLen: txt.length,
        textHead: txt.slice(0, 400),
        h1: pick('h1'), h2: pick('h2'), h3: pick('h3'),
        buttons: pick('button, [role=button], .btn, a.btn'),
        placeholders: Array.from(document.querySelectorAll('input, textarea')).slice(0, 20).map(i => i.placeholder || '').filter(Boolean),
        errUi: !!document.querySelector('#blazor-error-ui') && getComputedStyle(document.querySelector('#blazor-error-ui')).display !== 'none',
        selects: Array.from(document.querySelectorAll('select')).map(s => ({ name: s.name || s.id || '', options: Array.from(s.options).map(o => o.textContent.trim()).slice(0, 12) })),
      };
    }).catch(e => ({ evalErr: String(e) }));
    results.push({
      path, ok: r.ok, url: r.url, gotoErr: r.gotoErr,
      consoleErrors: audit.issues.consoleErrors.slice(before.ce),
      pageErrors: audit.issues.pageErrors.slice(before.pe),
      failedRequests: audit.issues.failedRequests.slice(before.fr),
      info,
    });
    console.log('DONE', path, 'ok=' + r.ok, 'textLen=' + (info.textLen ?? '?'));
  }
  fs.writeFileSync('results-c.json', JSON.stringify(results, null, 2), 'utf8');
  console.log('==== SUMMARY ====');
  for (const r of results) {
    console.log(r.path, '| ok=' + r.ok, '| ce=' + r.consoleErrors.length, '| pe=' + r.pageErrors.length, '| fr=' + r.failedRequests.length, '| errUi=' + (r.info.errUi || false));
  }
  await audit.browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
