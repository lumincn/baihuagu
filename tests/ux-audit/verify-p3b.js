// P3 细节 v2：首页清空搜索、qr-tool 默认展开、tasks 隐藏已完成、leaderboard tooltip
const { startAudit, openPage, shot } = require('./login');

(async () => {
  const audit = await startAudit();
  let pass = 0, fail = 0;
  const check = (name, ok, extra = '') => {
    console.log(`${ok ? '✅' : '❌'} ${name}${extra ? ' — ' + extra : ''}`);
    ok ? pass++ : fail++;
  };
  try {
    // 1. 首页最近搜索清空按钮
    await openPage(audit, '/', { waitMs: 3000 });
    const clearBtn = audit.page.locator('button', { hasText: '清空' }).first();
    check('首页最近搜索有清空按钮', (await clearBtn.count()) > 0);
    await shot(audit, 'p3b-home');

    // 2. qr-tool 通用二维码默认展开
    await openPage(audit, '/qr-tool', { waitMs: 3500 });
    const generalBody = await audit.page.locator('.card-body.collapse.show', { hasText: '通用二维码' }).count()
      .catch(() => 0);
    const textareaVisible = await audit.page.locator('textarea.form-control').isVisible().catch(() => false);
    check('qr-tool 通用二维码默认展开', textareaVisible, textareaVisible ? '输入框可见' : '折叠');
    await shot(audit, 'p3b-qrtool');

    // 3. tasks 隐藏已完成开关
    await openPage(audit, '/tasks', { waitMs: 3000 });
    const hideSwitch = audit.page.locator('input[type="checkbox"]', { hasText: '隐藏已完成' }).first();
    const switchCount = await audit.page.locator('label', { hasText: '隐藏已完成' }).count();
    check('tasks 有隐藏已完成开关', switchCount > 0);
    await shot(audit, 'p3b-tasks');

    // 4. leaderboard 分数 tooltip
    await openPage(audit, '/leaderboard', { waitMs: 3000 });
    const scoreVal = audit.page.locator('.score-value').first();
    const title = await scoreVal.getAttribute('title').catch(() => '');
    check('leaderboard 分数公式提示', !!title && title.includes('分数'), title);
    await shot(audit, 'p3b-leaderboard');

    console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
    console.log('console 错误:', audit.issues.consoleErrors.length ? audit.issues.consoleErrors.slice(0, 3) : '无');
    console.log('page 错误:', audit.issues.pageErrors.length ? audit.issues.pageErrors.slice(0, 3) : '无');
  } finally {
    await audit.browser.close();
  }
  process.exit(fail > 0 ? 1 : 0);
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
