// B组交互测试 第二轮：budget 完整记一笔、achievements 校验、leaderboard tabs、dashboard 筛选、ai-metrics、onboarding、family 链接
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');
const sleep = (ms) => new Promise(r => setTimeout(r, ms));

(async () => {
  const audit = await startAudit();
  const { page } = audit;
  const log = [];
  const rec = (s) => { log.push(s); console.log(s); };

  // ===== budget 完整记一笔（会创建 1 条数据，备注已标注可删） =====
  await openPage(audit, '/family-budget', { waitMs: 2500 });
  rec('--- budget ---');
  await page.locator('input[type=number]').first().fill('1.00');
  // 打开增强下拉，选“餐饮”
  await page.locator('.enhanced-select-trigger').first().click();
  await sleep(500);
  rec('dropdown open: ' + (await page.locator('.enhanced-select-dropdown').first().isVisible().catch(()=>false)));
  await shot(audit, 'b2-budget-dropdown-open');
  await page.locator('.enhanced-select-option[data-value="餐饮"]').first().click().catch(e => rec('pick cat err: ' + e.message));
  await sleep(500);
  rec('trigger now: ' + (await page.locator('.enhanced-select-trigger').first().innerText().catch(()=>'?')));
  // 备注
  const noteInputs = await page.locator('input[type=text]').count();
  rec('text inputs count: ' + noteInputs);
  await page.locator('input[placeholder="备注（可选）"]').fill('UX审计-可删-20260811').catch(e => rec('note fill err: ' + e.message));
  await page.getByRole('button', { name: /^保存$/ }).click().catch(e => rec('save err: ' + e.message));
  await sleep(1800);
  rec('after save body: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(0, 700))));
  await shot(audit, 'b2-budget-after-save2');
  // 检查明细
  rec('明细区: ' + (await page.evaluate(() => Array.from(document.querySelectorAll('.transaction-row, .budget-item, [class*=txn], [class*=detail] li')).map(e=>e.innerText.replace(/\n+/g,' ')).join(' | ').slice(0,300))));
  // 删除刚创建的条目（清理）→ 同时测试删除功能
  const delBtn = page.locator('button[title*="删除"], button:has-text("删除"), [class*=delete]');
  rec('delete btn count: ' + (await delBtn.count()));
  if (await delBtn.count() > 0) {
    await delBtn.first().click().catch(e => rec('del click err: ' + e.message));
    await sleep(1000);
    rec('after del body: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(-350))));
    await shot(audit, 'b2-budget-after-delete');
  } else {
    rec('NO DELETE BUTTON visible in list');
  }

  // ===== achievements 校验 =====
  await openPage(audit, '/achievements', { waitMs: 2500 });
  rec('--- achievements ---');
  await page.getByRole('button', { name: /添加奖励/ }).click().catch(e => rec('add reward err: ' + e.message));
  await sleep(1000);
  rec('empty reward msg: ' + (await page.evaluate(() => { const a = document.querySelector('.alert, [class*=alert], [class*=error]'); return a ? a.innerText : '(none)'; })));
  await shot(audit, 'b2-achievements-addreward-empty2');
  // + 添加 按钮点开看看（不创建）
  const addBtn = page.locator('button:has-text("+ 添加")');
  rec('+添加 count: ' + (await addBtn.count()));
  if (await addBtn.count() > 0) {
    await addBtn.first().click().catch(e => rec('+添加 err: ' + e.message));
    await sleep(1000);
    rec('after +添加 body: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(-400))));
    await shot(audit, 'b2-achievements-addmember');
  }

  // ===== leaderboard tabs =====
  await openPage(audit, '/leaderboard', { waitMs: 2500 });
  rec('--- leaderboard ---');
  rec('initial tabs: ' + JSON.stringify(await page.locator('.tab-btn, [class*=tab] button, button:has-text("比"), button:has-text("排行")').allInnerTexts().catch(()=>[])));
  await page.getByRole('button', { name: /家庭排行/ }).click().catch(e => rec('famtab err: ' + e.message));
  await sleep(800);
  rec('family tab: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(-400))));
  await shot(audit, 'b2-leaderboard-familytab2');
  await page.getByRole('button', { name: /和自己比/ }).click().catch(e => rec('selftab err: ' + e.message));
  await sleep(800);
  rec('self tab: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(-400))));
  await shot(audit, 'b2-leaderboard-selftab2');

  // ===== dashboard 成员筛选 =====
  await openPage(audit, '/dashboard', { waitMs: 2500 });
  rec('--- dashboard ---');
  const mb = page.locator('button:has-text("小明")');
  rec('member btn count: ' + (await mb.count()));
  await mb.first().click().catch(e => rec('filter err: ' + e.message));
  await sleep(1000);
  rec('filtered body: ' + (await page.evaluate(() => document.body.innerText.replace(/\n+/g,' ').slice(0, 500))));
  await shot(audit, 'b2-dashboard-filtered2');

  // ===== ai-metrics 时间范围 =====
  await openPage(audit, '/ai-metrics', { waitMs: 2500 });
  rec('--- ai-metrics ---');
  rec('range btns: ' + JSON.stringify(await page.locator('button:has-text("天"), button:has-text("天")').allInnerTexts().catch(()=>[])));
  await page.getByRole('button', { name: /30 天/ }).click().catch(e => rec('30d err: ' + e.message));
  await sleep(1000);
  const sum30 = await page.evaluate(() => document.body.innerText.match(/\d+\s*总调用次数[\s\S]{0,120}/)?.[0] || '?');
  rec('30d summary: ' + sum30.replace(/\n+/g,' '));
  await shot(audit, 'b2-aimetrics-30d2');
  // chart 是否渲染（canvas/svg）
  rec('chart elements: ' + JSON.stringify(await page.evaluate(() => Array.from(document.querySelectorAll('canvas, svg, [class*=chart]')).map(e => e.tagName + '.' + e.className).slice(0, 8))));

  // ===== onboarding =====
  await openPage(audit, '/onboarding', { waitMs: 2500 });
  rec('--- onboarding ---');
  rec('step labels: ' + (await page.evaluate(() => Array.from(document.querySelectorAll('[class*=step]')).map(e=>e.innerText.trim()).filter(Boolean).join('|').slice(0,150))));
  const kbLink = page.getByRole('link', { name: /进入知识库/ });
  rec('进入知识库 exists: ' + (await kbLink.count()));
  if (await kbLink.count()) {
    await kbLink.click().catch(e => rec('kb link err: ' + e.message));
    await sleep(1500);
    rec('navigated to: ' + page.url());
  }

  // ===== family 链接 =====
  await openPage(audit, '/family', { waitMs: 2500 });
  rec('--- family links ---');
  await page.getByRole('link', { name: /查看全部/ }).click().catch(e => rec('seeall err: ' + e.message));
  await sleep(1200);
  rec('查看全部 -> ' + page.url());

  rec('=== ISSUES ===');
  rec(JSON.stringify(audit.issues, null, 2));
  fs.writeFileSync('interact-b2-log.txt', log.join('\n'));
  await audit.browser.close();
})();
