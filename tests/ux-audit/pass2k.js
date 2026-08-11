// Pass 2k: 定位 circuit 崩溃
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg) => log.push({ route, msg });

  // 测试1: /search 搜索结果点击
  await openPage(audit, '/search', { waitMs: 5000 });
  await page.fill('input[placeholder*="搜索笔记"]', '中医');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(4000);
  const before = audit.issues.consoleErrors.length;
  await page.evaluate(() => { const item = document.querySelector('.result-item'); if (item) item.click(); });
  await page.waitForTimeout(3000);
  rec('T1', '点击结果后 url=' + page.url() + ' 新console错误=' + (audit.issues.consoleErrors.length - before));
  await shot(audit, 't1-after-click');

  // 测试2: /browse 听 + 播放
  await openPage(audit, '/browse', { waitMs: 2500 });
  const b2 = audit.issues.consoleErrors.length;
  await page.locator('button:has-text("🎧 听")').first().click().catch(() => {});
  await page.waitForTimeout(2500);
  const play = page.locator('.modal-dialog button:has-text("播放")').first();
  if (await play.count()) { await play.click().catch(() => {}); }
  await page.waitForTimeout(4000);
  rec('T2', '听+播放后 新console错误=' + (audit.issues.consoleErrors.length - b2));
  await shot(audit, 't2-listen-play');

  // 测试3: /master-stage 无参直达
  await openPage(audit, '/master-stage', { waitMs: 2500 });
  const b3 = audit.issues.consoleErrors.length;
  const st = await page.evaluate(() => {
    const t = document.body.innerText;
    return { hasEmptyHint: t.includes('选择一位师父'), masterEmpty: t.includes('师父：') };
  });
  rec('T3', '无参直达: ' + JSON.stringify(st) + ' 新错误=' + (audit.issues.consoleErrors.length - b3));

  // 测试4: /master-stage?masterId=不存在
  await openPage(audit, '/master-stage?masterId=notexist', { waitMs: 2500 });
  const b4 = audit.issues.consoleErrors.length;
  const st4 = await page.evaluate(() => { const t = document.body.innerText; return t.slice(0, 300).replace(/\n+/g, '|'); });
  rec('T4', 'masterId=notexist: ' + st4 + ' | 新错误=' + (audit.issues.consoleErrors.length - b4));
  await shot(audit, 't4-master-notexist');

  fs.writeFileSync('pass2k.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
