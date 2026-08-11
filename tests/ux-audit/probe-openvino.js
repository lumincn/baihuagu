// 探测 /local-models OpenVINO tab
const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/local-models', { waitMs: 5000 });
    // 列出所有 tab 按钮
    const tabs = await audit.page.locator('.nav-tabs button').allInnerTexts();
    console.log('tabs:', JSON.stringify(tabs));
    const ovTab = audit.page.locator('.nav-tabs button', { hasText: 'OpenVINO' }).first();
    console.log('OpenVINO tab 数量:', await ovTab.count());
    await ovTab.click();
    await audit.page.waitForTimeout(4000);
    // 检查 active tab
    const active = await audit.page.locator('.nav-tabs button.active').allInnerTexts();
    console.log('active tab:', JSON.stringify(active));
    // 所有可见按钮（含文本）
    const btns = await audit.page.locator('button:visible').allInnerTexts();
    console.log('可见按钮:', JSON.stringify(btns.filter(t => t.includes('删') || t.includes('模型') || t.includes('运行') || t.includes('停止')).slice(0, 20)));
    // 表格行数
    const rows = await audit.page.locator('table tbody tr').count();
    console.log('表格行数:', rows);
    const bodyText = await audit.page.locator('body').innerText();
    console.log('页面文本片段:', bodyText.replace(/\n/g, ' ').slice(0, 300));
    await shot(audit, 'probe-openvino-tab');
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
