// /generate 灵感主题功能验证：渲染、点击填入、换一批
const { startAudit, openPage, shot } = require('./login');

(async () => {
  const audit = await startAudit();
  try {
    await openPage(audit, '/generate', { waitMs: 3000 });

    // 等主题卡片出现（AI 推荐异步加载，最多等 60s）
    let cards = 0;
    for (let i = 0; i < 60; i++) {
      cards = await audit.page.locator('.topic-card').count();
      if (cards > 0) break;
      await audit.page.waitForTimeout(1000);
    }
    console.log('topic-card 数量:', cards);
    if (cards === 0) {
      console.log('页面文本:', (await audit.page.locator('body').innerText()).slice(0, 400));
      throw new Error('未出现主题卡片');
    }

    await shot(audit, 'generate-topics');
    const firstTitle = await audit.page.locator('.topic-card .topic-title').first().innerText();
    const firstCat = await audit.page.locator('.topic-card .topic-cat').first().innerText();
    console.log('首张卡片:', `[${firstCat}] ${firstTitle}`);

    // 点击第一张卡片 → 主题输入框应填入
    await audit.page.locator('.topic-card').first().click();
    await audit.page.waitForTimeout(800);
    const inputVal = await audit.page.locator('input[type="text"].form-control').first().inputValue();
    console.log('点击后输入框值:', JSON.stringify(inputVal), '| 匹配:', inputVal === firstTitle ? '✅' : '❌');

    // 换一批按钮
    const refreshBtn = audit.page.locator('button', { hasText: '换一批' });
    const before = await audit.page.locator('.topic-card .topic-title').first().innerText();
    await refreshBtn.click();
    await audit.page.waitForTimeout(1000);
    let after = before, changed = false;
    for (let i = 0; i < 60; i++) {
      await audit.page.waitForTimeout(1000);
      after = await audit.page.locator('.topic-card .topic-title').first().innerText().catch(() => '');
      if (after && after !== before) { changed = true; break; }
    }
    console.log('换一批:', changed ? `✅ 已更新（${before} → ${after}）` : '❌ 内容未变化');
    await shot(audit, 'generate-topics-refreshed');

    console.log('console 错误:', audit.issues.consoleErrors.length ? audit.issues.consoleErrors : '无');
    console.log('page 错误:', audit.issues.pageErrors.length ? audit.issues.pageErrors : '无');
  } finally {
    await audit.browser.close();
  }
})().catch(e => { console.error('FAIL:', e.message); process.exit(1); });
