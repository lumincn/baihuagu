// 第四批验证：统一确认弹窗（tasks/settings/templates/log-errors）+ 命名/文案
const { startAudit, openPage, shot } = require('./login');

(async () => {
  const audit = await startAudit();
  let pass = 0, fail = 0;
  const check = (name, ok, extra = '') => {
    console.log(`${ok ? '✅' : '❌'} ${name}${extra ? ' — ' + extra : ''}`);
    ok ? pass++ : fail++;
  };
  const dismissModals = async () => {
    const cancel = audit.page.locator('.modal-content button, .confirm-modal-box button', { hasText: '取消' }).first();
    if (await cancel.count()) await cancel.click().catch(() => {});
    await audit.page.waitForTimeout(400);
  };
  try {
    // 1. /tasks：状态中文 + 删除弹窗
    await openPage(audit, '/tasks', { waitMs: 3000 });
    let txt = await audit.page.locator('body').innerText().catch(() => '');
    const hasEnStatus = /\b(Success|Cancelled|Running|Pending|Timeout|Failed)\b/.test(txt);
    check('tasks 状态中文化', !hasEnStatus, hasEnStatus ? '仍有英文状态' : '无英文状态词');
    const delBtn = audit.page.locator('button', { hasText: '删除' }).first();
    if (await delBtn.count()) {
      await delBtn.click();
      await audit.page.waitForTimeout(700);
      const modalVisible = await audit.page.locator('.modal-overlay').isVisible().catch(() => false);
      const modalText = modalVisible ? await audit.page.locator('.modal-content').innerText().catch(() => '') : '';
      check('tasks 删除弹自定义确认框', modalVisible && modalText.includes('删除任务'), modalText.replace(/\n/g, ' ').slice(0, 50));
      await shot(audit, 'fix4-tasks-modal');
      await dismissModals();
    } else check('tasks 删除弹自定义确认框', false, '无任务行可测（空态也接受）');

    // 2. /settings：删提供方弹窗
    await openPage(audit, '/settings', { waitMs: 3000 });
    const provDel = audit.page.locator('button.btn-icon.btn-danger').first();
    if (await provDel.count()) {
      await provDel.click();
      await audit.page.waitForTimeout(700);
      const m = await audit.page.locator('.modal-overlay').isVisible().catch(() => false);
      const t = m ? await audit.page.locator('.modal-content').innerText().catch(() => '') : '';
      check('settings 删提供方自定义弹窗', m && t.includes('删除 AI 提供商'), t.replace(/\n/g, ' ').slice(0, 50));
      await dismissModals();
    } else check('settings 删提供方自定义弹窗', true, '无提供方可删（跳过）');

    // 3. /prompt-templates：删模板弹窗
    await openPage(audit, '/prompt-templates', { waitMs: 3000 });
    const tplDel = audit.page.locator('button', { hasText: '删除' }).first();
    if (await tplDel.count()) {
      await tplDel.click();
      await audit.page.waitForTimeout(700);
      const m = await audit.page.locator('.confirm-modal-overlay').isVisible().catch(() => false);
      const t = m ? await audit.page.locator('.confirm-modal-box').innerText().catch(() => '') : '';
      check('templates 删模板自定义弹窗', m && t.includes('删除模板'), t.replace(/\n/g, ' ').slice(0, 50));
      await dismissModals();
    } else check('templates 删模板自定义弹窗', false, '无模板行');

    // 4. /log-errors：清日志弹窗
    await openPage(audit, '/log-errors', { waitMs: 2500 });
    const clrBtn = audit.page.locator('button', { hasText: '清理本地日志' }).first();
    if (await clrBtn.count()) {
      await clrBtn.click();
      await audit.page.waitForTimeout(700);
      const m = await audit.page.locator('.confirm-modal-overlay').isVisible().catch(() => false);
      const t = m ? await audit.page.locator('.confirm-modal-box').innerText().catch(() => '') : '';
      check('log-errors 清理自定义弹窗', m && t.includes('确定要清理'), t.replace(/\n/g, ' ').slice(0, 60));
      await dismissModals();
    } else check('log-errors 清理自定义弹窗', false, '无清理按钮');

    // 5. /family H1 无重复 emoji
    await openPage(audit, '/family', { waitMs: 2500 });
    const h1 = await audit.page.locator('h1').first().innerText().catch(() => '');
    const emojiCount = (h1.match(/👨/g) || []).length;
    check('family H1 emoji 不重复', emojiCount <= 1, `H1="${h1}"`);

    // 6. /messages 浏览器 title
    await openPage(audit, '/messages', { waitMs: 2500 });
    const title = await audit.page.title();
    check('messages 标题统一为 AI 对话', title.includes('AI 对话'), title);

    // 7. /model-benchmark 标题
    await openPage(audit, '/model-benchmark', { waitMs: 2500 });
    const bTitle = await audit.page.title();
    const bH1 = await audit.page.locator('h1').first().innerText().catch(() => '');
    check('model-benchmark 标题统一', !bTitle.includes('benchmark') && bH1.includes('模型评测'), `title=${bTitle} h1=${bH1}`);

    // 8. /dashboard 较昨天
    await openPage(audit, '/dashboard', { waitMs: 3000 });
    txt = await audit.page.locator('body').innerText().catch(() => '');
    check('dashboard 文案"较昨天"', !txt.includes('vs 昨天'), txt.includes('较昨天') ? '已改为较昨天' : '未找到对比文案');

    console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
    console.log('console 错误:', audit.issues.consoleErrors.length ? audit.issues.consoleErrors.slice(0, 3) : '无');
    console.log('page 错误:', audit.issues.pageErrors.length ? audit.issues.pageErrors.slice(0, 3) : '无');
  } finally {
    await audit.browser.close();
  }
  process.exit(fail > 0 ? 1 : 0);
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
