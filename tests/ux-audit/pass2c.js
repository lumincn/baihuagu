// Pass 2c: 交互审计第二轮
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg, extra = {}) => log.push({ route, msg, ...extra });

  // ===== /search: 下载 Obsidian 可点性 + 烘焙长等待 =====
  await openPage(audit, '/search', { waitMs: 2000 });
  const obs = await page.evaluate(() => {
    const el = [...document.querySelectorAll('a,button,span,div')].find(e => (e.innerText || '').trim() === '下载 Obsidian');
    if (!el) return null;
    const a = el.closest('a');
    return { tag: el.tagName, isA: !!a, href: a ? a.getAttribute('href') : null, cursor: getComputedStyle(el).cursor, clickable: getComputedStyle(el).pointerEvents };
  });
  rec('/search', '下载Obsidian: ' + JSON.stringify(obs));
  // 烘焙搜索等待更久
  await page.fill('input[placeholder*="搜索笔记"]', '烘焙');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(6000);
  const res = await page.evaluate(() => {
    const txt = document.body.innerText;
    const i = txt.indexOf('搜索结果');
    return { hasResultHeader: i >= 0, around: txt.slice(Math.max(0, i - 50), i + 600) };
  });
  rec('/search', '烘焙搜索6s后: ' + JSON.stringify(res));
  await shot(audit, 'i-search-baking-long');
  // 清空记录（清理我创建的搜索历史）
  const clearBtn = page.locator('button:has-text("清空记录")');
  if (await clearBtn.count()) {
    await clearBtn.click().catch(e => rec('/search', '清空记录失败 ' + e));
    await page.waitForTimeout(1200);
    const after = await page.evaluate(() => (document.body.innerText.match(/最近搜索/) ? document.body.innerText.slice(document.body.innerText.indexOf('最近搜索'), document.body.innerText.indexOf('最近搜索') + 100) : ''));
    rec('/search', '清空记录后: ' + after.replace(/\n+/g, '|'));
    await shot(audit, 'i-search-cleared');
  }
  // tab 切换：文件扫描
  const tab = page.locator('button:has-text("文件扫描"), a:has-text("文件扫描")');
  if (await tab.count()) {
    await tab.first().click().catch(e => rec('/search', '点文件扫描tab失败 ' + e));
    await page.waitForTimeout(1500);
    rec('/search', '文件扫描tab后: ' + (await page.evaluate(() => document.body.innerText.slice(0, 600))).replace(/\n+/g, '|'));
    await shot(audit, 'i-search-filescan');
  }

  // ===== /browse: 卡片可点性 =====
  await openPage(audit, '/browse', { waitMs: 2000 });
  const kbClick = await page.evaluate(() => {
    const card = [...document.querySelectorAll('*')].find(e => (e.innerText || '').includes('中医抗敏') && e.children.length < 5 && e.querySelector('button') && e.textContent.length < 120);
    const all = [...document.querySelectorAll('*')].filter(e => (e.innerText || '').trim().startsWith('📚') && (e.innerText || '').includes('中医抗敏'));
    return all.slice(0, 3).map(e => ({ tag: e.tagName, cls: (e.className || '').slice(0, 60), clickable: !!e.onclick, cursor: getComputedStyle(e).cursor, role: e.getAttribute('role') }));
  });
  rec('/browse', '中医抗敏卡片元素: ' + JSON.stringify(kbClick));
  // 点击卡片区域试试
  const cardEl = await page.evaluate(() => {
    const els = [...document.querySelectorAll('*')].filter(e => (e.innerText || '').includes('中医抗敏') && (e.innerText || '').includes('🎧') && e.children.length < 8);
    return els.length ? { tag: els[0].tagName, cls: (els[0].className || '').slice(0, 60), txt: els[0].innerText.slice(0, 60) } : null;
  });
  rec('/browse', '卡片容器: ' + JSON.stringify(cardEl));
  if (cardEl) {
    const box = await page.evaluate(() => {
      const els = [...document.querySelectorAll('*')].filter(e => (e.innerText || '').includes('中医抗敏') && (e.innerText || '').includes('🎧'));
      const el = els[0];
      const r = el.getBoundingClientRect();
      return { x: r.x + 10, y: r.y + 10 };
    });
    await page.mouse.click(box.x, box.y).catch(e => rec('/browse', '点击卡片失败 ' + e));
    await page.waitForTimeout(2000);
    rec('/browse', '点击卡片区域后 url=' + page.url());
    await shot(audit, 'i-browse-cardclick');
  }

  // ===== /master-chat: 拜师 =====
  await openPage(audit, '/master-chat', { waitMs: 2000 });
  const masterCard = await page.evaluate(() => {
    const els = [...document.querySelectorAll('*')].filter(e => (e.innerText || '').includes('岐伯') && e.children.length < 10 && (e.innerText || '').length < 200);
    return els.slice(0, 2).map(e => ({ tag: e.tagName, cls: (e.className || '').slice(0, 60) }));
  });
  rec('/master-chat', '岐伯卡片: ' + JSON.stringify(masterCard));
  const baishi = page.locator('button:has-text("拜师")');
  if (await baishi.count()) {
    await baishi.first().click().catch(e => rec('/master-chat', '点拜师失败 ' + e));
    await page.waitForTimeout(1500);
    const after = await page.evaluate(() => {
      const modal = [...document.querySelectorAll('.modal-overlay, [class*=modal]')].filter(d => getComputedStyle(d).display !== 'none').map(d => d.innerText.slice(0, 150));
      return { url: location.href, modal, body: document.body.innerText.slice(0, 500) };
    });
    rec('/master-chat', '点拜师后: ' + JSON.stringify(after));
    await shot(audit, 'i-master-baishi');
    // 若有 modal 找取消
    const cancel = page.locator('button:has-text("取消")');
    if (await cancel.count()) { await cancel.first().click().catch(() => {}); await page.waitForTimeout(800); rec('/master-chat', '已点取消'); }
    else { await page.keyboard.press('Escape').catch(() => {}); await page.waitForTimeout(800); rec('/master-chat', '无取消按钮，按Escape'); }
  }

  // ===== /master-stage: 关联知识库 modal + 师父名 =====
  await openPage(audit, '/master-stage', { waitMs: 2000 });
  const stageInfo = await page.evaluate(() => {
    const txt = document.body.innerText;
    const i = txt.indexOf('师父：');
    return { around: txt.slice(Math.max(0, i - 30), i + 80) };
  });
  rec('/master-stage', '师父名区域: ' + JSON.stringify(stageInfo));
  const relate = page.locator('button:has-text("关联知识库")');
  if (await relate.count()) {
    await relate.first().click().catch(e => rec('/master-stage', '点关联失败 ' + e));
    await page.waitForTimeout(1500);
    const modal = await page.evaluate(() => {
      const m = [...document.querySelectorAll('.modal-overlay, [class*=modal]')].filter(d => getComputedStyle(d).display !== 'none');
      return m.length ? m[0].innerText.slice(0, 300) : 'NO MODAL';
    });
    rec('/master-stage', '关联modal: ' + modal.replace(/\n+/g, '|'));
    await shot(audit, 'i-master-stage-relate');
    const cancel = page.locator('button:has-text("取消"), button:has-text("关闭")');
    if (await cancel.count()) { await cancel.first().click().catch(() => {}); await page.waitForTimeout(800); }
    else { await page.keyboard.press('Escape').catch(() => {}); await page.waitForTimeout(800); }
  }
  // 返回对话
  await page.locator('button:has-text("返回对话")').first().click().catch(e => rec('/master-stage', '返回对话失败 ' + e));
  await page.waitForTimeout(1500);
  rec('/master-stage', '返回对话后 url=' + page.url());

  // ===== /generate: 空主题校验 + 自定义行业 + 提示词 modal =====
  await openPage(audit, '/generate', { waitMs: 2000 });
  await page.locator('button:has-text("生成知识库 并创建记忆卡片"), button:has-text("🚀")').first().click().catch(e => rec('/generate', '点生成失败 ' + e));
  await page.waitForTimeout(1500);
  const genEmpty = await page.evaluate(() => {
    const txt = document.body.innerText;
    const i = txt.indexOf('生成');
    return { hasAlert: !!document.querySelector('.alert, [class*=toast], [class*=error]'), bodyAround: txt.slice(Math.max(0, i - 30), i + 150) };
  });
  rec('/generate', '空主题提交: ' + JSON.stringify(genEmpty));
  await shot(audit, 'i-generate-empty');
  // 自定义行业
  await page.locator('button:has-text("✏️ 自定义")').first().click().catch(e => rec('/generate', '点自定义失败 ' + e));
  await page.waitForTimeout(1000);
  const customInput = await page.evaluate(() => {
    const inputs = [...document.querySelectorAll('input')].map(i => ({ ph: i.getAttribute('placeholder') || '', w: Math.round(i.getBoundingClientRect().width) }));
    return inputs;
  });
  rec('/generate', '自定义行业后inputs: ' + JSON.stringify(customInput));
  await shot(audit, 'i-generate-custom');
  // 编辑提示词 modal
  await page.locator('button:has-text("编辑提示词")').first().click().catch(e => rec('/generate', '点编辑提示词失败 ' + e));
  await page.waitForTimeout(1500);
  const promptModal = await page.evaluate(() => {
    const m = [...document.querySelectorAll('.modal-overlay, [class*=modal]')].filter(d => getComputedStyle(d).display !== 'none');
    return m.length ? m[0].innerText.slice(0, 250) : 'NO MODAL';
  });
  rec('/generate', '提示词modal: ' + promptModal.replace(/\n+/g, '|'));
  await shot(audit, 'i-generate-promptmodal');
  const c2 = page.locator('button:has-text("取消"), button:has-text("关闭")');
  if (await c2.count()) { await c2.first().click().catch(() => {}); await page.waitForTimeout(800); }

  // ===== /cards: 搜索 + 筛选 =====
  await openPage(audit, '/cards', { waitMs: 2000 });
  await page.fill('input[placeholder*="搜索卡片"]', '烘焙');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(2000);
  const cardsState = await page.evaluate(() => ({ url: location.href, body: document.body.innerText.slice(0, 400) }));
  rec('/cards', '搜索烘焙后: ' + JSON.stringify(cardsState));
  await shot(audit, 'i-cards-search');
  await page.fill('input[placeholder*="搜索卡片"]', '');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(1200);
  // 知识库下拉
  const dd = page.locator('select, [class*=dropdown]').first();
  rec('/cards', '下拉元素: ' + (await dd.count() ? await dd.evaluate(e => e.tagName + ':' + (e.className || '').slice(0, 50)) : '无'));
  if (await dd.count() && (await dd.evaluate(e => e.tagName)) === 'SELECT') {
    await dd.selectOption({ index: 1 }).catch(e => rec('/cards', '选择知识库失败 ' + e));
    await page.waitForTimeout(1500);
    rec('/cards', '选择知识库后: ' + (await page.evaluate(() => document.body.innerText.slice(0, 300))).replace(/\n+/g, '|'));
    await shot(audit, 'i-cards-kbfilter');
  }

  // ===== /messages: 发送按钮状态 + 模型切换 =====
  await openPage(audit, '/messages', { waitMs: 2000 });
  const sendState = await page.evaluate(() => {
    const send = [...document.querySelectorAll('button')].find(b => (b.innerText || '').trim() === '发送');
    return { sendDisabled: send ? send.disabled : 'N/A', sendTitle: send ? (send.title || '') : '' };
  });
  rec('/messages', '空输入发送按钮: ' + JSON.stringify(sendState));
  const ta = page.locator('textarea');
  if (await ta.count()) {
    await ta.fill('测试');
    await page.waitForTimeout(800);
    const after = await page.evaluate(() => {
      const send = [...document.querySelectorAll('button')].find(b => (b.innerText || '').trim() === '发送');
      return { sendDisabled: send ? send.disabled : 'N/A' };
    });
    rec('/messages', '输入后发送按钮: ' + JSON.stringify(after));
    await ta.fill(''); // 不发送
    await page.waitForTimeout(500);
  }
  // 切换模型下拉
  const modelSel = page.locator('select').first();
  if (await modelSel.count()) {
    rec('/messages', 'select options: ' + JSON.stringify(await modelSel.evaluate(s => [...s.options].map(o => o.text))));
  }
  await shot(audit, 'i-messages-state');

  // ===== /: 主题切换 + 查看更多 + 设备管理 =====
  await openPage(audit, '/', { waitMs: 2000 });
  const recent = await page.evaluate(() => {
    const txt = document.body.innerText;
    const i = txt.indexOf('最近浏览');
    return txt.slice(i, i + 300);
  });
  rec('/home', '最近浏览区: ' + recent.replace(/\n+/g, '|'));
  await page.locator('button[title="切换亮色/暗色模式"]').first().click().catch(e => rec('/home', '主题切换失败 ' + e));
  await page.waitForTimeout(1500);
  rec('/home', '主题切换后 body class/attr: ' + await page.evaluate(() => document.body.className + '|' + (document.documentElement.getAttribute('data-bs-theme') || 'none')));
  await shot(audit, 'i-home-dark');
  await page.locator('button[title="切换亮色/暗色模式"]').first().click().catch(() => {});
  await page.waitForTimeout(800);
  await page.locator('a:has-text("查看更多")').first().click().catch(e => rec('/home', '查看更多失败 ' + e));
  await page.waitForTimeout(1500);
  rec('/home', '查看更多后 url=' + page.url());
  await openPage(audit, '/', { waitMs: 1500 });
  await page.locator('a:has-text("设备管理"), button:has-text("设备管理")').first().click().catch(e => rec('/home', '设备管理失败 ' + e));
  await page.waitForTimeout(1500);
  rec('/home', '设备管理后 url=' + page.url());

  fs.writeFileSync('pass2c.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
