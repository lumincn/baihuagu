// B组交互测试：daily-card 翻转/答题、家长出题校验、checkin 补签弹窗、quiz 开始互考、
// budget 校验+记一笔、achievements 添加奖励校验、leaderboard tabs、dashboard 成员筛选、ai-metrics 时间范围
const { startAudit, openPage, shot } = require('./login');

const sleep = (ms) => new Promise(r => setTimeout(r, ms));
const txt = async (p, sel) => p.locator(sel).innerText().catch(() => '(n/a)');

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (s) => { log.push(s); console.log(s); };

  // ============ 1) /daily-card 核心流程 ============
  await openPage(audit, '/daily-card', { waitMs: 2500 });
  rec('--- 1) daily-card ---');
  rec('progress: ' + (await txt(page, '.progress-text').catch(()=>'')));
  // 点击卡片翻转
  await page.locator('.card-container').first().click().catch(e => rec('flip click err: ' + e.message));
  await sleep(900);
  rec('after flip -> answer buttons visible: ' + (await page.locator('.answer-buttons').isVisible().catch(()=>false)));
  rec('flip-hint visible: ' + (await page.locator('.flip-hint').isVisible().catch(()=>false)));
  await shot(audit, 'b2-dailycard-flipped');
  // 提交 remember（产生 1 条学习记录，需清理）
  const cardFront = await txt(page, '.card-front .card-content').catch(()=>'?');
  rec('card front: ' + cardFront);
  await page.getByRole('button', { name: /记得|remember/i }).click().catch(e => rec('submit err: ' + e.message));
  await sleep(1500);
  rec('after submit, progress: ' + (await txt(page, '.progress-text').catch(()=>'')));
  rec('after submit body snippet: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(0,300))));
  await shot(audit, 'b2-dailycard-after-answer');
  // 家长出题：展开 + 空提交
  await page.locator('.custom-card-toggle').click().catch(e => rec('details open err: ' + e.message));
  await sleep(600);
  rec('custom form visible: ' + (await page.locator('.custom-card-form').isVisible().catch(()=>false)));
  await shot(audit, 'b2-dailycard-customform');
  await page.getByRole('button', { name: /保存卡片|saving/i }).first().click().catch(e => rec('empty save err: ' + e.message));
  await sleep(1200);
  rec('empty save message: ' + (await txt(page, '.custom-card-form .alert').catch(()=>'(none)')));
  await shot(audit, 'b2-dailycard-custom-empty-submit');

  // ============ 2) /checkin 补签弹窗 ============
  await openPage(audit, '/checkin', { waitMs: 2500 });
  rec('--- 2) checkin ---');
  rec('streak text: ' + (await txt(page, '.streak-banner').catch(()=>'?')));
  const makeupCells = page.locator('.cal-cell.makeupable');
  rec('makeupable cells: ' + (await makeupCells.count()));
  if (await makeupCells.count() > 0) {
    await makeupCells.first().click();
    await sleep(900);
    rec('makeup dialog visible: ' + (await page.locator('.makeup-dialog').isVisible().catch(()=>false)));
    rec('dialog text: ' + (await txt(page, '.makeup-dialog').catch(()=>'?')));
    await shot(audit, 'b2-checkin-makeup-dialog');
    // 取消，不产生数据
    const cancelBtn = page.locator('.makeup-dialog button').last();
    await cancelBtn.click().catch(e => rec('cancel err: ' + e.message));
    await sleep(600);
    rec('dialog closed: ' + (!(await page.locator('.makeup-dialog').isVisible().catch(()=>true))));
  }

  // ============ 3) /family/quiz 开始互考 ============
  await openPage(audit, '/family/quiz', { waitMs: 2500 });
  rec('--- 3) quiz ---');
  const selA = page.locator('select').nth(0); const selB = page.locator('select').nth(1);
  rec('memberA options: ' + JSON.stringify(await selA.locator('option').allInnerTexts().catch(()=>[])));
  rec('memberB options: ' + JSON.stringify(await selB.locator('option').allInnerTexts().catch(()=>[])));
  await page.getByRole('button', { name: /开始互考/ }).click().catch(e => rec('start err: ' + e.message));
  await sleep(2000);
  rec('after start url: ' + page.url());
  rec('after start body: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(0,400))));
  await shot(audit, 'b2-quiz-after-start');

  // ============ 4) /family-budget 记一笔 ============
  await openPage(audit, '/family-budget', { waitMs: 2500 });
  rec('--- 4) budget ---');
  const catOptions = await page.locator('select').nth(0).locator('option').allInnerTexts().catch(()=>[]);
  rec('category options: ' + JSON.stringify(catOptions));
  // 空金额保存
  await page.getByRole('button', { name: /保存/ }).click().catch(e => rec('save0 err: ' + e.message));
  await sleep(900);
  rec('save empty msg: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(-400))));
  await shot(audit, 'b2-budget-save-empty');
  // 填金额但不选分类
  await page.locator('input[type=number]').first().fill('12.34');
  await page.getByRole('button', { name: /保存/ }).click().catch(e => rec('save1 err: ' + e.message));
  await sleep(900);
  rec('save no-cat msg: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(-400))));
  await shot(audit, 'b2-budget-save-nocat');
  // 完整记一笔：支出 1.00 餐饮（创建数据，备注标注可删）
  await page.locator('input[type=number]').first().fill('1.00');
  await page.locator('select').nth(0).selectOption({ index: 1 }).catch(e => rec('selcat err: ' + e.message));
  await page.locator('input[type=text]').nth(1).fill('UX审计-可删-20260811');
  await page.getByRole('button', { name: /保存/ }).click().catch(e => rec('save2 err: ' + e.message));
  await sleep(1500);
  rec('after save body: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(0,600))));
  await shot(audit, 'b2-budget-after-save');

  // ============ 5) /achievements 添加奖励空提交 ============
  await openPage(audit, '/achievements', { waitMs: 2500 });
  rec('--- 5) achievements ---');
  await page.getByRole('button', { name: /添加奖励/ }).click().catch(e => rec('add reward err: ' + e.message));
  await sleep(1000);
  rec('add reward msg: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(-300))));
  await shot(audit, 'b2-achievements-addreward-empty');
  // 检查 + 添加 按钮
  const addBtn = page.getByRole('button', { name: /\+ 添加/ });
  rec('+添加 exists: ' + (await addBtn.count()));
  rec('member selector btns: ' + JSON.stringify(await page.locator('button', { hasText: '小明' }).allInnerTexts().catch(()=>[])));

  // ============ 6) /leaderboard tabs ============
  await openPage(audit, '/leaderboard', { waitMs: 2500 });
  rec('--- 6) leaderboard ---');
  await page.getByRole('button', { name: /家庭排行/ }).click().catch(e => rec('tab2 err: ' + e.message));
  await sleep(800);
  rec('family tab body: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(-500))));
  await shot(audit, 'b2-leaderboard-familytab');
  await page.getByRole('button', { name: /和自己比/ }).click().catch(e => rec('tab1 err: ' + e.message));
  await sleep(800);
  rec('self tab body: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(-500))));
  await shot(audit, 'b2-leaderboard-selftab');

  // ============ 7) /dashboard 成员筛选 ============
  await openPage(audit, '/dashboard', { waitMs: 2500 });
  rec('--- 7) dashboard ---');
  const memberBtns = page.locator('.member-filter button, button', { hasText: '小明' });
  rec('member filter btns count: ' + (await memberBtns.count()));
  if (await memberBtns.count() > 0) {
    await memberBtns.first().click();
    await sleep(900);
    rec('after filter body: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(0,400))));
    await shot(audit, 'b2-dashboard-filtered');
  }

  // ============ 8) /ai-metrics 时间范围 ============
  await openPage(audit, '/ai-metrics', { waitMs: 2500 });
  rec('--- 8) ai-metrics ---');
  await page.getByRole('button', { name: /1 天/ }).click().catch(e => rec('t1 err: ' + e.message));
  await sleep(900);
  rec('1d summary: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').match(/\d+\s*\n?总调用次数[^📊]*/)?.[0] || '?')));
  await shot(audit, 'b2-aimetrics-1d');
  await page.getByRole('button', { name: /30 天/ }).click().catch(e => rec('t30 err: ' + e.message));
  await sleep(900);
  await shot(audit, 'b2-aimetrics-30d');

  // ============ 9) /onboarding ============
  await openPage(audit, '/onboarding', { waitMs: 2500 });
  rec('--- 9) onboarding ---');
  rec('steps: ' + (await page.evaluate(() => Array.from(document.querySelectorAll('.step, .onboarding-step, [class*=step]')).map(e=>e.innerText.trim()).filter(Boolean).join(' | ').slice(0,200))));
  await page.getByRole('link', { name: /进入知识库/ }).click().catch(e => rec('enter kb err: ' + e.message));
  await sleep(1500);
  rec('after 进入知识库 url: ' + page.url());

  // ============ 10) /family 链接 ============
  await openPage(audit, '/family', { waitMs: 2500 });
  rec('--- 10) family links ---');
  await page.getByRole('link', { name: /查看全部/ }).click().catch(e => rec('seeall err: ' + e.message));
  await sleep(1200);
  rec('查看全部 -> url: ' + page.url());

  rec('=== ISSUES ===');
  rec(JSON.stringify(audit.issues, null, 2));
  require('fs').writeFileSync('interact-b-log.txt', log.join('\n'));
  await audit.browser.close();
})();
