// /model-benchmark chip 级联测试 + /code-agent 结构
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');
const results = [];
const log = (name, data) => { results.push({ name, ...data }); console.log('STEP', name, JSON.stringify(data).slice(0, 350)); };
const T = (p) => p.catch(e => 'ERR:' + String(e).slice(0, 120));

(async () => {
  const audit = await startAudit();
  const { page } = audit;

  await openPage(audit, '/model-benchmark', { waitMs: 2500 });
  const chips0 = await T(page.locator('.enhanced-select-chip').allInnerTexts());
  log('mb_chips', { chips: chips0 });
  const deepseekChip = page.locator('.enhanced-select-chip', { hasText: 'DeepSeek' }).first();
  if (await T(deepseekChip.count())) {
    await T(deepseekChip.click());
    await page.waitForTimeout(1800);
  }
  const chips1 = await T(page.locator('.enhanced-select-chip').allInnerTexts());
  const sel1 = await T(page.locator('select').nth(1).locator('option').allInnerTexts());
  const sel1Sel = await T(page.locator('select').nth(1).evaluate(el => el.selectedOptions[0]?.textContent));
  log('mb_after_deepseek_chip', { chips: chips1, modelOpts: sel1, selectedModel: sel1Sel });
  await shot(audit, 'C_model-benchmark_chip_selected');

  // 编程大模型 tab
  await T(page.getByRole('button', { name: /编程大模型/ }).click());
  await page.waitForTimeout(1500);
  const chips2 = await T(page.locator('.enhanced-select-chip').allInnerTexts());
  log('mb_coding_chips', { chips: chips2 });
  await shot(audit, 'C_model-benchmark_coding_chips');

  // ---------- /code-agent：provider 芯片 ----------
  await openPage(audit, '/code-agent', { waitMs: 2500 });
  const caChips = await T(page.locator('.enhanced-select-chip').allInnerTexts());
  log('code_agent_chips', { chips: caChips });
  // 切到 OpenVINO (本地) 芯片，看模型下拉
  const ovChip = page.locator('.enhanced-select-chip', { hasText: 'OpenVINO (本地)' }).first();
  if (await T(ovChip.count())) { await T(ovChip.click()); await page.waitForTimeout(1500); }
  const caModelOpts = await T(page.locator('select').nth(2).locator('option').allInnerTexts());
  const caSelModel = await T(page.locator('select').nth(2).evaluate(el => el.selectedOptions[0]?.textContent));
  log('code_agent_after_ov', { modelOpts: caModelOpts, selected: caSelModel });
  await shot(audit, 'C_code-agent_ov_selected');

  fs.writeFileSync('interact-c4.json', JSON.stringify(results, null, 2), 'utf8');
  await audit.browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
