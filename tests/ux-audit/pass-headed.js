// 有头模式复核：听书"播放"崩溃是否 headless 特有
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit({ headless: false }); // 弹出可见 Edge 窗口，正常现象
  const { page } = audit;
  const log = [];
  const rec = (route, msg) => log.push({ route, msg });

  await openPage(audit, '/browse', { waitMs: 2500 });
  await page.locator('button:has-text("🎧 听")').first().click().catch(e => rec('T', '听点击失败 ' + e));
  await page.waitForTimeout(2500);
  const play = page.locator('.modal-dialog button:has-text("播放")').first();
  const before = audit.issues.consoleErrors.length;
  if (await play.count()) {
    await play.click().catch(e => rec('T', '播放点击失败 ' + e));
  }
  await page.waitForTimeout(5000);
  const after = await page.evaluate(() => {
    const d = [...document.querySelectorAll('.modal-dialog')].find(x => getComputedStyle(x).display !== 'none');
    const badge = d ? d.querySelector('.badge') : null;
    const sp = typeof speechSynthesis !== 'undefined';
    return {
      badge: badge ? badge.innerText.trim() : 'N/A',
      speechSynth: sp ? speechSynthesis.speaking : 'N/A',
      voices: sp ? speechSynthesis.getVoices().length : 0,
      bodyLen: document.body.innerText.length,
    };
  });
  rec('T', '有头模式播放后: ' + JSON.stringify(after) + ' 新console错误=' + (audit.issues.consoleErrors.length - before));
  await shot(audit, 'headed-listen-play');

  // 页面是否仍响应（circuit 是否存活）
  await page.locator('.modal-dialog button:has-text("下一首")').first().click().catch(() => {});
  await page.waitForTimeout(2000);
  const alive = await page.evaluate(() => {
    const d = [...document.querySelectorAll('.modal-dialog')].find(x => getComputedStyle(x).display !== 'none');
    return d ? d.innerText.slice(0, 60).replace(/\n+/g, '|') : 'MODAL CLOSED?';
  });
  rec('T', '点"下一首"后弹窗: ' + alive);
  await shot(audit, 'headed-listen-next');

  fs.writeFileSync('pass-headed.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
