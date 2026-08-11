// Pass 2l: 搜索结果点击（真实鼠标事件）
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg) => log.push({ route, msg });

  await openPage(audit, '/search', { waitMs: 5000 });
  await page.fill('input[placeholder*="搜索笔记"]', '中医');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(4000);
  const n = await page.locator('.result-item').count();
  rec('T', 'result-item 数量: ' + n);
  await shot(audit, 't-search-results');
  if (n > 0) {
    await page.locator('.result-item').first().click({ timeout: 5000 }).catch(e => rec('T', '点击失败 ' + String(e).slice(0, 150)));
    await page.waitForTimeout(3000);
    rec('T', '真实点击后 url=' + page.url());
    await shot(audit, 't-search-result-realclick');
  }

  fs.writeFileSync('pass2l.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
