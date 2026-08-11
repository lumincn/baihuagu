// Pass 2m: 分组展开 + 条目点击
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
  const groups = await page.locator('.group-header').count();
  rec('T', '分组数: ' + groups);
  await shot(audit, 't-search-groups');
  if (groups > 0) {
    await page.locator('.group-header').first().click({ timeout: 5000 }).catch(e => rec('T', '点组失败 ' + String(e).slice(0, 100)));
    await page.waitForTimeout(1500);
    const items = await page.locator('.result-item').count();
    rec('T', '展开后 result-item: ' + items);
    await shot(audit, 't-search-group-expanded');
    if (items > 0) {
      await page.locator('.result-item').first().click({ timeout: 5000 }).catch(e => rec('T', '点条目失败 ' + String(e).slice(0, 100)));
      await page.waitForTimeout(3000);
      rec('T', '点条目后 url=' + page.url());
    }
  }

  fs.writeFileSync('pass2m.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
