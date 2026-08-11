// Pass 2d: 针对性复查
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg, extra = {}) => log.push({ route, msg, ...extra });

  // ===== /search 深入 =====
  await openPage(audit, '/search', { waitMs: 2000 });
  // 下载 Obsidian 元素链
  const obs = await page.evaluate(() => {
    const el = [...document.querySelectorAll('a,button,span')].find(e => (e.innerText || '').includes('下载 Obsidian'));
    if (!el) return 'NOT FOUND';
    const chain = [];
    let n = el;
    for (let i = 0; i < 4 && n; i++) { chain.push(n.tagName + '.' + ((n.className || '').toString().slice(0, 30)) + ' href=' + (n.getAttribute && n.getAttribute('href'))); n = n.parentElement; }
    return chain;
  });
  rec('/search', '下载Obsidian元素链: ' + JSON.stringify(obs));
  // 多词搜索行为
  for (const term of ['中医抗敏', '面粉', '桂花']) {
    await page.fill('input[placeholder*="搜索笔记"]', term);
    await page.keyboard.press('Enter');
    await page.waitForTimeout(4000);
    const st = await page.evaluate(() => {
      const t = document.body.innerText;
      const i = t.indexOf('文件扫描');
      const seg = t.slice(i, i + 400);
      return { seg: seg.replace(/\n+/g, '|') };
    });
    rec('/search', `搜"${term}"后: ` + st.seg);
    await shot(audit, 'i-search-' + term);
  }
  // 清空记录（清理本审计创建的搜索历史；首页初始无历史记录）
  const clearBtn = page.locator('button:has-text("清空记录")');
  if (await clearBtn.count()) {
    await clearBtn.first().click().catch(e => rec('/search', '清空记录失败 ' + e));
    await page.waitForTimeout(1200);
    rec('/search', '已点清空记录，剩余: ' + (await page.evaluate(() => { const t = document.body.innerText; const i = t.indexOf('最近搜索'); return i >= 0 ? t.slice(i, i + 80).replace(/\n+/g, '|') : '无最近搜索区'; })));
  } else rec('/search', '无清空记录按钮');

  // ===== /browse 卡片精确可点性 =====
  await openPage(audit, '/browse', { waitMs: 2000 });
  const cardInfo = await page.evaluate(() => {
    const arts = [...document.querySelectorAll('article.content')];
    return arts.slice(0, 3).map(a => {
      const r = a.getBoundingClientRect();
      const anchors = a.querySelectorAll('a');
      const btns = a.querySelectorAll('button');
      return { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height), anchors: [...anchors].map(x => x.getAttribute('href')), btnCount: btns.length, cursor: getComputedStyle(a).cursor, txt: a.innerText.slice(0, 80).replace(/\n/g, '|') };
    });
  });
  rec('/browse', '卡片结构: ' + JSON.stringify(cardInfo));
  if (cardInfo.length) {
    const c = cardInfo[0];
    await page.mouse.click(c.x + c.w / 2, c.y + c.h / 2);
    await page.waitForTimeout(2000);
    rec('/browse', '点击第一张卡片中心后 url=' + page.url());
  }
  await shot(audit, 'i-browse-cardcenter');

  // ===== /cards 增强下拉 =====
  await openPage(audit, '/cards', { waitMs: 2000 });
  const ddInfo = await page.evaluate(() => {
    const sel = document.querySelector('select.form-select');
    if (!sel) return 'NO SELECT';
    const vis = getComputedStyle(sel).display;
    // 找自定义触发器
    const triggers = [...document.querySelectorAll('[data-enhanced], .dropdown-toggle, [class*=select]')].filter(e => e !== sel && getComputedStyle(e).display !== 'none').slice(0, 5).map(e => ({ tag: e.tagName, cls: (e.className || '').slice(0, 50), txt: e.innerText.slice(0, 30) }));
    return { nativeDisplay: vis, triggers };
  });
  rec('/cards', '下拉信息: ' + JSON.stringify(ddInfo));
  // 点击可见的下拉触发器
  const visibleTrigger = await page.evaluate(() => {
    const cands = [...document.querySelectorAll('div,button,span')].filter(e => (e.innerText || '').trim() === '全部知识库' && getComputedStyle(e).display !== 'none' && e.children.length <= 2);
    return cands.length ? { tag: cands[0].tagName, cls: (cands[0].className || '').slice(0, 50) } : null;
  });
  rec('/cards', '下拉触发器候选: ' + JSON.stringify(visibleTrigger));
  if (visibleTrigger) {
    await page.locator(`text=全部知识库`).first().click().catch(e => rec('/cards', '点下拉失败 ' + e));
    await page.waitForTimeout(1200);
    const opts = await page.evaluate(() => {
      const items = [...document.querySelectorAll('[class*=option], [class*=dropdown-item], [class*=menu] li, [class*=list] li')].filter(e => getComputedStyle(e).display !== 'none').map(e => e.innerText.trim().slice(0, 20));
      return items.slice(0, 15);
    });
    rec('/cards', '下拉选项: ' + JSON.stringify(opts));
    await shot(audit, 'i-cards-dropdown');
    // 选择一项
    const opt = page.locator('[class*=option], [class*=dropdown-item]').filter({ hasText: '烘焙初体验' }).first();
    if (await opt.count()) { await opt.click().catch(e => rec('/cards', '选选项失败 ' + e)); await page.waitForTimeout(1500); rec('/cards', '选烘焙后: ' + (await page.evaluate(() => document.body.innerText.slice(0, 260))).replace(/\n+/g, '|')); await shot(audit, 'i-cards-selected'); }
  }

  // ===== /generate 编辑提示词 + radio + 目标知识库 =====
  await openPage(audit, '/generate', { waitMs: 2000 });
  const genEls = await page.evaluate(() => {
    const edit = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('编辑提示词'));
    const radios = [...document.querySelectorAll('input[type=radio]')].map(r => { const lb = r.closest('label'); return { checked: r.checked, label: lb ? lb.innerText.trim().slice(0, 20) : '' }; });
    return { hasEdit: !!edit, radios };
  });
  rec('/generate', '元素: ' + JSON.stringify(genEls));
  // radio 切换：单条笔记
  const singleRadio = page.locator('input[type=radio]').nth(1);
  if (await singleRadio.count()) {
    await singleRadio.check({ force: true }).catch(e => rec('/generate', '切radio失败 ' + e));
    await page.waitForTimeout(1000);
    rec('/generate', '切到单条笔记后按钮文案: ' + (await page.evaluate(() => [...document.querySelectorAll('button')].filter(b => b.offsetParent !== null).map(b => b.innerText.trim()).filter(t => t.includes('生成')).join('|'))));
    await shot(audit, 'i-generate-single');
    await page.locator('input[type=radio]').nth(0).check({ force: true }).catch(() => {});
    await page.waitForTimeout(800);
  }
  // 编辑提示词 - 通过 JS 查找并点击
  const editClicked = await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('编辑提示词'));
    if (b) { b.click(); return true; }
    return false;
  });
  await page.waitForTimeout(1500);
  const promptModal = await page.evaluate(() => {
    const m = [...document.querySelectorAll('.modal-overlay, [class*=modal]')].filter(d => getComputedStyle(d).display !== 'none' && d.innerText.trim());
    return m.length ? m[0].innerText.slice(0, 300) : 'NO VISIBLE MODAL';
  });
  rec('/generate', '提示词modal(JS点击): ' + promptModal.replace(/\n+/g, '|'));
  if (promptModal !== 'NO VISIBLE MODAL') {
    await shot(audit, 'i-generate-promptmodal2');
    const c2 = page.locator('button:has-text("取消"), button:has-text("关闭")');
    if (await c2.count()) { await c2.first().click().catch(() => {}); await page.waitForTimeout(800); }
  }
  // 目标知识库 控件
  const target = await page.evaluate(() => {
    const labels = [...document.querySelectorAll('label, .form-label, h5, h6')].filter(e => (e.innerText || '').includes('目标知识库'));
    const around = labels.length ? labels[0].parentElement.innerText.slice(0, 200) : 'NOT FOUND';
    return around.replace(/\n+/g, '|');
  });
  rec('/generate', '目标知识库区: ' + target);

  // ===== /home 设备管理元素 =====
  await openPage(audit, '/', { waitMs: 2000 });
  const devEl = await page.evaluate(() => {
    const el = [...document.querySelectorAll('a,button,div,span')].find(e => (e.innerText || '').trim() === '设备管理' || ((e.innerText || '').includes('设备管理') && (e.innerText || '').length < 30));
    if (!el) return 'NOT FOUND';
    let n = el; const chain = [];
    for (let i = 0; i < 4 && n; i++) { chain.push(n.tagName + (n.getAttribute && n.getAttribute('href') ? ' href=' + n.getAttribute('href') : '') + '.' + ((n.className || '').toString().slice(0, 30))); n = n.parentElement; }
    return chain;
  });
  rec('/home', '设备管理元素链: ' + JSON.stringify(devEl));
  const devClick = await page.evaluate(() => {
    const el = [...document.querySelectorAll('a,button')].find(e => (e.innerText || '').includes('设备管理') && (e.innerText || '').length < 30);
    if (el) { el.click(); return 'CLICKED ' + el.tagName; }
    return 'NO CLICKABLE';
  });
  await page.waitForTimeout(1800);
  rec('/home', '设备管理点击后 url=' + page.url());

  // ===== /tasks 删除确认（先取消上一个modal） =====
  await openPage(audit, '/tasks', { waitMs: 2000 });
  const delBtn = page.locator('button:has-text("🗑️ 删除")').first();
  if (await delBtn.count()) {
    await delBtn.click().catch(e => rec('/tasks', '点删除失败 ' + e));
    await page.waitForTimeout(1200);
    const modal = await page.evaluate(() => {
      const m = [...document.querySelectorAll('.modal-overlay')].filter(d => getComputedStyle(d).display !== 'none');
      return m.length ? m[0].innerText.slice(0, 200) : 'NO MODAL';
    });
    rec('/tasks', '删除任务modal: ' + modal.replace(/\n+/g, '|'));
    await shot(audit, 'i-tasks-deleteconfirm');
    const cancel = page.locator('button:has-text("取消")');
    if (await cancel.count()) { await cancel.first().click().catch(() => {}); await page.waitForTimeout(800); rec('/tasks', '已取消删除'); }
  }
  // Escape 是否可关 modal
  const escTest = await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('清空历史'));
    if (b) b.click();
    return true;
  });
  await page.waitForTimeout(1000);
  const modalOpen = await page.evaluate(() => [...document.querySelectorAll('.modal-overlay')].some(d => getComputedStyle(d).display !== 'none'));
  rec('/tasks', '清空modal打开: ' + modalOpen);
  await page.keyboard.press('Escape');
  await page.waitForTimeout(1000);
  const modalAfterEsc = await page.evaluate(() => [...document.querySelectorAll('.modal-overlay')].some(d => getComputedStyle(d).display !== 'none'));
  rec('/tasks', 'Escape后modal仍开: ' + modalAfterEsc);
  if (modalAfterEsc) { // 点取消关掉
    const c = page.locator('button:has-text("取消")');
    if (await c.count()) { await c.first().click().catch(() => {}); await page.waitForTimeout(800); }
  }

  // ===== /messages 本地工具 + 模式按钮 =====
  await openPage(audit, '/messages', { waitMs: 2000 });
  const msgEls = await page.evaluate(() => {
    const btns = [...document.querySelectorAll('button')].filter(b => b.offsetParent !== null).map(b => ({ t: b.innerText.trim().slice(0, 24), title: (b.title || '').slice(0, 20), disabled: b.disabled }));
    return btns.filter(b => b.t.includes('GGUF') || b.t.includes('ONNX') || b.t.includes('远程') || b.t.includes('知识库') || b.t.includes('发送') || b.t.includes('本地工具')).slice(0, 12);
  });
  rec('/messages', '模式/知识库按钮: ' + JSON.stringify(msgEls));
  // 点 知识库 按钮（关闭/打开）
  const kbBtn = page.locator('button:has-text("知识库")').first();
  if (await kbBtn.count()) {
    await kbBtn.click().catch(e => rec('/messages', '点知识库按钮失败 ' + e));
    await page.waitForTimeout(1200);
    const after = await page.evaluate(() => {
      const m = [...document.querySelectorAll('.modal-overlay, [class*=modal]')].filter(d => getComputedStyle(d).display !== 'none');
      return m.length ? m[0].innerText.slice(0, 200) : (document.body.innerText.includes('选择知识库') ? '有选择知识库UI' : '无变化');
    });
    rec('/messages', '知识库按钮后: ' + after.replace(/\n+/g, '|'));
    await shot(audit, 'i-messages-kb');
    await page.keyboard.press('Escape').catch(() => {});
    await page.waitForTimeout(800);
  }

  fs.writeFileSync('pass2d.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
