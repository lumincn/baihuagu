// Pass 2f: 结构深挖 + 对话框监听 + 响应式
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (route, msg, extra = {}) => log.push({ route, msg, ...extra });
  let dialogs = [];
  page.on('dialog', async (d) => { dialogs.push(d.type() + ':' + d.message().slice(0, 80)); await d.dismiss(); });

  // ===== /browse 卡片结构（从听按钮向上爬） =====
  await openPage(audit, '/browse', { waitMs: 2000 });
  const cardChain = await page.evaluate(() => {
    const listen = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('🎧'));
    if (!listen) return 'NO LISTEN BTN';
    const chain = [];
    let n = listen;
    for (let i = 0; i < 6 && n; i++) {
      const r = n.getBoundingClientRect();
      chain.push({ tag: n.tagName, cls: (n.className || '').toString().slice(0, 45), w: Math.round(r.width), h: Math.round(r.height), txt: (n.innerText || '').slice(0, 40).replace(/\n/g, '|') });
      n = n.parentElement;
    }
    return chain;
  });
  rec('/browse', '听按钮祖先链: ' + JSON.stringify(cardChain));
  // 听按钮点击的网络请求
  const reqs = [];
  page.on('request', r => { if (r.url().includes('api') || r.url().includes('tts') || r.url().includes('audio') || r.url().includes('speak')) reqs.push(r.method() + ' ' + r.url()); });
  await page.locator('button:has-text("🎧 听")').first().click().catch(() => {});
  await page.waitForTimeout(3000);
  rec('/browse', '听按钮相关请求: ' + JSON.stringify(reqs));
  const uiChange = await page.evaluate(() => {
    const btns = [...document.querySelectorAll('button')].map(b => b.innerText.trim());
    return { hasStop: btns.some(t => t.includes('停止') || t.includes('暂停')), anyPlaying: [...document.querySelectorAll('*')].some(e => (e.className || '').toString().includes('playing') || (e.className || '').toString().includes('speaking')) };
  });
  rec('/browse', '听后UI变化: ' + JSON.stringify(uiChange));

  // ===== /tasks 删除对话框监听 =====
  await openPage(audit, '/tasks', { waitMs: 2000 });
  dialogs = [];
  const del = page.locator('button:has-text("🗑️ 删除")').first();
  if (await del.count()) {
    await del.click().catch(e => rec('/tasks', '删除点击异常 ' + e));
    await page.waitForTimeout(3000);
    const st = await page.evaluate(() => {
      const m = [...document.querySelectorAll('.modal-overlay')].filter(d => getComputedStyle(d).display !== 'none');
      return { modal: m.length ? m[0].innerText.slice(0, 120) : 'NONE', bodyLen: document.body.innerText.length };
    });
    rec('/tasks', '删除后(3s): dialogs=' + JSON.stringify(dialogs) + ' modal=' + st.modal.replace(/\n+/g, '|') + ' bodyLen=' + st.bodyLen);
    await shot(audit, 'i-tasks-del3s');
  }

  // ===== /search 知识库下拉 options + 精确错误 =====
  await openPage(audit, '/search', { waitMs: 2000 });
  const selInfo = await page.evaluate(() => {
    const sels = [...document.querySelectorAll('select')];
    return sels.map(s => ({ name: s.name || '', cls: (s.className || '').slice(0, 30), opts: [...s.options].map(o => o.text), hidden: getComputedStyle(s).display === 'none' }));
  });
  rec('/search', '下拉: ' + JSON.stringify(selInfo));
  await page.fill('input[placeholder*="搜索笔记"]', 'zzzqq');
  await page.keyboard.press('Enter');
  await page.waitForTimeout(3000);
  const err = await page.evaluate(() => {
    const t = document.body.innerText;
    const i = t.indexOf('⚠️');
    return { seg: i >= 0 ? t.slice(i, i + 100).replace(/\n+/g, '|') : 'NO WARN', hist: t.includes('最近搜索') ? t.slice(t.indexOf('最近搜索'), t.indexOf('最近搜索') + 100).replace(/\n+/g, '|') : 'NO HIST' };
  });
  rec('/search', 'zzzqq错误: ' + JSON.stringify(err));
  await shot(audit, 'i-search-zzzqq');
  // 清空记录
  const cb = page.locator('button:has-text("清空记录")');
  if (await cb.count()) { await cb.first().click().catch(e => rec('/search', '清空失败 ' + e)); await page.waitForTimeout(1500); rec('/search', '清空后历史区: ' + (await page.evaluate(() => { const t = document.body.innerText; return t.includes('最近搜索') ? t.slice(t.indexOf('最近搜索'), t.indexOf('最近搜索') + 80).replace(/\n+/g, '|') : 'NO HIST'; }))); await shot(audit, 'i-search-cleared2'); }

  // ===== /messages 知识库 chip 文本变化 =====
  await openPage(audit, '/messages', { waitMs: 2000 });
  const kbBefore = await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('知识库'));
    return b ? b.innerText.replace(/\n+/g, '|') : 'NONE';
  });
  rec('/messages', '知识库chip前: ' + kbBefore);
  await page.locator('button:has-text("知识库")').first().click().catch(() => {});
  await page.waitForTimeout(1500);
  const kbAfter = await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('知识库'));
    return b ? b.innerText.replace(/\n+/g, '|') : 'NONE';
  });
  rec('/messages', '知识库chip后: ' + kbAfter);
  await shot(audit, 'i-messages-kb3');

  // ===== /generate 目标知识库 select =====
  await openPage(audit, '/generate', { waitMs: 2000 });
  const genSel = await page.evaluate(() => {
    const sels = [...document.querySelectorAll('select')];
    return sels.map(s => ({ opts: [...s.options].map(o => o.text).slice(0, 10), hidden: getComputedStyle(s).display === 'none', cls: (s.className || '').slice(0, 30) }));
  });
  rec('/generate', '下拉: ' + JSON.stringify(genSel));

  // ===== /assistant 状态 =====
  await openPage(audit, '/assistant', { waitMs: 2000 });
  const asst = await page.evaluate(() => {
    const b = [...document.querySelectorAll('button')].find(b => (b.innerText || '').includes('立即分析'));
    const cbx = document.querySelector('input[type=checkbox]');
    return { analyzeDisabled: b ? b.disabled : 'N/A', cbxChecked: cbx ? cbx.checked : 'N/A', cbxLabel: cbx && cbx.closest('label') ? cbx.closest('label').innerText.slice(0, 60) : '' };
  });
  rec('/assistant', '状态: ' + JSON.stringify(asst));
  await shot(audit, 'i-assistant-state');

  // ===== /note 编辑器保存按钮 =====
  await openPage(audit, '/note?id=' + encodeURIComponent('基础认识/烘焙必备工具清单及用途'), { waitMs: 2500 });
  await page.locator('button:has-text("✏️")').first().click().catch(() => {});
  await page.waitForTimeout(1200);
  const editorState = await page.evaluate(() => {
    const ta = document.querySelector('textarea');
    const btns = [...document.querySelectorAll('button')].filter(b => b.offsetParent !== null).map(b => b.innerText.trim()).slice(0, 12);
    return { hasTa: !!ta, taVal: ta ? ta.value.slice(0, 60) : '', btns };
  });
  rec('/note', '编辑器: ' + JSON.stringify(editorState));
  await shot(audit, 'i-note-editor');
  // 不保存，按 Escape / 找取消
  await page.keyboard.press('Escape').catch(() => {});
  await page.waitForTimeout(800);

  fs.writeFileSync('pass2f.json', JSON.stringify(log, null, 2));
  console.log('ISSUES: ' + JSON.stringify(audit.issues, null, 2));
  console.log('DONE');
  await audit.browser.close();
})();
