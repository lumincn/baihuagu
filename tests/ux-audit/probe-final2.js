// 最后核对：onboarding CTA 点击跳转、checkin 今日清单、dashboard 今日三件事
const { startAudit, openPage, shot } = require('./login');
(async () => {
  const audit = await startAudit();
  await openPage(audit, '/onboarding', { waitMs: 2000 });
  await audit.page.getByRole('button', { name: /进入知识库/ }).click().catch(e => console.log('cta click err:', e.message));
  await audit.page.waitForTimeout(1800);
  console.log('onboarding CTA ->', audit.page.url());

  await openPage(audit, '/checkin', { waitMs: 2200 });
  const ck = await audit.page.evaluate(() => {
    const list = Array.from(document.querySelectorAll('[class*=record], [class*=today-list] li, .checkin-records div')).map(e => e.innerText.replace(/\n+/g,' ')).filter(Boolean).slice(0, 6);
    return { streak: document.querySelector('.streak-banner')?.innerText.replace(/\n+/g,' '), records: list, body: document.body.innerText.replace(/\n+/g,' ').slice(200, 600) };
  });
  console.log('CHECKIN now:', JSON.stringify(ck, null, 1));
  await shot(audit, 'b-checkin-final');

  await openPage(audit, '/dashboard', { waitMs: 2200 });
  const d = await audit.page.evaluate(() => ({ body: document.body.innerText.replace(/\n+/g,' ').slice(0, 800) }));
  console.log('DASHBOARD now:', d.body);
  await audit.browser.close();
})();
