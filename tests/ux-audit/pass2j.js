// Pass 2j: 最终验证 + 清理
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg) => log.push({ route, msg });

  // ===== /browse 听 modal =====
  await openPage(audit, '/browse', { waitMs: 2500 });
  await page.locator('button:has-text("🎧 听")').first().click().catch(e => rec('/browse', '听点击失败 ' + e));
  await page.waitForTimeout(3000);
  const listenState = await page.evaluate(() => {
    const dialogs = [...document.querySelectorAll('.modal-dialog')].filter(d => getComputedStyle(d).display !== 'none');
    const overlay = [...document.querySelectorAll('.modal')].filter(d => getComputedStyle(d).display !== 'none' && d.className.includes('fade'));
    return { dialogs: dialogs.map(d => d.innerText.slice(0, 150).replace(/\n+/g, '|')), overlayCount: overlay.length };
  });
  rec('/browse', '听modal: ' + JSON.stringify(listenState));
  await shot(audit, 'i-browse-listening-modal');
  if (listenState.dialogs.length) {
    // 点播放
    const play = page.locator('.modal-dialog button:has-text("播放")').first();
    if (await play.count()) {
      await play.click().catch(e => rec('/browse', '播放点击失败 ' + e));
      await page.waitForTimeout(2500);
      const after = await page.evaluate(() => {
        const d = [...document.querySelectorAll('.modal-dialog')].find(x => getComputedStyle(x).display !== 'none');
        return d ? d.innerText.slice(0, 120).replace(/\n+/g, '|') : 'NONE';
      });
      rec('/browse', '点播放后: ' + after);
      await shot(audit, 'i-browse-listening-play');
    }
    // 关闭
    const close = page.locator('.modal-dialog .btn-close, .modal-dialog button:has-text("关闭")').first();
    if (await close.count()) { await close.click().catch(() => {}); await page.waitForTimeout(1000); }
  }

  // ===== /master-stage 关联知识库 dialog =====
  await openPage(audit, '/master-stage?masterId=' + encodeURIComponent('qibo'), { waitMs: 2500 });
  // 先看有 masterId 参数时的页面（岐伯）
  const st = await page.evaluate(() => {
    const t = document.body.innerText;
    const i = t.indexOf('师父：');
    return { around: i >= 0 ? t.slice(Math.max(0, i - 20), i + 60).replace(/\n+/g, '|') : '无师父标签', hasTimeline: t.includes('入道'), isEmpty: t.includes('选择一位师父') };
  });
  rec('/master-stage', '带masterId=qibo: ' + JSON.stringify(st));
  await page.waitForTimeout(3000); // 等 vaults 加载
  await page.locator('button:has-text("关联知识库")').first().click().catch(e => rec('/master-stage', '关联点击失败 ' + e));
  await page.waitForTimeout(1500);
  const picker = await page.evaluate(() => {
    const d = [...document.querySelectorAll('.dialog-overlay')].filter(x => getComputedStyle(x).display !== 'none');
    return { dialog: d.length ? d[0].innerText.slice(0, 250).replace(/\n+/g, '|') : 'NO DIALOG', vaults: [...document.querySelectorAll('.vault-picker-item')].map(v => v.innerText.trim().slice(0, 20)) };
  });
  rec('/master-stage', '关联dialog: ' + JSON.stringify(picker));
  await shot(audit, 'i-master-stage-picker');
  const close = page.locator('.dialog-footer button:has-text("关闭")');
  if (await close.count()) { await close.click().catch(() => {}); await page.waitForTimeout(800); }

  // ===== /generate 编辑提示词 inline =====
  await openPage(audit, '/generate', { waitMs: 2000 });
  await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('编辑提示词'));
    if (b) b.click();
  });
  await page.waitForTimeout(1500);
  const inline = await page.evaluate(() => {
    const tas = [...document.querySelectorAll('textarea')].filter(t => getComputedStyle(t).display !== 'none');
    return { textareas: tas.length, taText: tas.length ? tas[0].value.slice(0, 80) : '' };
  });
  rec('/generate', '编辑提示词inline: ' + JSON.stringify(inline));
  await shot(audit, 'i-generate-prompt-inline');
  // 收起
  await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('收起'));
    if (b) b.click();
  });
  await page.waitForTimeout(800);

  // ===== /search 结果点击 → note =====
  await openPage(audit, '/search', { waitMs: 5000 }); // 等 vaults
  await page.fill('input[placeholder*="搜索笔记"]', '中医');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(4000);
  const resClick = await page.evaluate(() => {
    const item = document.querySelector('.result-item');
    if (item) { item.click(); return true; }
    return false;
  });
  await page.waitForTimeout(2500);
  rec('/search', '点结果后 url=' + page.url());
  await shot(audit, 'i-search-result-click');

  // ===== 清理：清空搜索历史 =====
  await openPage(audit, '/search', { waitMs: 5000 });
  const histBtn = page.locator('button:has-text("清空记录")');
  rec('/search', '清空记录按钮存在: ' + (await histBtn.count()));
  if (await histBtn.count()) {
    await histBtn.first().click().catch(e => rec('/search', '清空点击失败 ' + e));
    await page.waitForTimeout(1500);
    const after = await page.evaluate(() => {
      const t = document.body.innerText;
      return t.includes('最近搜索') ? t.slice(t.indexOf('最近搜索'), t.indexOf('最近搜索') + 100).replace(/\n+/g, '|') : '最近搜索区已消失/无记录';
    });
    rec('/search', '清空后: ' + after);
    await shot(audit, 'i-search-cleaned');
  }

  fs.writeFileSync('pass2j.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
