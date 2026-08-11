// checkin 日历状态复核：哪些天可补签、点击取消确认不产生数据
const { startAudit, openPage, shot } = require('./login');
(async () => {
  const audit = await startAudit();
  await openPage(audit, '/checkin', { waitMs: 2500 });
  const cells = await audit.page.evaluate(() => {
    return Array.from(document.querySelectorAll('.cal-cell')).map(c => ({
      date: c.querySelector('.cal-date')?.innerText,
      mark: c.querySelector('.cal-mark')?.innerText,
      makeupable: c.classList.contains('makeupable'),
      today: c.classList.contains('today'),
      cls: c.className
    }));
  });
  console.log('CALENDAR:', JSON.stringify(cells, null, 2));
  console.log('streak:', await audit.page.locator('.streak-banner').innerText().catch(()=>'?'));
  // 点第一个可补签的天，然后点取消（第一个按钮）
  const m = audit.page.locator('.cal-cell.makeupable');
  const n = await m.count();
  console.log('makeupable count:', n);
  if (n > 0) {
    const d = await m.first().locator('.cal-date').innerText();
    await m.first().click();
    await audit.page.waitForTimeout(900);
    const btns = await audit.page.locator('.makeup-dialog button').allInnerTexts();
    console.log('dialog buttons:', JSON.stringify(btns));
    console.log('dialog text:', (await audit.page.locator('.makeup-dialog').innerText().catch(()=>'')));
    await shot(audit, 'b2-checkin-makeup-dialog2');
    // 点“取消”
    await audit.page.locator('.makeup-dialog button').first().click();
    await audit.page.waitForTimeout(700);
    console.log('dialog open after cancel:', await audit.page.locator('.makeup-dialog').isVisible().catch(()=>false));
  }
  await audit.browser.close();
})();
