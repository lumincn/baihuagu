// Pass 2g: 定点检查 + 响应式
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg, extra = {}) => log.push({ route, msg, ...extra });

  // ===== /browse 卡片点击细节 =====
  await openPage(audit, '/browse', { waitMs: 2000 });
  const cardBox = await page.evaluate(() => {
    const c = [...document.querySelectorAll('div.card.vault-folder-card')][0];
    if (!c) return null;
    const r = c.getBoundingClientRect();
    const title = [...c.querySelectorAll('*')].find(e => (e.innerText || '').trim() === '中医抗敏');
    const tr = title ? title.getBoundingClientRect() : null;
    return { x: r.x, y: r.y, w: r.width, h: r.height, tx: tr ? tr.x : 0, ty: tr ? tr.y : 0, tw: tr ? tr.width : 0, th: tr ? tr.height : 0 };
  });
  rec('/browse', '卡片位置: ' + JSON.stringify(cardBox));
  // hover 看样式变化
  await page.locator('div.card.vault-folder-card').first().hover().catch(() => {});
  await page.waitForTimeout(600);
  const hoverState = await page.evaluate(() => {
    const c = document.querySelector('div.card.vault-folder-card');
    return c ? { cls: c.className, shadow: getComputedStyle(c).boxShadow.slice(0, 60), cursor: getComputedStyle(c).cursor, border: getComputedStyle(c).borderColor } : 'NONE';
  });
  rec('/browse', 'hover后: ' + JSON.stringify(hoverState));
  await shot(audit, 'i-browse-hover');
  if (cardBox) {
    // 点标题文字
    await page.mouse.click(cardBox.tx + cardBox.tw / 2, cardBox.ty + cardBox.th / 2);
    await page.waitForTimeout(2500);
    rec('/browse', '点标题后 url=' + page.url() + ' selected=' + await page.evaluate(() => [...document.querySelectorAll('*')].some(e => (e.className || '').toString().includes('selected') && (e.className || '').toString().includes('card'))));
    await shot(audit, 'i-browse-titleclick');
  }

  // ===== /search 知识库筛选结构 =====
  await openPage(audit, '/search', { waitMs: 2000 });
  const kbFilter = await page.evaluate(() => {
    const all = [...document.querySelectorAll('*')].filter(e => {
      const t = (e.innerText || '').trim();
      return t === '所有知识库' && e.children.length <= 2;
    });
    return all.slice(0, 3).map(e => {
      const r = e.getBoundingClientRect();
      return { tag: e.tagName, cls: (e.className || '').toString().slice(0, 50), x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), parent: e.parentElement ? e.parentElement.tagName + '.' + ((e.parentElement.className || '').toString().slice(0, 40)) : '' };
    });
  });
  rec('/search', '知识库筛选元素: ' + JSON.stringify(kbFilter));
  if (kbFilter.length) {
    const f = kbFilter[0];
    await page.mouse.click(f.x + f.w / 2, f.y + f.h / 2);
    await page.waitForTimeout(1500);
    const after = await page.evaluate(() => {
      const opts = [...document.querySelectorAll('[class*=option], [class*=menu] *, [class*=dropdown] *')].filter(e => getComputedStyle(e).display !== 'none' && (e.innerText || '').trim() && e.children.length === 0).map(e => e.innerText.trim()).slice(0, 15);
      const t = document.body.innerText;
      return { opts, hasKbList: t.includes('中医抗敏') };
    });
    rec('/search', '点"所有知识库"后: ' + JSON.stringify(after));
    await shot(audit, 'i-search-kbfilter');
    await page.keyboard.press('Escape').catch(() => {});
    await page.waitForTimeout(600);
  }

  // ===== /messages 知识库 chip 结构 =====
  await openPage(audit, '/messages', { waitMs: 2000 });
  const chipInfo = await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('知识库'));
    if (!b) return 'NO CHIP';
    const r = b.getBoundingClientRect();
    const parent = b.parentElement;
    return { btnCls: (b.className || '').slice(0, 50), btnTxt: b.innerText.replace(/\n/g, '|'), x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), parentTxt: (parent.innerText || '').slice(0, 60).replace(/\n/g, '|'), parentCls: (parent.className || '').slice(0, 40) };
  });
  rec('/messages', 'chip: ' + JSON.stringify(chipInfo));
  if (chipInfo && chipInfo !== 'NO CHIP') {
    await page.mouse.click(chipInfo.x + chipInfo.w / 2, chipInfo.y + chipInfo.h / 2);
    await page.waitForTimeout(2000);
    const after = await page.evaluate(() => {
      const t = document.body.innerText;
      const kbIdx = t.indexOf('知识库');
      return { around: t.slice(kbIdx, kbIdx + 120).replace(/\n+/g, '|'), newEls: [...document.querySelectorAll('[class*=kb-list], [class*=panel], [class*=drawer]')].filter(e => getComputedStyle(e).display !== 'none').length };
    });
    rec('/messages', '点击chip后: ' + JSON.stringify(after));
  }

  // ===== /settings 只读：知识库路径配置入口 =====
  await openPage(audit, '/settings', { waitMs: 2500 });
  const settingsInfo = await page.evaluate(() => {
    const t = document.body.innerText;
    const hits = [];
    ['知识库路径', 'vault', 'Vault', '路径'].forEach(k => { if (t.includes(k)) hits.push(k); });
    return { title: document.title, hits, seg: t.slice(0, 400).replace(/\n+/g, '|') };
  });
  rec('/settings(参考)', '配置: ' + JSON.stringify(settingsInfo));

  fs.writeFileSync('pass2g.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
