// Pass 2e: 复查
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg, extra = {}) => log.push({ route, msg, ...extra });

  // ===== /tasks 当前状态（确认上轮删除影响） =====
  await openPage(audit, '/tasks', { waitMs: 2500 });
  const tasksState = await page.evaluate(() => {
    const items = [...document.querySelectorAll('*')].filter(e => {
      const t = (e.innerText || '');
      return e.children.length <= 4 && /(Success|Cancelled|Failed|Error|Running|等待|失败|成功|取消)/.test(t) && t.length < 150 && t.includes('%');
    }).map(e => e.innerText.replace(/\n+/g, '|')).slice(0, 20);
    const header = document.body.innerText.slice(document.body.innerText.indexOf('任务管理'), document.body.innerText.indexOf('任务管理') + 200).replace(/\n+/g, '|');
    return { header, items, bodyLen: document.body.innerText.length };
  });
  rec('/tasks', '任务列表: ' + JSON.stringify(tasksState));
  await shot(audit, 'i-tasks-current');

  // ===== /browse 精确卡片 =====
  await openPage(audit, '/browse', { waitMs: 2000 });
  const kbCards = await page.evaluate(() => {
    const els = [...document.querySelectorAll('div,article,section')].filter(e => {
      const t = (e.innerText || '');
      return t.includes('中医抗敏') && t.includes('🎧') && t.length < 300 && t.length > 30 && e.children.length >= 2 && e.children.length <= 10;
    });
    return els.slice(0, 5).map(e => {
      const r = e.getBoundingClientRect();
      const a = e.querySelector('a');
      const onclick = e.getAttribute('onclick');
      return { tag: e.tagName, cls: (e.className || '').slice(0, 40), x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), hasAnchor: !!a, anchorHref: a ? a.getAttribute('href') : null, onclick };
    });
  });
  rec('/browse', 'KB卡片: ' + JSON.stringify(kbCards));
  if (kbCards.length) {
    const c = kbCards.find(k => k.h > 50) || kbCards[0];
    await page.mouse.click(c.x + c.w / 2, c.y + Math.min(40, c.h / 2));
    await page.waitForTimeout(2000);
    rec('/browse', `点击卡片(${c.tag}.${c.cls})后 url=` + page.url());
    await shot(audit, 'i-browse-kbcardclick');
  }
  // 听按钮反馈检测：audio 元素 / SpeechSynthesis
  await page.locator('button:has-text("🎧 听")').first().click().catch(() => {});
  await page.waitForTimeout(2500);
  const tts = await page.evaluate(() => {
    const audio = [...document.querySelectorAll('audio')].filter(a => !a.paused);
    return { audioPlaying: audio.length, speechSynth: typeof speechSynthesis !== 'undefined' ? speechSynthesis.speaking : 'N/A', anyAudio: document.querySelectorAll('audio').length };
  });
  rec('/browse', '听按钮后: ' + JSON.stringify(tts));

  // ===== /cards 点 chip =====
  await openPage(audit, '/cards', { waitMs: 2000 });
  const chip = page.locator('button.enhanced-select-chip:has-text("烘焙初体验")').first();
  if (await chip.count()) {
    await chip.click().catch(e => rec('/cards', '点chip失败 ' + e));
    await page.waitForTimeout(1500);
    const st = await page.evaluate(() => {
      const t = document.body.innerText;
      const i = t.indexOf('卡片总数');
      return { around: t.slice(Math.max(0, i - 100), i + 100).replace(/\n+/g, '|'), selChips: [...document.querySelectorAll('button.enhanced-select-chip.selected')].map(b => b.innerText.trim()) };
    });
    rec('/cards', '点烘焙chip后: ' + JSON.stringify(st));
    await shot(audit, 'i-cards-chip-baking');
  } else rec('/cards', '无烘焙chip');

  // ===== /generate 编辑提示词按钮属性 =====
  await openPage(audit, '/generate', { waitMs: 2000 });
  const editBtn = await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('编辑提示词'));
    if (!b) return 'NOT FOUND';
    const r = b.getBoundingClientRect();
    return { disabled: b.disabled, x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), title: b.title || '', html: b.outerHTML.slice(0, 300) };
  });
  rec('/generate', '编辑提示词按钮: ' + JSON.stringify(editBtn));
  if (editBtn && editBtn !== 'NOT FOUND') {
    await page.mouse.click(editBtn.x + editBtn.w / 2, editBtn.y + editBtn.h / 2);
    await page.waitForTimeout(2000);
    const after = await page.evaluate(() => {
      const m = [...document.querySelectorAll('.modal-overlay, [class*=modal], [class*=drawer], [class*=popup]')].filter(d => getComputedStyle(d).display !== 'none' && d.innerText.trim());
      return { modals: m.map(x => x.innerText.slice(0, 120)), bodyChanged: document.body.innerText.length };
    });
    rec('/generate', '真实点击编辑提示词后: ' + JSON.stringify(after));
    await shot(audit, 'i-generate-editclick');
    // 关掉可能打开的
    await page.keyboard.press('Escape').catch(() => {});
    await page.waitForTimeout(800);
  }

  // ===== /search 配置知识库路径入口 + 知识库下拉 =====
  await openPage(audit, '/search', { waitMs: 2000 });
  const searchExtras = await page.evaluate(() => {
    const t = document.body.innerText;
    const i = t.indexOf('⚠️');
    const warn = t.slice(i, i + 120).replace(/\n+/g, '|');
    const kbChips = [...document.querySelectorAll('button, [class*=chip]')].filter(e => (e.innerText || '').trim().length < 12 && e.offsetParent !== null).map(e => e.innerText.trim()).slice(0, 15);
    const inputs = [...document.querySelectorAll('input')].map(i => i.getAttribute('placeholder')).filter(Boolean);
    return { warn, kbChips, inputs };
  });
  rec('/search', '结构: ' + JSON.stringify(searchExtras));
  // 尝试触发历史区：先输入一个无结果词
  await page.fill('input[placeholder*="搜索笔记"]', 'zzzqq');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(3000);
  const hist = await page.evaluate(() => {
    const t = document.body.innerText;
    const i = t.indexOf('最近搜索');
    return { has: i >= 0, seg: i >= 0 ? t.slice(i, i + 150).replace(/\n+/g, '|') : '' };
  });
  rec('/search', '历史区: ' + JSON.stringify(hist));
  await shot(audit, 'i-search-hist');
  // 清空记录
  const cb = page.locator('button:has-text("清空记录")');
  if (await cb.count()) { await cb.first().click().catch(e => rec('/search', '清空失败 ' + e)); await page.waitForTimeout(1200); rec('/search', '清空后: ' + (await page.evaluate(() => { const t = document.body.innerText; const i = t.indexOf('最近搜索'); return i >= 0 ? t.slice(i, i + 80).replace(/\n+/g, '|') : '无'; }))); }

  // ===== /note 编辑按钮 =====
  await openPage(audit, '/note?id=' + encodeURIComponent('基础认识/烘焙必备工具清单及用途'), { waitMs: 2500 });
  const noteBtns = await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].filter(b => b.offsetParent !== null).map(b => ({ t: b.innerText.trim().slice(0, 10), title: (b.title || '').slice(0, 15), disabled: b.disabled }));
    return b.filter(x => /编辑|收藏|返回/.test(x.t + x.title));
  });
  rec('/note', '按钮: ' + JSON.stringify(noteBtns));
  const edit = page.locator('button:has-text("✏️"), button[title*="编辑"]').first();
  if (await edit.count()) {
    await edit.click().catch(e => rec('/note', '点编辑失败 ' + e));
    await page.waitForTimeout(1500);
    const st = await page.evaluate(() => {
      const m = [...document.querySelectorAll('.modal-overlay, [class*=modal]')].filter(d => getComputedStyle(d).display !== 'none');
      const hasEditor = !!document.querySelector('textarea');
      return { modals: m.map(x => x.innerText.slice(0, 100)), hasEditor };
    });
    rec('/note', '点编辑后: ' + JSON.stringify(st));
    await shot(audit, 'i-note-edit');
    await page.keyboard.press('Escape').catch(() => {});
    await page.waitForTimeout(800);
  }

  // ===== /messages 知识库按钮长等 =====
  await openPage(audit, '/messages', { waitMs: 2000 });
  const kbBtn2 = page.locator('button:has-text("知识库")').first();
  if (await kbBtn2.count()) {
    await kbBtn2.click().catch(e => rec('/messages', '知识库点击失败 ' + e));
    await page.waitForTimeout(2500);
    const st = await page.evaluate(() => {
      const m = [...document.querySelectorAll('.modal-overlay, [class*=modal], [class*=panel]')].filter(d => getComputedStyle(d).display !== 'none' && d.innerText.trim());
      const t = document.body.innerText;
      return { modals: m.map(x => x.innerText.slice(0, 150)), mentions: ['选择知识库', '挂载', '关联', '引用'].filter(k => t.includes(k)) };
    });
    rec('/messages', '知识库长等后: ' + JSON.stringify(st));
    await shot(audit, 'i-messages-kb2');
    await page.keyboard.press('Escape').catch(() => {});
    await page.waitForTimeout(800);
  }

  fs.writeFileSync('pass2e.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
