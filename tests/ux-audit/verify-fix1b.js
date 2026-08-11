// 第一批优化验证 v2：lang + 记账/OpenVINO/师父删除确认（修正选择器）
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

    // 2. 记账删除确认（创建一条 → 删除弹窗 → 确认删除 → 无残留）
    await openPage(audit, '/family-budget', { waitMs: 2500 });
    await audit.page.locator('input[type="number"]').first().fill('1.00');
    // 增强下拉（可搜索）：点触发 → 选"餐饮"
    await audit.page.locator('.enhanced-select-trigger').first().click();
    await audit.page.waitForTimeout(600);
    const catOption = audit.page.locator('.enhanced-select-option[data-value="餐饮"]').first();
    if (await catOption.count()) {
      await catOption.click();
    } else {
      // 备选：选第一个非空选项
      await audit.page.locator('.enhanced-select-option').nth(1).click().catch(() => {});
    }
    await audit.page.waitForTimeout(400);
    await audit.page.locator('button', { hasText: /^保存/ }).first().click();
    await audit.page.waitForTimeout(1800);
    const delBtn = audit.page.locator('button[title="删除"]').first();
    if (await delBtn.count()) {
      await delBtn.click();
      await audit.page.waitForTimeout(800);
      const modalVisible = await audit.page.locator('.modal-overlay').isVisible().catch(() => false);
      const modalText = modalVisible ? await audit.page.locator('.modal-content').innerText() : '';
      check('记账删除弹出确认', modalVisible && modalText.includes('确认删除'), modalText.replace(/\n/g, ' ').slice(0, 70));
      await shot(audit, 'fix1-budget-modal');
      await audit.page.locator('.modal-content button', { hasText: '确认删除' }).click();
      await audit.page.waitForTimeout(1500);
      const rowCount = await audit.page.locator('button[title="删除"]').count();
      check('记账删除确认后生效并清理', rowCount === 0, `剩余行: ${rowCount}`);
    } else {
      const pageText = await audit.page.locator('body').innerText();
      check('记账删除弹出确认', false, '未创建成功；页面含: ' + pageText.replace(/\n/g, ' ').slice(0, 80));
    }

    // 3. OpenVINO 删除确认（弹窗 → 取消，不真删）
    await openPage(audit, '/local-models', { waitMs: 4000 });
    const ovTab = audit.page.locator('.nav-tabs button', { hasText: 'OpenVINO' }).first();
    if (await ovTab.count()) {
      await ovTab.click();
      // 页面首屏可能长时间“刷新中...”，轮询等待删除按钮出现（最多 45s）
      let ovDel = audit.page.locator('button:visible', { hasText: '删除' }).first();
      for (let i = 0; i < 45 && (await ovDel.count()) === 0; i++) {
        await audit.page.waitForTimeout(1000);
      }
      if ((await ovDel.count()) > 0) {
        await ovDel.click();
        await audit.page.waitForTimeout(800);
        const ovModal = await audit.page.locator('.modal-overlay').isVisible().catch(() => false);
        const ovText = ovModal ? await audit.page.locator('.modal-content').innerText().catch(() => '') : '';
        check('OpenVINO 删除弹出确认', ovModal && ovText.includes('删除模型'), ovText.replace(/\n/g, ' ').slice(0, 70));
        await shot(audit, 'fix1-openvino-modal');
        await audit.page.locator('.modal-content button', { hasText: '取消' }).first().click().catch(() => {});
      } else {
        check('OpenVINO 删除弹出确认', false, '等待 45s 后仍无删除按钮');
      }
    } else {
      check('OpenVINO 删除弹出确认', false, '未找到 OpenVINO tab');
    }

    // 4. 删除师父确认（选卡片 → 删除按钮 → 弹窗 → 取消）
    await openPage(audit, '/master-chat', { waitMs: 3000 });
    const masterCard = audit.page.locator('.master-card').first();
    if (await masterCard.count()) {
      await masterCard.click();
      await audit.page.waitForTimeout(2000);
      const mDel = audit.page.locator('.master-actions button').last();
      if (await mDel.count()) {
        await mDel.click();
        await audit.page.waitForTimeout(800);
        const mModal = await audit.page.locator('.modal-overlay').isVisible().catch(() => false);
        const mText = mModal ? await audit.page.locator('.modal-content').innerText().catch(() => '') : '';
        check('删除师父弹出确认', mModal && mText.includes('确认删除'), mText.replace(/\n/g, ' ').slice(0, 70));
        await shot(audit, 'fix1-master-modal');
        await audit.page.locator('.modal-content button', { hasText: '取消' }).first().click().catch(() => {});
      } else { check('删除师父弹出确认', false, 'master-actions 无按钮'); }
    } else { check('删除师父弹出确认', false, '无师父卡片'); }

    console.log(`\n结果: ${pass} 通过 / ${fail} 失败`);
  } finally {
    await audit.browser.close();
  }
  process.exit(fail > 0 ? 1 : 0);
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
