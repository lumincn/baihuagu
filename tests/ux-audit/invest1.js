// 深度 DOM 调查：Retry/Resume 按钮来源、成员重复、checkin 日历、daily-card 输入框
const { startAudit, openPage } = require('./login');

(async () => {
  const audit = await startAudit();

  // 1) Retry/Resume 在哪？
  await openPage(audit, '/family', { waitMs: 2000 });
  const rr = await audit.page.evaluate(() => {
    const btns = Array.from(document.querySelectorAll('button')).filter(b => /Retry|Resume/i.test(b.innerText));
    return btns.map(b => {
      const r = b.getBoundingClientRect();
      const vis = getComputedStyle(b).visibility;
      const d = b.closest('[style*="display"]');
      return {
        text: b.innerText.trim().slice(0, 30),
        rect: { x: Math.round(r.x), y: Math.round(r.y), w: Math.round(r.width), h: Math.round(r.height) },
        visible: r.width > 0 && r.height > 0,
        visibility: vis,
        cls: b.className,
        ancestor: b.parentElement ? (b.parentElement.className + ' | ' + b.parentElement.parentElement?.className) : '',
        ancestorVisible: b.parentElement ? !!(b.parentElement.getBoundingClientRect().width) : false
      };
    });
  });
  console.log('RETRY/RESUME BUTTONS:', JSON.stringify(rr, null, 2));

  // 2) 家庭成员重复：查 API
  const apis = ['/api/family/members', '/api/members', '/api/family/member', '/api/family', '/api/checkin/members', '/api/quiz/members'];
  for (const a of apis) {
    const resp = await audit.page.request.get(audit.BASE + a).catch(e => null);
    if (resp && resp.ok()) {
      const txt = await resp.text();
      console.log('API', a, '=>', txt.slice(0, 800));
    } else {
      console.log('API', a, '=>', resp ? resp.status() : 'ERR');
    }
  }

  // 3) family 页成员网格结构
  const fam = await audit.page.evaluate(() => {
    const out = {};
    const h1 = document.querySelector('h1');
    out.h1HTML = h1 ? h1.innerHTML : null;
    // 家庭成员卡片
    const cards = Array.from(document.querySelectorAll('div')).filter(d => d.innerText && /^🙂/.test(d.innerText.trim()) && d.innerText.trim().length < 60 && d.querySelector('div'));
    out.memberCardTexts = cards.slice(0, 10).map(d => d.innerText.trim());
    // 全页所有含 小明 的元素
    const all = Array.from(document.querySelectorAll('*')).filter(el => el.children.length === 0 && el.textContent.includes('小明')).map(el => ({
      tag: el.tagName, cls: el.className, text: el.textContent.trim().slice(0, 40)
    }));
    out.xiaoming = all.slice(0, 20);
    return out;
  });
  console.log('FAMILY PAGE:', JSON.stringify(fam, null, 2));

  await audit.browser.close();
})();
