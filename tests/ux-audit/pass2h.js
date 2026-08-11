// Pass 2h: 响应式 375x812 + messages 场景切换
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const log = [];
  const rec = (route, msg) => log.push({ route, msg });

  // ===== 响应式会话 =====
  const raudit = await startAudit({ viewport: { width: 375, height: 812 } });
  const { page } = raudit;
  for (const [route, name] of [['/', 'home'], ['/search', 'search'], ['/browse', 'browse'], ['/generate', 'generate']]) {
    await openPage(raudit, route, { waitMs: 2200 });
    const st = await page.evaluate(() => {
      const doc = document.documentElement;
      const overflowX = doc.scrollWidth > doc.clientWidth;
      const overflowY = doc.scrollHeight > doc.clientHeight;
      // 查找横向溢出元素
      let wideEls = [];
      if (overflowX) {
        wideEls = [...document.querySelectorAll('body *')].filter(e => {
          const r = e.getBoundingClientRect();
          return r.width > 0 && (r.right > doc.clientWidth + 2 || r.left < -2);
        }).slice(0, 5).map(e => ({ tag: e.tagName, cls: (e.className || '').toString().slice(0, 40), left: Math.round(e.getBoundingClientRect().left), right: Math.round(e.getBoundingClientRect().right) }));
      }
      const main = document.querySelector('article.content, main');
      const mr = main ? main.getBoundingClientRect() : null;
      return { scrollW: doc.scrollWidth, clientW: doc.clientWidth, overflowX, overflowY, wideEls, mainW: mr ? Math.round(mr.width) : 'N/A', mainLeft: mr ? Math.round(mr.left) : 'N/A' };
    });
    rec(route, '375px: ' + JSON.stringify(st));
    await shot(raudit, 'm-' + name);
  }
  // 移动端检查侧边栏行为
  await openPage(raudit, '/', { waitMs: 1500 });
  const navState = await page.evaluate(() => {
    const sb = document.querySelector('.sidebar, [class*=sidebar], nav');
    return { hasSidebar: !!sb, sbVisible: sb ? getComputedStyle(sb).display !== 'none' : 'N/A', sbPos: sb ? getComputedStyle(sb).position : 'N/A' };
  });
  rec('/home', '375px侧边栏: ' + JSON.stringify(navState));
  await raudit.browser.close();

  // ===== messages 场景切换 =====
  const audit = await startAudit();
  const p2 = audit.page;
  await openPage(audit, '/messages', { waitMs: 2000 });
  const sceneBtns = await p2.evaluate(() => {
    const sels = [...document.querySelectorAll('.scene-btn')];
    return sels.map(b => ({ txt: b.innerText.replace(/\n/g, '|'), cls: (b.className || ''), active: (b.className || '').includes('active') }));
  });
  rec('/messages', '场景按钮: ' + JSON.stringify(sceneBtns));
  // 点 家庭 场景
  const fam = p2.locator('.scene-btn:has-text("家庭")').first();
  if (await fam.count()) {
    await fam.click().catch(e => rec('/messages', '点家庭场景失败 ' + e));
    await p2.waitForTimeout(2000);
    const after = await p2.evaluate(() => ({ url: location.href, body: document.body.innerText.slice(0, 250).replace(/\n+/g, '|') }));
    rec('/messages', '点家庭场景后: ' + JSON.stringify(after));
    await shot(audit, 'i-messages-scene-family');
  }
  // 点 AI实验室
  const ai = p2.locator('.scene-btn:has-text("AI 实验室")').first();
  if (await ai.count()) {
    await ai.click().catch(() => {});
    await p2.waitForTimeout(1500);
    rec('/messages', '点AI实验室后 url=' + p2.url());
  }
  await audit.browser.close();

  fs.writeFileSync('pass2h.json', JSON.stringify(log, null, 2));
  console.log('DONE');
})();
