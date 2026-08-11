// Pass 2i: /search 知识库加载稳定性测试 + /browse SelectVault 验证
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg) => log.push({ route, msg });

  // /search 重复打开 4 次，检查 select 是否渲染
  for (let i = 1; i <= 4; i++) {
    await openPage(audit, '/search', { waitMs: 1500 });
    const s1 = await page.evaluate(() => {
      const sels = [...document.querySelectorAll('select')];
      return { selCount: sels.length, opts: sels.map(s => [...s.options].map(o => o.text)) };
    });
    await page.waitForTimeout(3000);
    const s2 = await page.evaluate(() => {
      const sels = [...document.querySelectorAll('select')];
      return { selCount: sels.length, opts: sels.map(s => [...s.options].map(o => o.text).slice(0, 10)) };
    });
    rec('/search', `第${i}次: 1.5s后=${JSON.stringify(s1)} | 4.5s后=${JSON.stringify(s2)}`);
  }

  // 当前状态下实际搜索一次
  await page.fill('input[placeholder*="搜索笔记"]', '中医');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(4000);
  const r = await page.evaluate(() => {
    const t = document.body.innerText;
    const i = t.indexOf('⚠️');
    const j = t.indexOf('搜索结果');
    return { warn: i >= 0 ? t.slice(i, i + 80).replace(/\n+/g, '|') : 'NO', results: j >= 0 ? t.slice(j, j + 200).replace(/\n+/g, '|') : 'NO' };
  });
  rec('/search', '搜"中医": ' + JSON.stringify(r));
  await shot(audit, 'i-search-zhongyi');

  // /browse: 点标题验证 SelectVault
  await openPage(audit, '/browse', { waitMs: 2000 });
  const t1 = await page.evaluate(() => document.body.innerText.includes('基础认识') || document.body.innerText.includes('材料知识'));
  await page.evaluate(() => {
    const title = [...document.querySelectorAll('div.fw-bold.text-truncate')].find(d => (d.innerText || '').trim() === '中医抗敏');
    if (title) title.click();
  });
  await page.waitForTimeout(2500);
  const t2 = await page.evaluate(() => {
    const txt = document.body.innerText;
    return { hasFolders: txt.includes('基础认识') || txt.includes('材料知识'), hasBack: txt.includes('返回'), breadcrumb: txt.slice(0, 800).replace(/\n+/g, '|') };
  });
  rec('/browse', '点标题前有笔记内容:' + t1 + ' 点后: ' + JSON.stringify(t2));
  await shot(audit, 'i-browse-after-title-click');
  // 点击文件夹 基础认识
  const folderClick = await page.evaluate(() => {
    const f = [...document.querySelectorAll('div.vault-folder-card')].find(d => (d.innerText || '').includes('基础认识'));
    if (f) { f.click(); return true; }
    return false;
  });
  await page.waitForTimeout(2000);
  const t3 = await page.evaluate(() => document.body.innerText.slice(0, 600).replace(/\n+/g, '|'));
  rec('/browse', '点基础认识文件夹后: ' + t3);
  await shot(audit, 'i-browse-folder');
  // 点击笔记卡片 → modal?
  const noteClick = await page.evaluate(() => {
    const n = [...document.querySelectorAll('div.vault-note-card')].find(d => (d.innerText || '').includes('烘焙必备工具清单'));
    if (n) { n.click(); return true; }
    return false;
  });
  await page.waitForTimeout(2500);
  const t4 = await page.evaluate(() => {
    const modal = [...document.querySelectorAll('.modal-dialog')].map(m => m.innerText.slice(0, 200));
    return { modal, body: document.body.innerText.slice(0, 400).replace(/\n+/g, '|') };
  });
  rec('/browse', '点笔记后: ' + JSON.stringify(t4));
  await shot(audit, 'i-browse-note-modal');

  fs.writeFileSync('pass2i.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
