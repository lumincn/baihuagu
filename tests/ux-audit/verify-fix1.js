// 第一批优化验证：lang=zh-CN + 记账/OpenVINO/师父删除确认弹窗
const { startAudit, openPage, shot } = require('./login');

(async () => {
  const audit = await startAudit();
  let pass = 0, fail = 0;
  const check = (name, ok, extra = '') => {
    console.log(`${ok ? '✅' : '❌'} ${name}${extra ? ' — ' + extra : ''}`);
    ok ? pass++ : fail++;
  };
  try {
    // 1. lang=zh-CN
    await openPage(audit, '/', { waitMs: 1500 });
    const lang = await audit.page.getAttribute('html', 'lang');
    check('html lang=zh-CN', lang === 'zh-CN', `lang=${lang}`);
    await shot(audit, 'fix1-home');

    // 2. 记账删除确认
    await openPage(audit, '/family-budget', { waitMs: 2500 });
    // 创建一条测试账目（支出 1.00 餐饮）
    await audit.page.locator('input[type="number"]').first().fill('1.00').catch(() => {});
    await audit.page.locator('select').first().selectOption({ label: '餐饮' }).catch(() => {});
    const addBtn = audit.page.locator('button', { hasText: '记一笔' }).first()
      .or(audit.page.locator('button', { hasText: '添加' }).first());
    await addBtn.click().catch(() => {});
    await audit.page.waitForTimeout(1500);
    const delBtn = audit.page.locator('button[title="删除"]').first();
    if (await delBtn.count()) {
      await delBtn.click();
      await audit.page.waitForTimeout(800);
      const modalVisible = await audit.page.locator('.modal-overlay').isVisible().catch(() => false);
      const modalText = modalVisible ? await audit.page.locator('.modal-content').innerText() : '';
      check('记账删除弹出确认', modalVisible && modalText.includes('确认删除'), modalText.replace(/\n/g, ' ').slice(0, 60));
      await shot(audit, 'fix1-budget-modal');
      // 确认删除（清掉测试数据）
      await audit.page.locator('.modal-content button', { hasText: '确认删除' }).click();
      await audit.page.waitForTimeout(1500);
      const rowCount = await audit.page.locator('button[title="删除"]').count();
      check('记账删除确认后生效', rowCount === 0, `剩余行: ${rowCount}`);
    } else {
      check('记账删除弹出确认', false, '未找到删除按钮（可能没创建成功）');
    }

    // 3. OpenVINO 删除确认（打开弹窗即取消，不真删）
    await openPage(audit, '/local-models', { waitMs: 4000 });
    const ovTab = audit.page.locator('button', { hasText: 'OpenVINO' }).first();
    if (await ovTab.count()) { await ovTab.click(); await audit.page.waitForTimeout(2000); }
    const ovDel = audit.page.locator('button', { hasText: '删除' }).first();
    if (await ovDel.count()) {
      await ovDel.click();
      await audit.page.waitForTimeout(800);
      const ovModal = await audit.page.locator('.modal-overlay').isVisible().catch(() => false);
      const ovText = ovModal ? await audit.page.locator('.modal-content').innerText().catch(() => '') : '';
      check('OpenVINO 删除弹出确认', ovModal && ovText.includes('删除模型'), ovText.replace(/\n/g, ' ').slice(0, 60));
      await shot(audit, 'fix1-openvino-modal');
      // 取消，不真删
      await audit.page.locator('.modal-content button', { hasText: '取消' }).click().catch(() => {});
    } else {
      check('OpenVINO 删除弹出确认', false, '无已下载模型行');
    }

    // 4. 删除师父确认
    await openPage(audit, '/master-chat', { waitMs: 3000 });
    const masterCard = audit.page.locator('.master-card').first();
    if (await masterCard.count()) {
      await masterCard.click();
      await audit.page.waitForTimeout(1200);
      const mDel = audit.page.locator('button', { hasText: '删除师父' }).first();
      if (await mDel.count()) {
        await mDel.click();
        await audit.page.waitForTimeout(800);
        const mModal = await audit.page.locator('.modal-overlay').isVisible().catch(() => false);
        const mText = mModal ? await audit.page.locator('.modal-content').innerText().catch(() => '') : '';
        check('删除师父弹出确认', mModal && mText.includes('确认删除'), mText.replace(/\n/g, ' ').slice(0, 60));
        await shot(audit, 'fix1-master-modal');
        await audit.page.locator('.modal-content button', { hasText: '取消' }).click().catch(() => {});
      } else { check('删除师父弹出确认', false, '未找到删除师父按钮'); }
    } else { check('删除师父弹出确认', false, '无师父卡片'); }

    console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
    console.log('console 错误:', audit.issues.consoleErrors.length ? audit.issues.consoleErrors : '无');
  } finally {
    await audit.browser.close();
  }
  process.exit(fail > 0 ? 1 : 0);
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
