// 清理：清空搜索历史（测试遗留）
const { startAudit, openPage } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  await openPage(audit, '/search', { waitMs: 5000 });
  const cb = page.locator('button:has-text("清空记录")');
  if (await cb.count()) {
    await cb.first().click();
    await page.waitForTimeout(1500);
  }
  const after = await page.evaluate(() => {
    const t = document.body.innerText;
    return t.includes('最近搜索') ? t.slice(t.indexOf('最近搜索'), t.indexOf('最近搜索') + 60).replace(/\n+/g, '|') : '已无最近搜索记录';
  });
  console.log('清理结果: ' + after);
  await audit.browser.close();
})();
