// 收尾探测：onboarding CTA 元素类型、quiz 退出选项、family 最新状态、ai-metrics 图表结构
const { startAudit, openPage, shot } = require('./login');
(async () => {
  const audit = await startAudit();

  // onboarding CTA
  await openPage(audit, '/onboarding', { waitMs: 2000 });
  const cta = await audit.page.evaluate(() => {
    const els = Array.from(document.querySelectorAll('a,button')).filter(e => e.innerText.includes('进入知识库'));
    return els.map(e => ({ tag: e.tagName, cls: e.className, href: e.getAttribute('href'), onclick: e.getAttribute('onclick') }));
  });
  console.log('ONBOARDING CTA:', JSON.stringify(cta));

  // quiz 退出选项
  await openPage(audit, '/family/quiz', { waitMs: 2000 });
  await audit.page.getByRole('button', { name: /开始互考/ }).click().catch(e=>{});
  await audit.page.waitForTimeout(1800);
  const qz = await audit.page.evaluate(() => {
    const body = document.body.innerText;
    return {
      hasQuit: /退出|取消|离开|放弃/.test(body),
      btns: Array.from(document.querySelectorAll('button')).map(b => b.innerText.trim()).filter(Boolean).slice(0, 12),
      timer: body.match(/\d+\s*秒/)?.[0] || null
    };
  });
  console.log('QUIZ after start:', JSON.stringify(qz, null, 1));
  await shot(audit, 'b-quiz-started');

  // family 最新状态（我创建数据后）
  await openPage(audit, '/family', { waitMs: 2200 });
  const fam = await audit.page.evaluate(() => {
    const stats = Array.from(document.querySelectorAll('.member-card')).map(c => c.innerText.replace(/\n+/g, ' '));
    const lb = Array.from(document.querySelectorAll('.lb-name, [class*=leaderboard] span, .lb-entry')).map(e => e.innerText.replace(/\n+/g,' ')).filter(Boolean).slice(0, 8);
    return { memberCards: stats, leaderboardArea: lb, body: document.body.innerText.replace(/\n+/g, ' ').slice(0, 900) };
  });
  console.log('FAMILY now:', JSON.stringify(fam, null, 1));

  // ai-metrics 图表结构
  await openPage(audit, '/ai-metrics', { waitMs: 2200 });
  const chart = await audit.page.evaluate(() => {
    const c = document.querySelector('.trend-chart');
    return c ? { html: c.outerHTML.slice(0, 700), bars: c.querySelectorAll('[class*=bar], [class*=col]').length } : null;
  });
  console.log('AI-METRICS chart:', JSON.stringify(chart, null, 1));

  await audit.browser.close();
})();
