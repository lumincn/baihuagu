// Pass 2b: 交互审计（无害操作）
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg, extra = {}) => log.push({ route, msg, ...extra });

  // ===== /search =====
  await openPage(audit, '/search', { waitMs: 2000 });
  // 下载 Obsidian 是什么元素？
  const obsidianEl = await page.evaluate(() => {
    const el = [...document.querySelectorAll('a,button')].find(e => (e.innerText || '').includes('下载 Obsidian'));
    return el ? { tag: el.tagName, href: el.getAttribute('href'), onclick: el.hasAttribute('@onclick') } : null;
  });
  rec('/search', '下载Obsidian元素: ' + JSON.stringify(obsidianEl));
  // 输入搜索词
  await page.fill('input[placeholder*="搜索笔记"]', '烘焙');
  await shot(audit, 'i-search-typed');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(2500);
  const searchState = await page.evaluate(() => ({
    url: location.href,
    results: [...document.querySelectorAll('body *')].filter(e => e.children.length === 0 && e.innerText && e.innerText.trim().length < 60).map(e => e.innerText.trim()).filter(t => t).slice(0, 40),
    hasSpinner: !!document.querySelector('.spinner-border, .loading'),
  }));
  rec('/search', '搜索"烘焙"后: url=' + searchState.url + ' spinner=' + searchState.hasSpinner);
  rec('/search', '结果文本片段: ' + JSON.stringify(searchState.results));
  await shot(audit, 'i-search-baking');
  // 无结果搜索
  await page.fill('input[placeholder*="搜索笔记"]', 'zzzzqq不存在内容');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(2500);
  const noResult = await page.evaluate(() => document.body.innerText.slice(0, 800));
  rec('/search', '无结果时正文: ' + noResult.replace(/\n+/g, '|'));
  await shot(audit, 'i-search-noresult');
  // 清空输入再搜
  await page.fill('input[placeholder*="搜索笔记"]', '');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1500);
  const emptySearch = await page.evaluate(() => document.body.innerText.slice(0, 400));
  rec('/search', '空输入搜索: ' + emptySearch.replace(/\n+/g, '|'));
  // 行业 pill 点击
  await page.locator('button:has-text("中医")').first().click().catch(e => rec('/search', '点行业pill失败 ' + e));
  await page.waitForTimeout(1200);
  await shot(audit, 'i-search-pill-tcm');
  await page.locator('button:has-text("全部")').first().click().catch(() => {});
  await page.waitForTimeout(800);

  // ===== /browse =====
  await openPage(audit, '/browse', { waitMs: 2000 });
  const cards = await page.evaluate(() => [...document.querySelectorAll('a,button')].filter(e => (e.innerText || '').includes('🎧')).map(e => ({ tag: e.tagName, href: e.getAttribute('href'), text: e.innerText.trim().slice(0, 20) })));
  rec('/browse', '听按钮: ' + JSON.stringify(cards.slice(0, 5)));
  // 点击第一个听按钮
  const listen = page.locator('button:has-text("🎧 听"), a:has-text("🎧 听")').first();
  if (await listen.count()) {
    await listen.click().catch(e => rec('/browse', '点听失败 ' + e));
    await page.waitForTimeout(2000);
    const afterListen = await page.evaluate(() => ({ url: location.href, txt: document.body.innerText.slice(0, 300) }));
    rec('/browse', '点"听"后: url=' + afterListen.url + ' txt=' + afterListen.txt.replace(/\n+/g, '|').slice(0, 200));
    await shot(audit, 'i-browse-listen');
  }
  // 点击知识库卡片 → 去哪？
  const kbCard = page.locator('a:has-text("中医抗敏")').first();
  if (await kbCard.count()) {
    await kbCard.click().catch(e => rec('/browse', '点知识库失败 ' + e));
    await page.waitForTimeout(2000);
    rec('/browse', '点"中医抗敏"后 url=' + page.url());
    await shot(audit, 'i-browse-kbclick');
  }
  // 行业筛选
  await openPage(audit, '/browse', { waitMs: 1500 });
  await page.locator('button:has-text("中医")').first().click().catch(e => rec('/browse', '行业筛选失败 ' + e));
  await page.waitForTimeout(1200);
  const filtered = await page.evaluate(() => document.body.innerText.slice(0, 400));
  rec('/browse', '筛选中医后: ' + filtered.replace(/\n+/g, '|'));
  await shot(audit, 'i-browse-filter');

  // ===== /note?id=实际笔记 =====
  const noteUrl = '/note?id=' + encodeURIComponent('基础认识/烘焙必备工具清单及用途');
  await openPage(audit, noteUrl, { waitMs: 2500 });
  const noteState = await page.evaluate(() => ({
    url: location.href,
    hasContent: (document.body.innerText || '').length,
    textHead: document.body.innerText.slice(0, 700),
  }));
  rec('/note', '真实笔记: ' + JSON.stringify(noteState));
  await shot(audit, 'i-note-real');
  // 返回/去搜索按钮
  await page.locator('button:has-text("去搜索"), a:has-text("去搜索")').first().click().catch(e => rec('/note', '去搜索失败 ' + e));
  await page.waitForTimeout(1500);
  rec('/note', '点"去搜索"后 url=' + page.url());

  // ===== /tasks =====
  await openPage(audit, '/tasks', { waitMs: 2000 });
  // 清空历史 → 有无确认？
  await page.locator('button:has-text("清空历史")').first().click().catch(e => rec('/tasks', '点清空历史失败 ' + e));
  await page.waitForTimeout(1200);
  const confirmState = await page.evaluate(() => {
    const dialogs = [...document.querySelectorAll('.modal, [class*=modal], [role=dialog], .swal, .confirm')].map(d => ({ cls: d.className, vis: getComputedStyle(d).display !== 'none', txt: d.innerText.slice(0, 120) }));
    const native = window.confirm ? 'confirm-exists' : 'no-confirm';
    return { dialogs, native, bodyText: document.body.innerText.slice(0, 300) };
  });
  rec('/tasks', '清空历史后: ' + JSON.stringify(confirmState));
  await shot(audit, 'i-tasks-clearhist');
  // 若有 modal 取消
  await page.keyboard.press('Escape').catch(() => {});
  await page.waitForTimeout(800);
  // 删除单条任务 → 有无确认？
  await page.locator('button:has-text("🗑️ 删除")').first().click().catch(e => rec('/tasks', '点删除失败 ' + e));
  await page.waitForTimeout(1200);
  const delState = await page.evaluate(() => {
    const dialogs = [...document.querySelectorAll('.modal, [class*=modal], [role=dialog]')].map(d => ({ cls: d.className, vis: getComputedStyle(d).display !== 'none', txt: d.innerText.slice(0, 150) }));
    return { dialogs, bodyText: document.body.innerText.slice(0, 300) };
  });
  rec('/tasks', '删除任务后: ' + JSON.stringify(delState));
  await shot(audit, 'i-tasks-delete');
  await page.keyboard.press('Escape').catch(() => {});
  await page.waitForTimeout(800);
  // 点任务里的笔记链接
  await page.locator('a[href*="note?id="]').first().click().catch(e => rec('/tasks', '点笔记链接失败 ' + e));
  await page.waitForTimeout(1800);
  rec('/tasks', '点笔记链接后 url=' + page.url());

  fs.writeFileSync('pass2b.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
