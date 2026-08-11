// B组页面扫描：截图 + 文本转储 + 元素清单 + 错误收集
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

const PAGES = [
  '/family', '/dashboard', '/checkin', '/daily-card', '/achievements',
  '/leaderboard', '/family/quiz', '/family-budget', '/onboarding', '/ai-metrics'
];

(async () => {
  const audit = await startAudit();
  const report = {};
  for (const p of PAGES) {
    const issuesBefore = JSON.stringify(audit.issues);
    const r = await openPage(audit, p, { waitMs: 2500 });
    const shotName = 'b-' + p.replace(/\//g, '_').replace(/^_/, '') || 'b-root';
    await shot(audit, shotName);
    const info = await audit.page.evaluate(() => {
      const q = (s) => Array.from(document.querySelectorAll(s));
      return {
        title: document.title,
        h1: q('h1').map(e => e.innerText.trim()),
        h2: q('h2').map(e => e.innerText.trim()),
        h3: q('h3').map(e => e.innerText.trim()),
        buttons: q('button').map(e => e.innerText.trim()).filter(Boolean).slice(0, 30),
        links: q('a').map(e => ({ t: e.innerText.trim(), h: e.getAttribute('href') })).filter(x => x.t || x.h).slice(0, 30),
        inputs: q('input,textarea,select').map(e => ({ tag: e.tagName, type: e.getAttribute('type'), ph: e.getAttribute('placeholder'), val: e.value })).slice(0, 20),
        bodyText: document.body.innerText.replace(/\n{3,}/g, '\n\n').slice(0, 6000),
        errUi: !!document.querySelector('#blazor-error-ui'),
        loading: !!document.querySelector('.loading, .spinner, [class*=loading]')
      };
    });
    report[p] = { ok: r.ok, url: r.url, ...info, shot: shotName + '.png' };
    // 记录该页新增的错误
    const now = JSON.stringify(audit.issues);
    if (now !== issuesBefore) {
      report[p].newIssues = { consoleErrors: audit.issues.consoleErrors, pageErrors: audit.issues.pageErrors, failedRequests: audit.issues.failedRequests };
    }
    console.log(`== ${p} ok=${r.ok} errs=${audit.issues.consoleErrors.length}/${audit.issues.pageErrors.length}/${audit.issues.failedRequests.length}`);
  }
  fs.writeFileSync('scan-b-report.json', JSON.stringify(report, null, 2));
  console.log('TOTAL issues:', JSON.stringify(audit.issues, null, 2));
  await audit.browser.close();
})();
