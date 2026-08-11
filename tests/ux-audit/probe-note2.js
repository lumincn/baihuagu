const { startAudit, openPage } = require('./login');
(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/note?id=' + encodeURIComponent('基础概念/过敏体质的概念与辨识') + '&vaultId=' + encodeURIComponent('中医抗敏'), { waitMs: 3000 });
    const body = await audit.page.locator('body').innerText().catch(() => '');
    console.log('笔记加载:', body.includes('过敏体质的概念与辨识') ? '✅' : '❌', '| 含Anki:', body.includes('Anki'));
    console.log('Anki按钮数:', await audit.page.locator('button', { hasText: 'Anki' }).count());
    await audit.page.screenshot({ path: 'shots/fix3-note-anki2.png', fullPage: true });
    // local-models 骨架 class 探测
    await openPage(audit, '/local-models', { waitMs: 600 });
    const classes = await audit.page.evaluate(() => Array.from(document.querySelectorAll('div')).filter(d => d.className && String(d.className).includes('spinner')).map(d => d.className).slice(0, 5));
    console.log('spinner 相关 class:', JSON.stringify(classes));
    const mainText = await audit.page.locator('main, .container, .page').first().innerText().catch(() => '');
    console.log('首屏文本:', mainText.replace(/\n/g, ' ').slice(0, 120));
  } finally { await audit.browser.close(); }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
