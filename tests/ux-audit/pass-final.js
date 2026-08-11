// 收尾：home 状态确认
const { startAudit, openPage } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const out = {};
  await openPage(audit, '/', { waitMs: 2500 });
  const t = await page.evaluate(() => {
    const txt = document.body.innerText;
    const i = txt.indexOf('最近浏览');
    const j = txt.indexOf('最近搜索');
    return { recentBrowse: txt.slice(i, i + 120).replace(/\n+/g, '|'), recentSearch: j >= 0 ? txt.slice(j, j + 120).replace(/\n+/g, '|') : '无最近搜索区' };
  });
  out.home = t;
  fs.writeFileSync('pass-final.json', JSON.stringify(out, null, 2));
  console.log(JSON.stringify(out, null, 2));
  await audit.browser.close();
})();
