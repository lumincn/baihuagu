// 探测记账页增强下拉结构
const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/family-budget', { waitMs: 2500 });
    const html = await audit.page.locator('select.form-select').first().evaluate(el => el.outerHTML);
    console.log('select HTML:', html.slice(0, 400));
    // 找增强组件的触发元素（前一个兄弟或父容器内的可见可点元素）
    const trigger = await audit.page.evaluate(() => {
      const sel = document.querySelector('select.form-select');
      if (!sel) return null;
      // 向上找容器，列出可见的交互元素
      let p = sel.parentElement;
      const items = [];
      for (let i = 0; i < 4 && p; i++) {
        p.querySelectorAll('button, [role="combobox"], .dropdown-toggle, input').forEach(el => {
          const r = el.getBoundingClientRect();
          if (r.width > 0 && r.height > 0) items.push({ tag: el.tagName, cls: el.className, text: el.textContent.trim().slice(0, 30), ph: el.placeholder || '' });
        });
        p = p.parentElement;
      }
      return items.slice(0, 12);
    });
    console.log('可见交互元素:', JSON.stringify(trigger, null, 2));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
