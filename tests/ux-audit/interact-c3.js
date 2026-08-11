// SmartSelect 级联测试：/model-benchmark（不点开始测试）+ /local-models Ollama tab 加载完成后的内容
const { startAudit, openPage, shot } = require('./login');
const fs = require('fs');
const results = [];
const log = (name, data) => { results.push({ name, ...data }); console.log('STEP', name, JSON.stringify(data).slice(0, 350)); };
const T = (p) => p.catch(e => 'ERR:' + String(e).slice(0, 120));

(async () => {
  const audit = await startAudit();
  const { page, issues } = audit;

  // ---------- /model-benchmark SmartSelect ----------
  await openPage(audit, '/model-benchmark', { waitMs: 2500 });
  const trigger = page.locator('.smart-select-trigger').first();
  log('mb_trigger', { count: await T(trigger.count()) });
  await T(trigger.click());
  await page.waitForTimeout(800);
  const optCount = await T(page.locator('.smart-select-option').count());
  const optTexts = await T(page.locator('.smart-select-option').allInnerTexts());
  log('mb_dropdown_opts', { optCount, optTexts });
  await shot(audit, 'C_model-benchmark_dropdown');
  // 选择 DeepSeek (官方)
  const deepseekOpt = page.locator('.smart-select-option', { hasText: 'DeepSeek' }).first();
  if (await T(deepseekOpt.count())) { await T(deepseekOpt.click()); await page.waitForTimeout(1500); }
  const provVal = await T(page.locator('.smart-select-value').first().innerText());
  const modelOpts = await T(page.locator('select').nth(1).locator('option').allInnerTexts());
  const modelSel2 = await T(page.locator('.smart-select').nth(1).locator('.smart-select-value').innerText());
  log('mb_after_provider', { provVal, modelOpts, modelSel2 });
  await shot(audit, 'C_model-benchmark_provider_chosen');

  // ---------- /local-models：Ollama tab 加载完成后（等 20s） ----------
  await openPage(audit, '/local-models', { waitMs: 20000 });
  const body1 = await T(page.locator('body').innerText());
  log('lm_overview_20s', { stillSpinner: (body1 || '').includes('刷新中'), snippet: (body1 || '').split('百花（寻芳居）')[1]?.replace(/\s+/g, ' ').slice(0, 500) });
  await shot(audit, 'C_local-models_overview_20s');
  const ollamaTab = page.getByRole('button', { name: /Ollama/ }).first();
  await T(ollamaTab.click());
  await page.waitForTimeout(3000);
  const body2 = await T(page.locator('body').innerText());
  log('lm_ollama_tab_after_load', { stillSpinner: (body2 || '').includes('刷新中'), snippet: (body2 || '').split('百花（寻芳居）')[1]?.replace(/\s+/g, ' ').slice(0, 600) });
  await shot(audit, 'C_local-models_ollama_tab');

  fs.writeFileSync('interact-c3.json', JSON.stringify(results, null, 2), 'utf8');
  console.log('==== ISSUES ====');
  console.log(JSON.stringify(issues, null, 2).slice(0, 2500));
  await audit.browser.close();
})().catch(e => { console.error('FATAL', e); process.exit(1); });
