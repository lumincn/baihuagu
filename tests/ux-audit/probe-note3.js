const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/note?id=' + encodeURIComponent('基础概念/过敏体质的概念与辨识') + '&vaultId=' + encodeURIComponent('中医抗敏'), { waitMs: 3000 });
    const actionsHtml = await audit.page.locator('.note-actions').first().evaluate(el => el.outerHTML).catch(e => 'ERR: ' + e.message);
    console.log('note-actions HTML:', actionsHtml.slice(0, 400));
    const allBtns = await audit.page.locator('button').allInnerTexts().catch(() => []);
    console.log('全部按钮:', JSON.stringify(allBtns.slice(0, 10)));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
