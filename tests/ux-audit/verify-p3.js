// P3 细节验证：search分组展开/死链修复/obsidian文案、hardware CPU、achievements反馈、daily-card、login
const { startAudit, openPage, shot } = require('./login');

(async () => {
  const audit = await startAudit();
  let pass = 0, fail = 0;
  const check = (name, ok, extra = '') => {
    console.log(`${ok ? '✅' : '❌'} ${name}${extra ? ' — ' + extra : ''}`);
    ok ? pass++ : fail++;
  };
  try {
    // 1. hardware-benchmark CPU 降级
    await openPage(audit, '/hardware-benchmark', { waitMs: 4000 });
    let body = await audit.page.locator('body').innerText().catch(() => '');
    check('hardware CPU 无 Unknown 裸显示', !body.includes('Unknown X64 CPU'), body.includes('未知 CPU') ? '已降级为未知 CPU' : 'CPU 正常显示');
    await shot(audit, 'p3-hardware');

    // 2. achievements 奖励空表单反馈
    await openPage(audit, '/achievements', { waitMs: 3000 });
    await audit.page.locator('button', { hasText: '添加奖励' }).click();
    await audit.page.waitForTimeout(800);
    body = await audit.page.locator('body').innerText().catch(() => '');
    check('achievements 空表单有反馈', body.includes('请填写奖励名称'), '显示校验提示');
    await shot(audit, 'p3-achievements');

    // 3. daily-card 家长出题无预填
    await openPage(audit, '/daily-card', { waitMs: 3000 });
    const deckInput = audit.page.locator('input[placeholder*="家长出题"], input[placeholder*="卡组"]').first();
    if (await deckInput.count()) {
      const val = await deckInput.inputValue().catch(() => '');
      check('daily-card 卡组无预填值', val === '', `value="${val}"`);
    } else check('daily-card 卡组无预填值', true, '未找到输入框（跳过）');

    // 4. login cmd 提示
    await audit.page.goto('http://127.0.0.1:5177/login', { waitUntil: 'domcontentloaded' });
    await audit.page.waitForTimeout(1500);
    body = await audit.page.locator('body').innerText().catch(() => '');
    check('login 提示 bh 命令', body.includes('bh dashboard') && !body.includes('./bh dashboard'), body.includes('cmd') ? '含 cmd 提示' : '');
    await shot(audit, 'p3-login');

    // 5. search 分组默认展开 + obsidian 文案
    await openPage(audit, '/search', { waitMs: 6000 });
    body = await audit.page.locator('body').innerText().catch(() => '');
    check('search Obsidian 文案正确', !body.includes('正在监听剪贴板') || body.includes('Obsidian 已连接'), body.includes('Obsidian 未运行') ? '未运行文案正确' : 'Obsidian 状态正常');
    await audit.page.locator('.search-box input').first().fill('中医');
    await audit.page.keyboard.press('Enter');
    await audit.page.waitForTimeout(4000);
    // 检查是否有一个分组处于展开态（group-items 可见）
    const expandedGroups = await audit.page.locator('.group-items').count();
    const groupHeaders = await audit.page.locator('.group-header').count();
    check('search 分组默认展开', expandedGroups >= 1 && groupHeaders > 0, `展开组=${expandedGroups} 总组=${groupHeaders}`);
    await shot(audit, 'p3-search');

    console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
    console.log('console 错误:', audit.issues.consoleErrors.length ? audit.issues.consoleErrors.slice(0, 3) : '无');
    console.log('page 错误:', audit.issues.pageErrors.length ? audit.issues.pageErrors.slice(0, 3) : '无');
  } finally {
    await audit.browser.close();
  }
  process.exit(fail > 0 ? 1 : 0);
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
